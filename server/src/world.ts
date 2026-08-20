import { classById, defaultBanner, defaultClass, defaultMap, itemById, mapById, monsters, npcs, skillById, spiritById, weaponById } from "./data.js";
import { emptyInventory, padInventory, pityFor, pityView, seedStarterInventory } from "./gacha.js";
import { loadGuest, type GuestSave } from "./persist.js";
import { applyPendingHit, applyStatusOnHit, entityBlockedAt, hitFromCaster, applyIncomingDamageMult } from "./combat.js";
import { resolveDamage, toCombatElement } from "./combat/damage.js";
import { loadCombatConfig } from "./combat/config.js";
import { bindInstanceHooks, resolveBaseMapId, tickInstances } from "./instance.js";
import { portalsOnMap } from "./portal.js";
import { noteKill } from "./quest.js";
import { addItem, removeItem } from "./shop.js";
import { starterSkillsFor } from "./skills.js";
import { grantXp } from "./xp.js";
import { applyKillLoot, killXpFor, lootTableFor, rollKillRewards } from "./loot.js";
import {
  addThreat,
  clearAllThreat,
  clearThreat,
  decayThreat,
  pruneDeadPlayers,
  seedThreat,
  syncThreatMessage,
  threatFromDamage,
  topThreatId,
} from "./threat.js";
import type {
  CooldownEntry,
  Entity,
  LiveProjectile,
  MapDef,
  MonsterDef,
  PlayerSession,
  PityView,
  ResistMap,
  ServerMessage,
  StatusInstance,
} from "./types.js";
import type { NpcDef } from "./data.js";

let nextId = 1;

export function createId(prefix: string): string {
  nextId += 1;
  return `${prefix}_${nextId}`;
}

export const players = new Map<string, PlayerSession>();
export const liveMonsters = new Map<string, Entity>();
export const liveNpcs = new Map<string, Entity>();
export const npcLines = new Map<string, string>();
export const npcInteract = new Map<string, NpcDef["interact"]>();
export const npcSwitchIds = new Map<string, string>();
export const monsterMeta = new Map<string, MonsterDef>();
export const monsterStatuses = new Map<string, StatusInstance[]>();
export const monsterHome = new Map<string, { x: number; y: number }>();
/** @deprecated binary aggro — use threat table; kept as derived top target for tests */
export const monsterAggro = new Map<string, string | null>();
export const liveProjectiles = new Map<string, LiveProjectile>();

type PendingRespawn = {
  def: MonsterDef;
  entityId: string;
  at: number;
};

const pendingRespawns: PendingRespawn[] = [];
const monsterAttackReady = new Map<string, number>();
/** Boss phase reached: 0 = none, 1 = <66%, 2 = <33% */
export const bossPhase = new Map<string, number>();
const RESPAWN_MS = 8000;
let lastThreatDecayAt = 0;
let lastMonsterAiAt = 0;

type AiPhase = "idle" | "patrol" | "chase" | "attack" | "return";
type MonsterAi = { phase: AiPhase; fixateId: string | null; patrolT: number; windupUntil: number };
const monsterAi = new Map<string, MonsterAi>();

function ensureMonsterAi(id: string): MonsterAi {
  let ai = monsterAi.get(id);
  if (!ai) {
    ai = { phase: "idle", fixateId: null, patrolT: Math.random() * Math.PI * 2, windupUntil: 0 };
    monsterAi.set(id, ai);
  }
  return ai;
}

export function isBossMonster(def: { id?: string; monsterType?: string } | undefined): boolean {
  if (!def) {
    return false;
  }
  const t = def.monsterType ?? "";
  const id = def.id ?? "";
  return t === "boss" || t.startsWith("tower_boss") || id.includes("boss") || id.includes("colossus") ||
    id.includes("warden") || id.includes("apex");
}

/** Simple priority list per enemy type — MVP, not a utility AI. */
function monsterSkillPriority(def: MonsterDef): string[] {
  if (def.monsterType === "immortal" || def.atk <= 0) {
    return [];
  }
  if (def.id === "cannon" || def.monsterType === "cannon") {
    return ["cannon_flame"];
  }
  const t = def.monsterType ?? def.id;
  if (isBossMonster(def)) {
    return ["shockwave", "auto"];
  }
  return ["auto"];
}

function monsterBlockedAt(monster: Entity, nx: number, ny: number): boolean {
  const map = mapById(resolveBaseMapId(monster.mapId)) ?? defaultMap;
  if (nx < 0 || ny < 0 || nx > map.width - 1 || ny > map.height - 1) {
    return true;
  }
  const txr = Math.round(nx);
  const tyr = Math.round(ny);
  if (map.blocked.some((tile) => tile.x === txr && tile.y === tyr)) {
    return true;
  }
  // Ignore players so chase can enter melee; still blocked by NPCs / solids.
  const playersIgnore = [...players.values()].map((p) => p.entity.id);
  return entityBlockedAt(nx, ny, monster, playersIgnore);
}

function tryMoveMonster(monster: Entity, tx: number, ty: number, step: number): boolean {
  const dx = tx - monster.x;
  const dy = ty - monster.y;
  const dist = Math.hypot(dx, dy);
  if (dist < 0.05 || step <= 0) {
    return false;
  }
  const ux = dx / dist;
  const uy = dy / dist;
  const limit = Math.min(step, dist);
  const directX = monster.x + ux * limit;
  const directY = monster.y + uy * limit;
  if (!monsterBlockedAt(monster, directX, directY)) {
    monster.x = directX;
    monster.y = directY;
    return true;
  }
  const xOnly = monster.x + ux * limit;
  const xOnlyOpen = Math.abs(dx) > 0.05 && !monsterBlockedAt(monster, xOnly, monster.y);
  if (xOnlyOpen) {
    monster.x = xOnly;
    return true;
  }
  const yOnly = monster.y + uy * limit;
  const yOnlyOpen = Math.abs(dy) > 0.05 && !monsterBlockedAt(monster, monster.x, yOnly);
  // Only slide on Y when we are not blocked on X (otherwise we step back into the wall).
  if (yOnlyOpen && Math.abs(dx) <= 0.05) {
    monster.y = yOnly;
    return true;
  }
  const perps: Array<[number, number]> = [
    [monster.x - uy * limit, monster.y + ux * limit],
    [monster.x + uy * limit, monster.y - ux * limit],
  ];
  perps.sort((a, b) => {
    const aNext = !monsterBlockedAt(monster, a[0] + Math.sign(dx) * limit, a[1]) ? 1 : 0;
    const bNext = !monsterBlockedAt(monster, b[0] + Math.sign(dx) * limit, b[1]) ? 1 : 0;
    return bNext - aNext;
  });
  for (const [nx, ny] of perps) {
    if (!monsterBlockedAt(monster, nx, ny)) {
      monster.x = nx;
      monster.y = ny;
      return true;
    }
  }
  return false;
}

const zeroResist = (): ResistMap => ({
  wind: 0, fire: 0, water: 0, earth: 0, holy: 0, dark: 0,
});

export function resetWorld(): void {
  players.clear();
  liveMonsters.clear();
  liveNpcs.clear();
  npcLines.clear();
  npcInteract.clear();
  npcSwitchIds.clear();
  monsterMeta.clear();
  monsterStatuses.clear();
  monsterHome.clear();
  monsterAggro.clear();
  liveProjectiles.clear();
  clearAllThreat();
  lootOwner.clear();
  bossPhase.clear();
  pendingRespawns.length = 0;
  monsterAttackReady.clear();
  monsterAi.clear();
  lastThreatDecayAt = 0;
  lastMonsterAiAt = 0;
  nextId = 1;
  for (const def of monsters) {
    if (def.mapId.startsWith("dungeon_") || def.mapId.startsWith("tower_boss_")) {
      continue;
    }
    spawnMonster(def, def.respawnId, def.x, def.y);
  }
  for (const def of npcs) {
    spawnNpc(def);
  }
}

function spawnNpc(def: NpcDef): Entity {
  const entity: Entity = {
    id: def.id,
    kind: "npc",
    name: def.name,
    x: def.x,
    y: def.y,
    hp: 1,
    maxHp: 1,
    mp: 0,
    maxMp: 0,
    atk: 0,
    magicAtk: 0,
    def: 0,
    magicResist: 0,
    attackSpeed: 1,
    hpRegen: 0,
    mpRegen: 0,
    critChance: 0,
    critDamage: 1,
    moveSpeed: 0,
    hitRadius: def.hitRadius,
    resist: zeroResist(),
    mapId: def.mapId,
  };
  liveNpcs.set(def.id, entity);
  npcLines.set(def.id, def.line);
  npcInteract.set(def.id, def.interact);
  if (def.switchId) {
    npcSwitchIds.set(def.id, def.switchId);
  }
  return entity;
}

export function spawnMonster(def: MonsterDef, id: string, x: number, y: number): Entity {
  const entity: Entity = {
    id,
    kind: "monster",
    name: def.name,
    x,
    y,
    hp: def.hp,
    maxHp: def.hp,
    mp: 0,
    maxMp: 0,
    atk: def.atk,
    magicAtk: Math.floor(def.atk * 0.5),
    def: def.def,
    magicResist: def.magicResist,
    attackSpeed: 1,
    hpRegen: 0,
    mpRegen: 0,
    critChance: 0.02,
    critDamage: 1.4,
    moveSpeed: def.prefer === "ranged" ? 2.5 : 2,
    hitRadius: def.hitRadius,
    resist: zeroResist(),
    element: def.element,
    mapId: def.mapId,
  };
  entity.resist[def.element] = 15;
  liveMonsters.set(id, entity);
  monsterMeta.set(id, def);
  monsterHome.set(id, { x: def.x, y: def.y });
  monsterAggro.set(id, null);
  clearThreat(id);
  monsterAttackReady.set(id, 0);
  monsterStatuses.set(id, []);
  return entity;
}

export function isImmortalMonster(id: string): boolean {
  const def = monsterMeta.get(id);
  return Boolean(def && def.monsterType === "immortal");
}

export function clampImmortalHp(entity: Entity): void {
  if (!isImmortalMonster(entity.id)) {
    return;
  }
  if (entity.hp < 1) {
    entity.hp = 1;
  }
}

export function despawnMonster(id: string): void {
  liveMonsters.delete(id);
  monsterMeta.delete(id);
  monsterHome.delete(id);
  monsterAggro.delete(id);
  monsterStatuses.delete(id);
  monsterAttackReady.delete(id);
  clearThreat(id);
}

function applySave(session: PlayerSession, save: GuestSave): void {
  const cls = classById(save.classId) ?? defaultClass;
  session.classId = cls.id;
  session.characterId = save.characterId;
  session.slotIndex = save.slotIndex ?? session.slotIndex ?? 0;
  if (save.name) {
    session.entity.name = save.name.slice(0, 16);
  }
  session.homeMapId = save.homeMapId && mapById(save.homeMapId) ? save.homeMapId : defaultMap.id;
  session.homeX = save.homeX ?? defaultMap.spawn.x;
  session.homeY = save.homeY ?? defaultMap.spawn.y;
  if (save.mapId && mapById(resolveBaseMapId(save.mapId))) {
    session.entity.mapId = resolveBaseMapId(save.mapId);
    session.entity.x = save.x;
    session.entity.y = save.y;
  } else if (session.homeMapId && mapById(session.homeMapId)) {
    session.entity.mapId = session.homeMapId;
    session.entity.x = session.homeX;
    session.entity.y = session.homeY;
  } else {
    session.entity.mapId = defaultMap.id;
    session.entity.x = defaultMap.spawn.x;
    session.entity.y = defaultMap.spawn.y;
  }
  session.entity.hp = Math.min(session.entity.maxHp, save.hp || session.entity.hp);
  session.entity.mp = Math.min(session.entity.maxMp, save.mp || session.entity.mp);
  session.inventory = padInventory(save.inventory);
  session.pity = save.pity ?? {};
  session.gold = save.gold ?? session.gold;
  session.quests = save.quests ?? [];
  session.completedQuestIds = save.completedQuestIds ?? [];
  session.charNameSet = Boolean(save.charNameSet || save.name);
  session.level = save.level ?? 1;
  session.xp = save.xp ?? 0;
  session.skillPoints = save.skillPoints ?? 0;
  if (Array.isArray(save.unlockedSkillIds) && save.unlockedSkillIds.length) {
    session.unlockedSkillIds = save.unlockedSkillIds;
  }
  session.equippedArmorId = save.equippedArmorId ?? null;
  session.equippedHelmId = save.equippedHelmId ?? null;
  session.equippedBootsId = save.equippedBootsId ?? null;
  session.equippedGlovesId = save.equippedGlovesId ?? null;
  session.equippedAccessoryId = save.equippedAccessoryId ?? null;
  session.classCardId = save.classCardId ?? null;
  session.equippedSkinId = save.equippedSkinId ?? null;
  session.towerClearedFloor = save.towerClearedFloor ?? 0;
  session.switchFlags = save.switchFlags ?? {};
  session.friends = Array.isArray(save.friends)
    ? save.friends
        .filter((f) => f && typeof f.guestToken === "string" && typeof f.name === "string")
        .slice(0, 40)
    : [];
  if (save.weaponIds?.length) {
    session.weaponIds = save.weaponIds.filter((id) => !!weaponById(id));
    if (!session.weaponIds.length) {
      session.weaponIds = [...cls.startingWeaponIds];
    }
  }
  if (save.equippedWeaponId && weaponById(save.equippedWeaponId) && session.weaponIds.includes(save.equippedWeaponId)) {
    session.equippedWeaponId = save.equippedWeaponId;
  } else {
    session.equippedWeaponId = cls.startingWeaponId;
  }
  if (save.equippedWeapon2Id && weaponById(save.equippedWeapon2Id) && session.weaponIds.includes(save.equippedWeapon2Id)) {
    session.equippedWeapon2Id = save.equippedWeapon2Id;
  } else {
    session.equippedWeapon2Id = null;
  }
  if (save.spiritIds?.length) {
    session.spiritIds = save.spiritIds.filter((id) => !!spiritById(id));
  }
  if (save.equippedSpiritId === null) {
    session.equippedSpiritId = null;
  } else if (save.equippedSpiritId && spiritById(save.equippedSpiritId) && session.spiritIds.includes(save.equippedSpiritId)) {
    session.equippedSpiritId = save.equippedSpiritId;
  }
}

function buildPlayerEntity(): Entity {
  return {
    id: createId("player"),
    kind: "player",
    name: defaultClass.name,
    x: defaultMap.spawn.x,
    y: defaultMap.spawn.y,
    hp: defaultClass.hp,
    maxHp: defaultClass.hp,
    mp: defaultClass.mp,
    maxMp: defaultClass.mp,
    atk: defaultClass.atk,
    magicAtk: defaultClass.magicAtk,
    def: defaultClass.def,
    magicResist: defaultClass.magicResist,
    attackSpeed: defaultClass.attackSpeed,
    hpRegen: defaultClass.hpRegen,
    mpRegen: defaultClass.mpRegen,
    critChance: defaultClass.critChance,
    critDamage: defaultClass.critDamage,
    moveSpeed: defaultClass.moveSpeed,
    hitRadius: defaultClass.hitRadius,
    resist: { ...defaultClass.resist },
    weaponId: defaultClass.startingWeaponId,
    spiritId: defaultClass.startingSpiritId,
    mapId: defaultMap.id,
  };
}

export function spawnPlayer(guestToken = "", opts?: { slotIndex?: number; enterWorld?: boolean; save?: GuestSave | null }): PlayerSession {
  const token = guestToken || createId("guest");
  const enterWorld = opts?.enterWorld !== false;
  const slotIndex = opts?.slotIndex ?? 0;
  const entity = buildPlayerEntity();
  const session: PlayerSession = {
    entity,
    classId: defaultClass.id,
    guestToken: token,
    characterId: undefined,
    slotIndex,
    lastActionAt: 0,
    lastMoveAt: Date.now(),
    facingX: 1,
    facingY: 0,
    actionTimes: [],
    moveTimes: [],
    skillReadyAt: {},
    inventory: emptyInventory(),
    pity: {},
    statuses: [],
    weaponIds: [...defaultClass.startingWeaponIds],
    equippedWeaponId: defaultClass.startingWeaponId,
    equippedWeapon2Id:
      defaultClass.startingWeaponIds.find((id) => id !== defaultClass.startingWeaponId) ?? null,
    spiritIds: [...defaultClass.startingSpiritIds],
    equippedSpiritId: defaultClass.startingSpiritId,
    moveLockUntil: 0,
    partyId: null,
    guildId: null,
    gold: 100,
    homeMapId: defaultMap.id,
    homeX: defaultMap.spawn.x,
    homeY: defaultMap.spawn.y,
    quests: [],
    completedQuestIds: [],
    charNameSet: false,
    homestoneReadyAt: 0,
    unlockedSkillIds: starterSkillsFor(defaultClass.id),
    skillPoints: 0,
    level: 1,
    xp: 0,
    equippedArmorId: null,
    equippedHelmId: null,
    equippedBootsId: null,
    equippedGlovesId: null,
    equippedAccessoryId: null,
    friends: [],
    classCardId: null,
    equippedSkinId: null,
    towerClearedFloor: 0,
    switchFlags: {},
    inWorld: enterWorld,
  };

  const save =
    opts && "save" in opts
      ? opts.save ?? null
      : enterWorld
        ? loadGuest(token)
        : null;
  if (save) {
    applySave(session, save);
  } else if (enterWorld) {
    seedStarterInventory(session.inventory);
    addItem(session, "item_homestone", 1);
  }
  if (enterWorld && !session.inventory.some((s) => s.itemId)) {
    seedStarterInventory(session.inventory);
    addItem(session, "item_homestone", 1);
  }
  if (session.entity.hp <= 0) {
    respawnAtHome(session);
  }
  ensureSecondaryWeapon(session);
  applyGearStats(session);
  players.set(entity.id, session);
  return session;
}

function ensureSecondaryWeapon(session: PlayerSession): void {
  if (session.equippedWeapon2Id && weaponById(session.equippedWeapon2Id)) {
    return;
  }
  const cls = classById(session.classId) ?? defaultClass;
  const sec = cls.startingWeaponIds.find((id) => id !== session.equippedWeaponId);
  if (!sec || !weaponById(sec)) {
    return;
  }
  if (!session.weaponIds.includes(sec)) {
    session.weaponIds.push(sec);
  }
  session.equippedWeapon2Id = sec;
}

/** Class base + gear resists only. Weapon atk/magic bonuses applied once in combat. */
export function applyGearStats(session: PlayerSession): void {
  const base = classById(session.classId) ?? defaultClass;
  const weapon = weaponById(session.equippedWeaponId);
  const secondary = session.equippedWeapon2Id ? weaponById(session.equippedWeapon2Id) : undefined;
  const spirit = session.equippedSpiritId ? spiritById(session.equippedSpiritId) : undefined;
  session.entity.atk = base.atk + Math.floor((session.level - 1) * 1);
  session.entity.magicAtk = base.magicAtk + Math.floor((session.level - 1) * 0.5);
  session.entity.def = base.def + Math.floor((session.level - 1) * 0.5);
  session.entity.magicResist = base.magicResist;
  session.entity.moveSpeed = base.moveSpeed;
  session.entity.resist = { ...base.resist };
  if (weapon?.resistBonus) {
    for (const [k, v] of Object.entries(weapon.resistBonus)) {
      session.entity.resist[k as keyof typeof session.entity.resist] += v ?? 0;
    }
  }
  // Secondary weapon: passive resist + small atk until swapped to primary
  if (secondary) {
    session.entity.atk += Math.max(1, Math.floor((secondary.atkBonus ?? 2) / 2));
    if (secondary.resistBonus) {
      for (const [k, v] of Object.entries(secondary.resistBonus)) {
        session.entity.resist[k as keyof typeof session.entity.resist] += Math.floor((v ?? 0) / 2);
      }
    }
  }
  if (spirit?.resistBonus) {
    for (const [k, v] of Object.entries(spirit.resistBonus)) {
      session.entity.resist[k as keyof typeof session.entity.resist] += v ?? 0;
    }
  }
  const gearIds = [
    session.equippedArmorId,
    session.equippedHelmId,
    session.equippedBootsId,
    session.equippedGlovesId,
    session.equippedAccessoryId,
  ];
  for (const gid of gearIds) {
    if (!gid) continue;
    const item = itemById(gid);
    if (!item) continue;
    session.entity.def += item.defBonus ?? 0;
    session.entity.atk += item.atkBonus ?? 0;
    session.entity.magicAtk += item.magicAtkBonus ?? 0;
    session.entity.moveSpeed += item.moveSpeedBonus ?? 0;
    if (item.resistBonus) {
      for (const [k, v] of Object.entries(item.resistBonus)) {
        session.entity.resist[k as keyof typeof session.entity.resist] += v ?? 0;
      }
    }
  }
  if (session.classCardId) {
    const card = itemById(session.classCardId);
    if (card?.resistBonus) {
      for (const [k, v] of Object.entries(card.resistBonus)) {
        session.entity.resist[k as keyof typeof session.entity.resist] += v ?? 0;
      }
    }
  }
  session.entity.weaponId = session.equippedWeaponId;
  session.entity.spiritId = session.equippedSpiritId;
  session.entity.element = spirit?.element ?? weapon?.element;
  session.entity.attackSpeed = base.attackSpeed;
  session.entity.critChance = base.critChance;
  session.entity.critDamage = base.critDamage;
  session.entity.hpRegen = base.hpRegen;
  session.entity.mpRegen = base.mpRegen;
  session.entity.maxHp = base.hp + Math.floor((session.level - 1) * 8);
  // TEST: ×20 MP for gray-box skill spam — revert later.
  const mpTestMult = 20;
  session.entity.maxMp = (base.mp + Math.floor((session.level - 1) * 4)) * mpTestMult;
  session.entity.hp = Math.min(session.entity.hp, session.entity.maxHp);
  session.entity.mp = Math.min(session.entity.maxMp, Math.max(session.entity.mp, session.entity.maxMp));
  session.entity.hitRadius = base.hitRadius;
}

/** @deprecated use applyGearStats */
export function applyWeaponStats(session: PlayerSession): void {
  applyGearStats(session);
}

export function equipWeapon(session: PlayerSession, weaponId: string): ServerMessage | { ok: true } {
  if (!session.weaponIds.includes(weaponId) || !weaponById(weaponId)) {
    return { type: "error", code: "unknown_weapon", message: `Cannot equip ${weaponId}` };
  }
  session.equippedWeaponId = weaponId;
  applyGearStats(session);
  return { ok: true };
}

export function equipSpirit(session: PlayerSession, spiritId: string | null): ServerMessage | { ok: true } {
  if (spiritId === null) {
    session.equippedSpiritId = null;
    applyGearStats(session);
    return { ok: true };
  }
  if (!session.spiritIds.includes(spiritId) || !spiritById(spiritId)) {
    return { type: "error", code: "unknown_spirit", message: `Cannot equip ${spiritId}` };
  }
  session.equippedSpiritId = spiritId;
  applyGearStats(session);
  return { ok: true };
}

export function equipGear(
  session: PlayerSession,
  slot: "armor" | "helm" | "boots" | "gloves" | "accessory",
  itemId: string | null,
): ServerMessage | { ok: true } {
  const field =
    slot === "armor"
      ? "equippedArmorId"
      : slot === "helm"
        ? "equippedHelmId"
        : slot === "boots"
          ? "equippedBootsId"
          : slot === "gloves"
            ? "equippedGlovesId"
            : "equippedAccessoryId";
  const prev = session[field];
  if (itemId === null) {
    if (prev) {
      addItem(session, prev, 1);
    }
    session[field] = null;
    applyGearStats(session);
    return { ok: true };
  }
  const item = itemById(itemId);
  if (!item || item.kind !== "armor" || item.slot !== slot) {
    return { type: "error", code: "bad_gear", message: "Wrong gear slot" };
  }
  const need = item.levelReq ?? 1;
  if (session.level < need) {
    return { type: "error", code: "level_too_low", message: `Need level ${need}` };
  }
  if (!session.inventory.some((s) => s.itemId === itemId && s.quantity > 0)) {
    return { type: "error", code: "missing_item", message: "Not in inventory" };
  }
  if (!removeItem(session, itemId, 1)) {
    return { type: "error", code: "missing_item", message: "Not in inventory" };
  }
  if (prev) {
    addItem(session, prev, 1);
  }
  session[field] = itemId;
  applyGearStats(session);
  return { ok: true };
}

export function currentPityView(session: PlayerSession): PityView {
  return pityView(defaultBanner, pityFor(session, defaultBanner.id));
}

export function cooldownSnapshot(session: PlayerSession): CooldownEntry[] {
  const cls = classById(session.classId) ?? defaultClass;
  return cls.skillIds.map((id) => {
    const skill = skillById(id);
    return {
      id,
      readyAt: session.skillReadyAt[id] ?? 0,
      cooldownMs: skill?.cooldownMs ?? 1000,
    };
  });
}

export function spawnProjectileFromCast(
  caster: Entity,
  cast: {
    speed: number;
    targetId: string;
    skillId: string;
    vx?: number;
    vy?: number;
    maxRange?: number;
    width?: number;
    pendingHits: { targetId: string; damage: number; crit: boolean }[];
    pendingStatus: StatusInstance | null;
    statusDurationMs: number;
  },
  mpAfter: number,
): { projectile: LiveProjectile; message: ServerMessage } {
  const id = createId("proj");
  const projectile: LiveProjectile = {
    id,
    casterId: caster.id,
    targetId: cast.targetId,
    skillId: cast.skillId,
    x: caster.x,
    y: caster.y,
    speed: cast.speed,
    vx: cast.vx,
    vy: cast.vy,
    traveled: 0,
    maxRange: cast.maxRange,
    width: cast.width,
    pendingHits: cast.pendingHits,
    pendingStatus: cast.pendingStatus,
    statusDurationMs: cast.statusDurationMs,
    mpAfter,
  };
  liveProjectiles.set(id, projectile);
  return {
    projectile,
    message: {
      type: "sync_projectile_spawn",
      projectile: {
        id,
        casterId: caster.id,
        targetId: cast.targetId,
        skillId: cast.skillId,
        x: caster.x,
        y: caster.y,
        speed: cast.speed,
        vx: cast.vx,
        vy: cast.vy,
      },
    },
  };
}

function distPointToSegment(px: number, py: number, ax: number, ay: number, bx: number, by: number): number {
  const abx = bx - ax;
  const aby = by - ay;
  const len2 = abx * abx + aby * aby;
  if (len2 < 1e-8) {
    return Math.hypot(px - ax, py - ay);
  }
  const t = Math.max(0, Math.min(1, ((px - ax) * abx + (py - ay) * aby) / len2));
  return Math.hypot(px - (ax + t * abx), py - (ay + t * aby));
}

/** Match client sprite scale so skillshots connect on the visible body, not only the feet origin. */
export function projectileCatchRadius(entity: Entity): number {
  const base = entity.hitRadius > 0 ? entity.hitRadius : 0.4;
  const id = entity.id ?? "";
  let scale = 2.2;
  if (id.includes("king")) {
    scale = 3.45;
  } else if (id.includes("ruins") || id.includes("colossus") || id.includes("apex") || id.includes("m_boss_f5")) {
    scale = 2.15;
  } else if (id.includes("boss") || id.includes("warden") || id.includes("crypt_lord")) {
    scale = 1.75;
  }
  return Math.max(base, scale * 0.5);
}

function reapDeadMonsters(now: number): ServerMessage[] {
  const out: ServerMessage[] = [];
  const aliveIds = new Set(
    [...players.values()].filter((s) => s.entity.hp > 0).map((s) => s.entity.id),
  );
  for (const [id, monster] of [...liveMonsters.entries()]) {
    if (monster.hp > 0 || isImmortalMonster(id)) {
      continue;
    }
    const mapId = monster.mapId;
    const topId = topThreatId(id, aliveIds, 1);
    out.push(...killMonster(id, now));
    const killer = (topId ? players.get(topId) : undefined) ??
      [...players.values()].find((p) => p.entity.mapId === mapId && p.entity.hp > 0);
    if (killer) {
      out.push(...onMonsterKilledBy(killer, id));
    }
  }
  return out;
}

export function tickProjectiles(now: number, dtSec: number): ServerMessage[] {
  const out: ServerMessage[] = [];
  for (const [id, proj] of [...liveProjectiles.entries()]) {
    const step = proj.speed * dtSec;
    const directional = proj.vx != null && proj.vy != null && (proj.maxRange ?? 0) > 0;

    if (directional) {
      const ox = proj.x;
      const oy = proj.y;
      const nx = proj.x + proj.vx! * step;
      const ny = proj.y + proj.vy! * step;
      proj.traveled = (proj.traveled ?? 0) + step;
      proj.x = nx;
      proj.y = ny;
      out.push({ type: "sync_projectile_move", id, x: nx, y: ny });

      const halfW = (proj.width ?? 0.7) * 0.5 + 0.15;
      let hitEntity: Entity | undefined;
      let bestAlong = Number.POSITIVE_INFINITY;
      for (const entity of [...liveMonsters.values(), ...[...players.values()].map((p) => p.entity)]) {
        if (entity.id === proj.casterId || entity.hp <= 0) {
          continue;
        }
        const caster = players.get(proj.casterId);
        if (caster && entity.mapId !== caster.entity.mapId) {
          continue;
        }
        const d = distPointToSegment(entity.x, entity.y, ox, oy, nx, ny);
        if (d <= halfW + projectileCatchRadius(entity)) {
          const along = proj.traveled ?? 0;
          if (along < bestAlong) {
            bestAlong = along;
            hitEntity = entity;
          }
        }
      }

      if (hitEntity) {
        const hit = hitFromCaster(proj.casterId, hitEntity.id, proj.skillId, now);
        if (hit) {
          out.push({
            type: "sync_skill",
            casterId: proj.casterId,
            targetId: hit.targetId,
            skillId: proj.skillId,
            damage: hit.damage,
            hpAfter: hit.hpAfter,
            mpAfter: proj.mpAfter,
            crit: hit.crit,
            element: hit.element,
            missed: hit.missed,
            advantage: hit.advantage,
            resistHint: hit.resistHint,
          });
          const threat = notePlayerDamageThreat(proj.casterId, hit.targetId, hit.damage, now);
          if (threat) {
            out.push(threat);
          }
          out.push(...checkBossPhase(hit.targetId));
          out.push({
            type: "sync_vitals",
            entityId: hitEntity.id,
            hp: hitEntity.hp,
            maxHp: hitEntity.maxHp,
            mp: hitEntity.mp,
            maxMp: hitEntity.maxMp,
          });
          if (hit.hpAfter <= 0 && liveMonsters.has(hit.targetId)) {
            const mid = hit.targetId;
            out.push(...killMonster(mid, now));
            const caster = players.get(proj.casterId);
            if (caster) {
              out.push(...onMonsterKilledBy(caster, mid));
            }
          }
        }
        if (proj.pendingStatus && proj.statusDurationMs > 0) {
          applyStatusOnHit(hitEntity.id, proj.pendingStatus, proj.statusDurationMs, now);
          const statusMsg = statusOf(hitEntity.id, now);
          if (statusMsg) {
            out.push(statusMsg);
          }
        }
        liveProjectiles.delete(id);
        out.push({ type: "sync_projectile_despawn", id });
        continue;
      }

      if ((proj.traveled ?? 0) >= (proj.maxRange ?? 0)) {
        liveProjectiles.delete(id);
        out.push({ type: "sync_projectile_despawn", id });
      }
      continue;
    }

    const target = findEntity(proj.targetId);
    if (!target || target.hp <= 0) {
      liveProjectiles.delete(id);
      out.push({ type: "sync_projectile_despawn", id });
      continue;
    }

    const dx = target.x - proj.x;
    const dy = target.y - proj.y;
    const dist = Math.hypot(dx, dy);
    const hitDist = projectileCatchRadius(target) + 0.15;

    if (dist <= hitDist || dist <= step) {
      proj.x = target.x;
      proj.y = target.y;
      out.push({ type: "sync_projectile_move", id, x: proj.x, y: proj.y });

      for (const pending of proj.pendingHits) {
        const hit = applyPendingHit(pending.targetId, pending.damage, pending.crit, proj.skillId, now);
        if (!hit) {
          continue;
        }
        out.push({
          type: "sync_skill",
          casterId: proj.casterId,
          targetId: hit.targetId,
          skillId: proj.skillId,
          damage: hit.damage,
          hpAfter: hit.hpAfter,
          mpAfter: proj.mpAfter,
          crit: hit.crit,
        });
        const threat = notePlayerDamageThreat(proj.casterId, hit.targetId, hit.damage, now);
        if (threat) {
          out.push(threat);
        }
        out.push(...checkBossPhase(hit.targetId));
        const entity = findEntity(hit.targetId) ?? liveMonsters.get(hit.targetId);
        if (entity) {
          out.push({
            type: "sync_vitals",
            entityId: entity.id,
            hp: entity.hp,
            maxHp: entity.maxHp,
            mp: entity.mp,
            maxMp: entity.maxMp,
          });
        }
        if (hit.hpAfter <= 0 && liveMonsters.has(hit.targetId)) {
          const mid = hit.targetId;
          out.push(...killMonster(mid, now));
          const caster = players.get(proj.casterId);
          if (caster) {
            out.push(...onMonsterKilledBy(caster, mid));
          }
        }
      }

      if (proj.pendingStatus && proj.statusDurationMs > 0) {
        applyStatusOnHit(proj.targetId, proj.pendingStatus, proj.statusDurationMs, now);
        const statusMsg = statusOf(proj.targetId, now);
        if (statusMsg) {
          out.push(statusMsg);
        }
      }

      liveProjectiles.delete(id);
      out.push({ type: "sync_projectile_despawn", id });
      continue;
    }

    const nx = proj.x + (dx / dist) * step;
    const ny = proj.y + (dy / dist) * step;
    proj.x = nx;
    proj.y = ny;
    out.push({ type: "sync_projectile_move", id, x: nx, y: ny });
  }
  return out;
}

export function snapshot(forMapId?: string) {
  const mapKey = forMapId ?? defaultMap.id;
  const baseId = resolveBaseMapId(mapKey);
  const map = mapById(baseId) ?? defaultMap;
  return {
    players: [...players.values()]
      .filter((session) => session.entity.mapId === mapKey)
      .map((session) => session.entity),
    monsters: [...liveMonsters.values()].filter((monster) => monster.hp > 0 && monster.mapId === mapKey),
    npcs: [...liveNpcs.values()].filter((npc) => npc.mapId === baseId && !mapKey.includes("#")),
    portals: mapKey.includes("#")
      ? portalsOnMap(baseId).filter(
          (p) =>
            p.targetMapId !== mapKey &&
            !p.targetMapId.startsWith("dungeon_") &&
            !p.targetMapId.startsWith("tower_boss_"),
        )
      : portalsOnMap(baseId),
    map: map as MapDef,
  };
}

export function respawnAtHome(session: PlayerSession): void {
  session.entity.hp = session.entity.maxHp;
  session.entity.mp = session.entity.maxMp;
  session.entity.mapId = session.homeMapId;
  session.entity.x = session.homeX;
  session.entity.y = session.homeY;
  session.statuses = [];
}

export function createCharacter(
  session: PlayerSession,
  name: string,
  classId: string,
): { error?: ServerMessage } {
  const cleaned = name.replace(/[^\w\s\-']/g, "").trim().slice(0, 16);
  if (cleaned.length < 2) {
    return { error: { type: "error", code: "bad_name", message: "Name too short" } };
  }
  // Always start as Adventurer; classId ignored except for legacy clients
  const cls = classById("adventurer") ?? defaultClass;
  void classId;
  session.classId = cls.id;
  session.entity.name = cleaned;
  session.charNameSet = true;
  session.unlockedSkillIds = starterSkillsFor(cls.id);
  session.skillPoints = 1;
  session.weaponIds = [...cls.startingWeaponIds];
  session.equippedWeaponId = cls.startingWeaponId;
  session.equippedWeapon2Id = cls.startingWeaponIds.find((id) => id !== cls.startingWeaponId) ?? null;
  session.classCardId = null;
  session.spiritIds = [...cls.startingSpiritIds];
  session.equippedSpiritId = cls.startingSpiritId;
  session.entity.maxHp = cls.hp;
  session.entity.hp = cls.hp;
  session.entity.maxMp = cls.mp;
  session.entity.mp = cls.mp;
  session.entity.atk = cls.atk;
  session.entity.magicAtk = cls.magicAtk;
  session.entity.def = cls.def;
  session.entity.magicResist = cls.magicResist;
  session.entity.moveSpeed = cls.moveSpeed;
  session.entity.hitRadius = cls.hitRadius;
  session.entity.resist = { ...cls.resist };
  session.entity.mapId = defaultMap.id;
  session.entity.x = defaultMap.spawn.x;
  session.entity.y = defaultMap.spawn.y;
  session.homeMapId = defaultMap.id;
  session.homeX = defaultMap.spawn.x;
  session.homeY = defaultMap.spawn.y;
  session.inWorld = true;
  applyGearStats(session);
  return {};
}

export function changeClass(session: PlayerSession, classId: string, cardItemId?: string): { error?: ServerMessage } {
  if (session.level < 20) {
    return {
      error: {
        type: "error",
        code: "level_too_low",
        message: "Class change unlocks at level 20",
      },
    };
  }
  const cls = classById(classId);
  if (!cls || cls.id === "adventurer") {
    return { error: { type: "error", code: "bad_class", message: "Invalid class card" } };
  }
  const keepLevel = session.level;
  const keepXp = session.xp;
  const keepSp = session.skillPoints;
  session.classId = cls.id;
  session.classCardId = cardItemId ?? session.classCardId;
  session.unlockedSkillIds = starterSkillsFor(cls.id);
  session.weaponIds = [...new Set([...session.weaponIds, ...cls.startingWeaponIds])];
  session.equippedWeaponId = cls.startingWeaponId;
  session.spiritIds = [...new Set([...session.spiritIds, ...cls.startingSpiritIds])];
  if (cls.startingSpiritId) {
    session.equippedSpiritId = cls.startingSpiritId;
  }
  const card = cardItemId ? itemById(cardItemId) : undefined;
  if (card?.secondaryWeaponId && weaponById(card.secondaryWeaponId)) {
    if (!session.weaponIds.includes(card.secondaryWeaponId)) {
      session.weaponIds.push(card.secondaryWeaponId);
    }
    session.equippedWeapon2Id = card.secondaryWeaponId;
  } else if (cls.id === "marksman" && weaponById("gun_spark")) {
    if (!session.weaponIds.includes("gun_spark")) session.weaponIds.push("gun_spark");
    session.equippedWeapon2Id = "gun_spark";
  } else if (cls.id === "rogue" && weaponById("dagger_twin")) {
    session.equippedWeapon2Id = session.equippedWeaponId === "dagger_twin" ? null : "dagger_twin";
  }
  session.level = keepLevel;
  session.xp = keepXp;
  session.skillPoints = keepSp;
  applyGearStats(session);
  return {};
}

export function swapWeapons(session: PlayerSession): { error?: ServerMessage; ok?: true } {
  if (!session.equippedWeapon2Id) {
    return { error: { type: "error", code: "no_secondary", message: "No secondary weapon" } };
  }
  if (!session.weaponIds.includes(session.equippedWeapon2Id)) {
    return { error: { type: "error", code: "unknown_weapon", message: "Secondary not owned" } };
  }
  const primary = session.equippedWeaponId;
  session.equippedWeaponId = session.equippedWeapon2Id;
  session.equippedWeapon2Id = primary;
  applyGearStats(session);
  return { ok: true };
}

export function bumpTowerFloor(session: PlayerSession, floor: number): void {
  if (floor > session.towerClearedFloor) {
    session.towerClearedFloor = floor;
  }
}

export function findEntity(id: string): Entity | undefined {
  const player = players.get(id)?.entity;
  if (player) {
    return player;
  }
  const monster = liveMonsters.get(id);
  if (monster && monster.hp > 0) {
    return monster;
  }
  return liveNpcs.get(id);
}

export function killMonster(entityId: string, now: number): ServerMessage[] {
  const monster = liveMonsters.get(entityId);
  const def = monsterMeta.get(entityId);
  if (!monster || monster.hp > 0 || !def) {
    return [];
  }
  const isInstance = monster.mapId.includes("#");
  liveMonsters.delete(entityId);
  monsterAttackReady.delete(entityId);
  monsterAggro.delete(entityId);
  clearThreat(entityId);
  monsterStatuses.delete(entityId);
  lootOwner.delete(entityId);
  bossPhase.delete(entityId);
  monsterAi.delete(entityId);
  if (!isInstance) {
    const already = pendingRespawns.some((p) => p.entityId === entityId);
    if (!already) {
      pendingRespawns.push({ def, entityId, at: now + RESPAWN_MS });
    }
  }
  return [{ type: "sync_despawn", entityId, reason: "death" }];
}

export function onMonsterKilledBy(session: PlayerSession, entityId: string): ServerMessage[] {
  const def = monsterMeta.get(entityId);
  const now = Date.now();
  const lootTo = resolveLootRecipient(session, entityId, now);
  const out: ServerMessage[] = [
    grantKillLoot(lootTo, def?.monsterType ?? "default"),
    ...grantXp(session, killXpFor(def?.monsterType ?? "default"), applyGearStats),
  ];
  if (def?.monsterType) {
    const q = noteKill(session, def.monsterType);
    if (q) {
      out.push(q);
    }
  }
  if (def?.monsterType === "tower_boss_f2") {
    bumpTowerFloor(session, 2);
  } else if (def?.monsterType === "tower_boss_f5") {
    bumpTowerFloor(session, 5);
  } else if (def?.mapId === "tower_f1" && session.towerClearedFloor < 1) {
    // soft progress: killing on floor still requires quests/switches for gates; bosses set floors
  }
  return out;
}

export function grantKillLoot(
  session: PlayerSession,
  monsterType = "slime",
  rng: () => number = Math.random,
): ServerMessage {
  return applyKillLoot(session, rollKillRewards(lootTableFor(monsterType), rng));
}

function tickDots(now: number): ServerMessage[] {
  const out: ServerMessage[] = [];
  for (const [id, statuses] of monsterStatuses) {
    const entity = liveMonsters.get(id);
    if (!entity || entity.hp <= 0) {
      continue;
    }
    for (const status of statuses) {
      if (status.kind !== "dot" || !status.nextTick || status.nextTick > now || status.until <= now) {
        continue;
      }
      const dmg = status.potency ?? 1;
      entity.hp = Math.max(0, entity.hp - dmg);
      status.nextTick = now + 1000;
      out.push({
        type: "sync_skill",
        casterId: id,
        targetId: id,
        skillId: status.id,
        damage: dmg,
        hpAfter: entity.hp,
        mpAfter: entity.mp,
      });
    }
    monsterStatuses.set(id, statuses.filter((s) => s.until > now));
  }
  return out;
}

function tickRegen(now: number): ServerMessage[] {
  const out: ServerMessage[] = [];
  if (now % 1000 > 400) {
    return out;
  }
  for (const session of players.values()) {
    if (session.entity.hp <= 0) {
      continue;
    }
    const beforeHp = session.entity.hp;
    const beforeMp = session.entity.mp;
    session.entity.hp = Math.min(session.entity.maxHp, session.entity.hp + session.entity.hpRegen);
    session.entity.mp = Math.min(session.entity.maxMp, session.entity.mp + session.entity.mpRegen);
    if (session.entity.hp !== beforeHp || session.entity.mp !== beforeMp) {
      out.push({
        type: "sync_vitals",
        entityId: session.entity.id,
        hp: session.entity.hp,
        maxHp: session.entity.maxHp,
        mp: session.entity.mp,
        maxMp: session.entity.maxMp,
      });
    }
  }

  // Ragdoll / training dummies: heal while not in combat.
  for (const [id, monster] of liveMonsters) {
    if (monster.hp <= 0 || monster.hp >= monster.maxHp) {
      continue;
    }
    const def = monsterMeta.get(id);
    if (!def || def.id !== "ragdoll") {
      continue;
    }
    if (monsterAggro.get(id)) {
      continue;
    }
    const before = monster.hp;
    monster.hp = Math.min(monster.maxHp, monster.hp + Math.max(20, Math.floor(monster.maxHp * 0.04)));
    if (monster.hp !== before) {
      out.push({
        type: "sync_vitals",
        entityId: id,
        hp: monster.hp,
        maxHp: monster.maxHp,
        mp: monster.mp,
        maxMp: monster.maxMp,
      });
    }
  }
  return out;
}

let lastHazardAt = 0;

function tickHazards(now: number): ServerMessage[] {
  const out: ServerMessage[] = [];
  if (now - lastHazardAt < 500) {
    return out;
  }
  lastHazardAt = now;

  for (const session of players.values()) {
    if (session.entity.hp <= 0 || !session.inWorld) {
      continue;
    }
    const map = mapById(session.entity.mapId.includes("#")
      ? session.entity.mapId.slice(0, session.entity.mapId.indexOf("#"))
      : session.entity.mapId);
    const hazards = map?.hazards;
    if (!hazards?.length) {
      continue;
    }
    const tx = Math.floor(session.entity.x);
    const ty = Math.floor(session.entity.y);
    const tile = hazards.find((h) => h.x === tx && h.y === ty);
    if (!tile) {
      continue;
    }
    const dmg = Math.max(1, tile.damage ?? 3);
    session.entity.hp = Math.max(0, session.entity.hp - dmg);
    out.push({
      type: "sync_skill",
      casterId: "hazard",
      targetId: session.entity.id,
      skillId: "hazard_tick",
      damage: dmg,
      hpAfter: session.entity.hp,
      mpAfter: session.entity.mp,
    });
    out.push({
      type: "sync_vitals",
      entityId: session.entity.id,
      hp: session.entity.hp,
      maxHp: session.entity.maxHp,
      mp: session.entity.mp,
      maxMp: session.entity.maxMp,
    });
    if (session.entity.hp <= 0) {
      out.push({
        type: "sync_death",
        entityId: session.entity.id,
        homeMapId: session.homeMapId,
        homeX: session.homeX,
        homeY: session.homeY,
      });
    }
  }
  return out;
}

export function tickWorld(now: number): ServerMessage[] {
  const out: ServerMessage[] = [
    ...tickDots(now),
    ...tickRegen(now),
    ...tickHazards(now),
    ...reapDeadMonsters(now),
  ];
  tickInstances(now);

  if (lastThreatDecayAt > 0) {
    const dt = Math.min(1, (now - lastThreatDecayAt) / 1000);
    if (dt > 0) {
      decayThreat(dt);
    }
  }
  lastThreatDecayAt = now;

  for (let i = pendingRespawns.length - 1; i >= 0; i -= 1) {
    const pending = pendingRespawns[i];
    if (now < pending.at) {
      continue;
    }
    pendingRespawns.splice(i, 1);
    const entity = spawnMonster(pending.def, pending.entityId, pending.def.x, pending.def.y);
    out.push({ type: "sync_spawn", entity });
  }

  const alivePlayers = new Set(
    [...players.values()].filter((s) => s.entity.hp > 0).map((s) => s.entity.id),
  );

  for (const [id, monster] of liveMonsters) {
    if (monster.hp <= 0) {
      continue;
    }
    const def = monsterMeta.get(id);
    if (!def) {
      continue;
    }

    // Immortal training dummies never fight back.
    if (def.monsterType === "immortal" || def.atk <= 0) {
      continue;
    }

    pruneDeadPlayers(id, alivePlayers);

    const isNeutral = (def.aggroMode ?? "hostile") === "neutral";

    let closest: PlayerSession | null = null;
    let closestDist = Number.POSITIVE_INFINITY;
    for (const session of players.values()) {
      if (session.entity.hp <= 0 || session.entity.mapId !== monster.mapId) {
        continue;
      }
      const dist = Math.hypot(session.entity.x - monster.x, session.entity.y - monster.y);
      if (dist < closestDist) {
        closestDist = dist;
        closest = session;
      }
    }
    // Proximity aggro only via closest player below — avoid seeding every unit in a packed yard.

    const home = monsterHome.get(id) ?? { x: monster.x, y: monster.y };
    const leash = Math.hypot(monster.x - home.x, monster.y - home.y);
    const ai = ensureMonsterAi(id);
    const dt = lastMonsterAiAt > 0 ? Math.min(0.25, (now - lastMonsterAiAt) / 1000) : 0.05;
    const moveStep = (monster.moveSpeed || 2) * dt;

    if (leash > def.leashRange && ai.phase !== "return") {
      clearThreat(id);
      ai.phase = "return";
      ai.fixateId = null;
      ai.windupUntil = 0;
      monsterAggro.set(id, null);
    }

    if (ai.phase === "return") {
      const distHome = Math.hypot(monster.x - home.x, monster.y - home.y);
      if (distHome <= 0.35) {
        monster.x = home.x;
        monster.y = home.y;
        monster.hp = monster.maxHp;
        ai.phase = "idle";
        ai.fixateId = null;
        clearThreat(id);
        monsterAggro.set(id, null);
        out.push({ type: "sync_move", entityId: id, x: monster.x, y: monster.y });
        out.push({
          type: "sync_vitals",
          entityId: id,
          hp: monster.hp,
          maxHp: monster.maxHp,
          mp: monster.mp,
          maxMp: monster.maxMp,
        });
        const cleared = syncThreatMessage(id, now, true);
        if (cleared) {
          out.push(cleared);
        }
      } else if (tryMoveMonster(monster, home.x, home.y, moveStep)) {
        out.push({ type: "sync_move", entityId: id, x: monster.x, y: monster.y });
      }
      continue;
    }

    // Prefer highest threat; nearest in aggro range as fallback (hostile only).
    // Cap how many monsters can newly seed from proximity per target to avoid yard swarms.
    let aggroId = topThreatId(id, alivePlayers, 10);
    if (!aggroId && !isNeutral && closest && closestDist <= def.aggroRange) {
      seedThreat(id, closest.entity.id, 20);
      aggroId = closest.entity.id;
    }
    if (ai.fixateId && alivePlayers.has(ai.fixateId) && players.get(ai.fixateId)?.entity.mapId === monster.mapId) {
      aggroId = ai.fixateId;
    }
    if (isNeutral && aggroId) {
      const target = players.get(aggroId);
      if (!target || target.entity.mapId !== monster.mapId ||
          Math.hypot(target.entity.x - monster.x, target.entity.y - monster.y) > def.leashRange) {
        clearThreat(id);
        aggroId = null;
        ai.phase = "return";
      }
    }
    monsterAggro.set(id, aggroId);

    const threatMsg = syncThreatMessage(id, now);
    if (threatMsg) {
      out.push(threatMsg);
    }

    if (!aggroId) {
      if (ai.phase === "idle" && Math.random() < 0.008) {
        ai.phase = "patrol";
        ai.patrolT = Math.random() * Math.PI * 2;
      }
      if (ai.phase === "patrol") {
        const px = home.x + Math.cos(ai.patrolT) * 1.2;
        const py = home.y + Math.sin(ai.patrolT) * 1.2;
        if (tryMoveMonster(monster, px, py, moveStep * 0.5)) {
          out.push({ type: "sync_move", entityId: id, x: monster.x, y: monster.y });
        }
        if (Math.hypot(monster.x - px, monster.y - py) < 0.3) {
          ai.phase = "idle";
        }
      } else {
        ai.phase = "idle";
      }
      continue;
    }

    const target = players.get(aggroId);
    if (!target) {
      continue;
    }

    if (isBossMonster(def) && !ai.fixateId) {
      ai.fixateId = aggroId;
    }

    const dist = Math.hypot(target.entity.x - monster.x, target.entity.y - monster.y);
    const hitRange = def.prefer === "ranged" ? 4 : 1.6;
    const priority = monsterSkillPriority(def);

    if (dist > hitRange) {
      ai.phase = "chase";
      ai.windupUntil = 0;
      if (tryMoveMonster(monster, target.entity.x, target.entity.y, moveStep)) {
        out.push({ type: "sync_move", entityId: id, x: monster.x, y: monster.y });
      }
      continue;
    }

    ai.phase = "attack";
    const readyAt = monsterAttackReady.get(id) ?? 0;
    if (now < readyAt || priority.length === 0) {
      continue;
    }

    if (isBossMonster(def) && ai.windupUntil === 0) {
      const windup = def.id.includes("ruins") || def.monsterType === "tower_boss_f5" ? 900 : 700;
      const radius = def.id.includes("ruins") || def.monsterType === "tower_boss_f5" ? 2.2 : 1.7;
      ai.windupUntil = now + windup;
      out.push({
        type: "sync_fx",
        kind: "telegraph",
        entityId: id,
        x: target.entity.x,
        y: target.entity.y,
        radius,
        durationMs: windup,
      });
      continue;
    }
    if (isBossMonster(def) && now < ai.windupUntil) {
      continue;
    }
    ai.windupUntil = 0;

    const pick = priority[0]!;
    const resolved = resolveDamage({
      attacker: {
        atk: monster.atk,
        matk: monster.magicAtk,
        critRate: monster.critChance,
        critDamage: monster.critDamage,
        hitRate: 1,
      },
      defender: {
        def: target.entity.def,
        mdef: target.entity.magicResist,
        dodgeRate: 0,
        elementalResist: {
          [toCombatElement(monster.element)]: (target.entity.resist?.[monster.element ?? "earth"] ?? 0) / 100,
        },
        element: toCombatElement(target.entity.element),
      },
      skill: {
        damageType: "physical",
        baseDamageMultiplier: 1,
        flatDamage: 0,
        element: toCombatElement(monster.element),
      },
      config: loadCombatConfig(),
      rng: Math.random,
    });
    let damage = resolved.missed ? 0 : resolved.damage;
    damage = applyIncomingDamageMult(target, damage, now);
    target.entity.hp = Math.max(0, target.entity.hp - damage);
    addThreat(id, target.entity.id, Math.min(8, Math.max(2, Math.floor(damage / 2))));
    monsterAttackReady.set(id, now + def.attackMs);
    const skillId = pick === "auto"
      ? (def.id === "cannon" ? "cannon_flame" : `${def.id}_hit`)
      : pick;
    out.push({
      type: "sync_skill",
      casterId: id,
      targetId: target.entity.id,
      skillId,
      damage,
      hpAfter: target.entity.hp,
      mpAfter: target.entity.mp,
      crit: resolved.crit,
      element: monster.element,
      missed: resolved.missed,
      advantage: resolved.advantage,
      resistHint: resolved.resistHint,
    });
    if (target.entity.hp <= 0) {
      out.push({
        type: "sync_vitals",
        entityId: target.entity.id,
        hp: target.entity.hp,
        maxHp: target.entity.maxHp,
        mp: target.entity.mp,
        maxMp: target.entity.maxMp,
      });
      out.push({
        type: "sync_death",
        entityId: target.entity.id,
        homeMapId: target.homeMapId,
        homeX: target.homeX,
        homeY: target.homeY,
      });
    } else {
      out.push({
        type: "sync_vitals",
        entityId: target.entity.id,
        hp: target.entity.hp,
        maxHp: target.entity.maxHp,
        mp: target.entity.mp,
        maxMp: target.entity.maxMp,
      });
    }
    const afterHitThreat = syncThreatMessage(id, now, true);
    if (afterHitThreat) {
      out.push(afterHitThreat);
    }
  }

  lastMonsterAiAt = now;
  return out;
}

export function statusOf(entityId: string, now: number): ServerMessage | null {
  const player = players.get(entityId);
  if (player) {
    return {
      type: "sync_status",
      entityId,
      statuses: player.statuses.filter((s) => s.until > now),
      serverTime: now,
    };
  }
  const statuses = monsterStatuses.get(entityId);
  if (statuses) {
    return {
      type: "sync_status",
      entityId,
      statuses: statuses.filter((s) => s.until > now),
      serverTime: now,
    };
  }
  return null;
}

const LOOT_WINDOW_MS = 45_000;
/** First damager gets loot priority for LOOT_WINDOW_MS. */
export const lootOwner = new Map<string, { playerId: string; until: number }>();

/** Apply player→monster threat after a damaging hit. Returns sync_threat if any. */
export function notePlayerDamageThreat(
  casterId: string,
  targetId: string,
  damage: number,
  now: number,
): ServerMessage | null {
  if (damage <= 0 || !liveMonsters.has(targetId) || !players.has(casterId)) {
    return null;
  }
  const monster = liveMonsters.get(targetId)!;
  if (!lootOwner.has(targetId)) {
    lootOwner.set(targetId, { playerId: casterId, until: now + LOOT_WINDOW_MS });
  }
  addThreat(targetId, casterId, threatFromDamage(damage, monster.maxHp));
  return syncThreatMessage(targetId, now, true);
}

export function checkBossPhase(monsterId: string): ServerMessage[] {
  const monster = liveMonsters.get(monsterId);
  const def = monsterMeta.get(monsterId);
  if (!monster || !def || !isBossMonster(def) || monster.maxHp <= 0) {
    return [];
  }
  const ratio = monster.hp / monster.maxHp;
  const prev = bossPhase.get(monsterId) ?? 0;
  const out: ServerMessage[] = [];
  if (ratio <= 0.66 && prev < 1) {
    bossPhase.set(monsterId, 1);
    monster.atk = Math.floor(monster.atk * 1.15);
    monster.moveSpeed += 0.4;
    out.push({
      type: "sync_chat",
      channel: "map",
      fromId: "system",
      fromName: "System",
      text: `${monster.name} enters phase 2!`,
      serverTime: Date.now(),
    });
    out.push({
      type: "sync_instance",
      instanceId: monster.mapId.includes("#") ? monster.mapId.split("#")[1] : null,
      mapId: monster.mapId.includes("#") ? monster.mapId.split("#")[0] : monster.mapId,
      expiresAt: 0,
      phase: 2,
    });
  }
  if (ratio <= 0.33 && prev < 2) {
    bossPhase.set(monsterId, 2);
    monster.atk = Math.floor(monster.atk * 1.2);
    monster.attackSpeed = Math.min(2, monster.attackSpeed + 0.25);
    out.push({
      type: "sync_chat",
      channel: "map",
      fromId: "system",
      fromName: "System",
      text: `${monster.name} enrages!`,
      serverTime: Date.now(),
    });
    out.push({
      type: "sync_instance",
      instanceId: monster.mapId.includes("#") ? monster.mapId.split("#")[1] : null,
      mapId: monster.mapId.includes("#") ? monster.mapId.split("#")[0] : monster.mapId,
      expiresAt: 0,
      phase: 3,
    });
  }
  return out;
}

export function resolveLootRecipient(killer: PlayerSession, monsterId: string, now: number): PlayerSession {
  const own = lootOwner.get(monsterId);
  lootOwner.delete(monsterId);
  if (own && now <= own.until && own.playerId !== killer.entity.id) {
    const owner = players.get(own.playerId);
    if (owner && owner.entity.mapId === killer.entity.mapId) {
      if (!killer.partyId || killer.partyId !== owner.partyId) {
        return owner;
      }
    }
  }
  return killer;
}

export function buildInspect(targetId: string, now: number): ServerMessage {
  const player = players.get(targetId);
  if (player) {
    const e = player.entity;
    return {
      type: "sync_inspect",
      targetId,
      kind: "player",
      name: e.name,
      portraitKey: "player",
      hp: e.hp,
      maxHp: e.maxHp,
      mp: e.mp,
      maxMp: e.maxMp,
      atk: e.atk,
      magicAtk: e.magicAtk,
      def: e.def,
      magicResist: e.magicResist,
      attackSpeed: e.attackSpeed,
      moveSpeed: e.moveSpeed,
      critChance: e.critChance,
      critDamage: e.critDamage,
      hitRadius: e.hitRadius,
      resist: { ...e.resist },
      element: e.element,
      weaponId: player.equippedWeaponId,
      spiritId: player.equippedSpiritId,
      statuses: player.statuses.filter((s) => s.until > now),
    };
  }
  const monster = liveMonsters.get(targetId);
  if (monster && monster.hp > 0) {
    const meta = monsterMeta.get(targetId);
    return {
      type: "sync_inspect",
      targetId,
      kind: "monster",
      name: monster.name,
      portraitKey: meta?.id ?? "monster",
      hp: monster.hp,
      maxHp: monster.maxHp,
      mp: monster.mp,
      maxMp: monster.maxMp,
      atk: monster.atk,
      magicAtk: monster.magicAtk,
      def: monster.def,
      magicResist: monster.magicResist,
      attackSpeed: monster.attackSpeed,
      moveSpeed: monster.moveSpeed,
      critChance: monster.critChance,
      critDamage: monster.critDamage,
      hitRadius: monster.hitRadius,
      resist: { ...monster.resist },
      element: monster.element,
      statuses: (monsterStatuses.get(targetId) ?? []).filter((s) => s.until > now),
      monsterType: meta?.prefer ?? "melee",
    };
  }
  const npc = liveNpcs.get(targetId);
  if (npc) {
    return {
      type: "sync_inspect",
      targetId,
      kind: "npc",
      name: npc.name,
      portraitKey: "npc",
      hp: npc.hp,
      maxHp: npc.maxHp,
      mp: 0,
      maxMp: 0,
      atk: 0,
      magicAtk: 0,
      def: 0,
      magicResist: 0,
      attackSpeed: 1,
      moveSpeed: 0,
      critChance: 0,
      critDamage: 1,
      hitRadius: npc.hitRadius,
      resist: { ...npc.resist },
      statuses: [],
      monsterType: npcLines.get(targetId) ?? "",
      interact: npcInteract.get(targetId),
    };
  }
  return { type: "error", code: "invalid_target", message: "Target not found" };
}

resetWorld();

bindInstanceHooks({
  createId,
  spawnMonster,
  listDungeonDefs: (mapId) => monsters.filter((m) => m.mapId === mapId),
  despawnMonster,
});
