import { defaultBanner, defaultClass, defaultMap, monsters, skillById, spiritById, weaponById } from "./data.js";
import { emptyInventory, pityFor, pityView, seedStarterInventory } from "./gacha.js";
import { loadGuest, type GuestSave } from "./persist.js";
import { applyPendingHit, applyStatusOnHit, hitFromCaster } from "./combat.js";
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

let nextId = 1;

export function createId(prefix: string): string {
  nextId += 1;
  return `${prefix}_${nextId}`;
}

export const players = new Map<string, PlayerSession>();
export const liveMonsters = new Map<string, Entity>();
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
const RESPAWN_MS = 8000;
let lastThreatDecayAt = 0;

const zeroResist = (): ResistMap => ({
  wind: 0, fire: 0, water: 0, earth: 0, holy: 0, dark: 0,
});

export function resetWorld(): void {
  players.clear();
  liveMonsters.clear();
  monsterMeta.clear();
  monsterStatuses.clear();
  monsterHome.clear();
  monsterAggro.clear();
  liveProjectiles.clear();
  clearAllThreat();
  pendingRespawns.length = 0;
  monsterAttackReady.clear();
  lastThreatDecayAt = 0;
  for (const def of monsters) {
    spawnMonster(def, def.respawnId, def.x, def.y);
  }
}

function spawnMonster(def: MonsterDef, id: string, x: number, y: number): Entity {
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
    mapId: defaultMap.id,
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

function applySave(session: PlayerSession, save: GuestSave): void {
  session.classId = save.classId || session.classId;
  session.entity.x = save.x;
  session.entity.y = save.y;
  session.entity.hp = Math.min(session.entity.maxHp, save.hp || session.entity.hp);
  session.entity.mp = Math.min(session.entity.maxMp, save.mp || session.entity.mp);
  session.inventory = save.inventory?.length ? save.inventory : session.inventory;
  session.pity = save.pity ?? {};
  if (save.weaponIds?.length) {
    session.weaponIds = save.weaponIds.filter((id) => !!weaponById(id));
    if (!session.weaponIds.length) {
      session.weaponIds = [...defaultClass.startingWeaponIds];
    }
  }
  if (save.equippedWeaponId && weaponById(save.equippedWeaponId) && session.weaponIds.includes(save.equippedWeaponId)) {
    session.equippedWeaponId = save.equippedWeaponId;
  } else {
    session.equippedWeaponId = defaultClass.startingWeaponId;
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

export function spawnPlayer(guestToken = ""): PlayerSession {
  const token = guestToken || createId("guest");
  const entity = buildPlayerEntity();
  const session: PlayerSession = {
    entity,
    classId: defaultClass.id,
    guestToken: token,
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
    spiritIds: [...defaultClass.startingSpiritIds],
    equippedSpiritId: defaultClass.startingSpiritId,
    moveLockUntil: 0,
  };

  const save = loadGuest(token);
  if (save) {
    applySave(session, save);
  } else {
    seedStarterInventory(session.inventory);
  }
  if (!session.inventory.some((s) => s.itemId)) {
    seedStarterInventory(session.inventory);
  }
  if (session.entity.hp <= 0) {
    session.entity.hp = session.entity.maxHp;
    session.entity.mp = session.entity.maxMp;
    session.entity.x = defaultMap.spawn.x;
    session.entity.y = defaultMap.spawn.y;
  }
  applyGearStats(session);
  players.set(entity.id, session);
  return session;
}

/** Class base + gear resists only. Weapon atk/magic bonuses applied once in combat. */
export function applyGearStats(session: PlayerSession): void {
  const base = defaultClass;
  const weapon = weaponById(session.equippedWeaponId);
  const spirit = session.equippedSpiritId ? spiritById(session.equippedSpiritId) : undefined;
  session.entity.atk = base.atk;
  session.entity.magicAtk = base.magicAtk;
  session.entity.def = base.def;
  session.entity.magicResist = base.magicResist;
  session.entity.attackSpeed = base.attackSpeed;
  session.entity.critChance = base.critChance;
  session.entity.moveSpeed = base.moveSpeed;
  session.entity.resist = { ...base.resist };
  if (weapon?.resistBonus) {
    for (const [key, value] of Object.entries(weapon.resistBonus)) {
      const element = key as keyof ResistMap;
      session.entity.resist[element] = (session.entity.resist[element] ?? 0) + (value ?? 0);
    }
  }
  if (spirit?.resistBonus) {
    for (const [key, value] of Object.entries(spirit.resistBonus)) {
      const element = key as keyof ResistMap;
      session.entity.resist[element] = (session.entity.resist[element] ?? 0) + (value ?? 0);
    }
  }
  session.entity.weaponId = session.equippedWeaponId;
  session.entity.spiritId = session.equippedSpiritId;
  session.entity.element = spirit?.element ?? weapon?.element;
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

export function currentPityView(session: PlayerSession): PityView {
  return pityView(defaultBanner, pityFor(session, defaultBanner.id));
}

export function cooldownSnapshot(session: PlayerSession): CooldownEntry[] {
  return defaultClass.skillIds.map((id) => {
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

export function tickProjectiles(now: number, dtSec: number): ServerMessage[] {
  const out: ServerMessage[] = [];
  for (const [id, proj] of [...liveProjectiles.entries()]) {
    const step = proj.speed * dtSec;
    const directional = proj.vx != null && proj.vy != null && (proj.maxRange ?? 0) > 0;

    if (directional) {
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
        if (entity.kind === "player" && !players.has(entity.id)) {
          continue;
        }
        const d = Math.hypot(entity.x - proj.x, entity.y - proj.y);
        if (d <= halfW + (entity.hitRadius || 0.4)) {
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
          });
          const threat = notePlayerDamageThreat(proj.casterId, hit.targetId, hit.damage, now);
          if (threat) {
            out.push(threat);
          }
          out.push({
            type: "sync_vitals",
            entityId: hitEntity.id,
            hp: hitEntity.hp,
            maxHp: hitEntity.maxHp,
            mp: hitEntity.mp,
            maxMp: hitEntity.maxMp,
          });
          if (hit.hpAfter <= 0 && liveMonsters.has(hit.targetId)) {
            out.push(...killMonster(hit.targetId, now));
            const caster = players.get(proj.casterId);
            if (caster) {
              out.push(grantKillLoot(caster));
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
    const hitDist = (target.hitRadius || 0.4) + 0.15;

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
          out.push(...killMonster(hit.targetId, now));
          const caster = players.get(proj.casterId);
          if (caster) {
            out.push(grantKillLoot(caster));
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

export function snapshot() {
  return {
    players: [...players.values()].map((session) => session.entity),
    monsters: [...liveMonsters.values()].filter((monster) => monster.hp > 0),
    map: defaultMap as MapDef,
  };
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
  return undefined;
}

export function killMonster(entityId: string, now: number): ServerMessage[] {
  const monster = liveMonsters.get(entityId);
  const def = monsterMeta.get(entityId);
  if (!monster || monster.hp > 0 || !def) {
    return [];
  }
  liveMonsters.delete(entityId);
  monsterAttackReady.delete(entityId);
  monsterAggro.delete(entityId);
  clearThreat(entityId);
  monsterStatuses.delete(entityId);
  pendingRespawns.push({ def, entityId, at: now + RESPAWN_MS });
  return [{ type: "sync_despawn", entityId, reason: "death" }];
}

export function grantKillLoot(session: PlayerSession): ServerMessage {
  const slot = session.inventory.find((s) => s.itemId === "item_dust")
    ?? session.inventory.find((s) => s.itemId === null);
  if (slot) {
    if (slot.itemId === null) {
      slot.itemId = "item_dust";
      slot.quantity = 1;
    } else {
      slot.quantity += 1;
    }
  }
  return { type: "sync_loot", itemId: "item_dust", quantity: 1, inventory: session.inventory };
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
  return out;
}

export function tickWorld(now: number): ServerMessage[] {
  const out: ServerMessage[] = [...tickDots(now), ...tickRegen(now)];

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

    pruneDeadPlayers(id, alivePlayers);

    let closest: PlayerSession | null = null;
    let closestDist = Number.POSITIVE_INFINITY;
    for (const session of players.values()) {
      if (session.entity.hp <= 0) {
        continue;
      }
      const dist = Math.hypot(session.entity.x - monster.x, session.entity.y - monster.y);
      if (dist < closestDist) {
        closestDist = dist;
        closest = session;
      }
      if (dist <= def.aggroRange) {
        seedThreat(id, session.entity.id, 20);
      }
    }

    const home = monsterHome.get(id) ?? { x: monster.x, y: monster.y };
    const leash = Math.hypot(monster.x - home.x, monster.y - home.y);

    if (leash > def.leashRange) {
      clearThreat(id);
      monster.x = home.x;
      monster.y = home.y;
      monsterAggro.set(id, null);
      out.push({ type: "sync_move", entityId: id, x: monster.x, y: monster.y });
      const cleared = syncThreatMessage(id, now, true);
      if (cleared) {
        out.push(cleared);
      }
      continue;
    }

    // Prefer highest threat; if tie / none, fall back to closest in aggro range
    let aggroId = topThreatId(id, alivePlayers, 10);
    if (!aggroId && closest && closestDist <= def.aggroRange) {
      seedThreat(id, closest.entity.id, 20);
      aggroId = closest.entity.id;
    }
    monsterAggro.set(id, aggroId);

    const threatMsg = syncThreatMessage(id, now);
    if (threatMsg) {
      out.push(threatMsg);
    }

    if (!aggroId) {
      continue;
    }

    const readyAt = monsterAttackReady.get(id) ?? 0;
    if (now < readyAt) {
      continue;
    }

    const target = players.get(aggroId);
    if (!target) {
      continue;
    }
    const dist = Math.hypot(target.entity.x - monster.x, target.entity.y - monster.y);
    const hitRange = def.prefer === "ranged" ? 4 : 1.6;
    if (dist > hitRange) {
      continue;
    }

    const damage = Math.max(1, monster.atk - target.entity.def);
    target.entity.hp = Math.max(0, target.entity.hp - damage);
    // Being hit builds a little threat so the victim stays on the bar
    addThreat(id, target.entity.id, Math.min(8, Math.max(2, Math.floor(damage / 2))));
    monsterAttackReady.set(id, now + def.attackMs);
    out.push({
      type: "sync_skill",
      casterId: id,
      targetId: target.entity.id,
      skillId: `${def.id}_hit`,
      damage,
      hpAfter: target.entity.hp,
      mpAfter: target.entity.mp,
    });
    out.push({
      type: "sync_vitals",
      entityId: target.entity.id,
      hp: target.entity.hp,
      maxHp: target.entity.maxHp,
      mp: target.entity.mp,
      maxMp: target.entity.maxMp,
    });
    const afterHitThreat = syncThreatMessage(id, now, true);
    if (afterHitThreat) {
      out.push(afterHitThreat);
    }
  }

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
  addThreat(targetId, casterId, threatFromDamage(damage, monster.maxHp));
  return syncThreatMessage(targetId, now, true);
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
  return { type: "error", code: "invalid_target", message: "Target not found" };
}

resetWorld();
