import { mapById, skillById, spiritById, weaponById } from "./data.js";
import { resolveBaseMapId } from "./instance.js";
import { isSolidAt, resolveWalk } from "./tileCollision.js";
import { loadCombatConfig } from "./combat/config.js";
import { resolveDamage, toCombatElement, type DamageResult, type DamageRng } from "./combat/damage.js";
import { cancelRest, isResting } from "./rest.js";
import type {
  AttrName,
  Element,
  Entity,
  MapDef,
  PlayerSession,
  Scaling,
  ServerMessage,
  SkillAffects,
  SkillDef,
  AoeOrigin,
  StatusInstance,
  WeaponDef,
} from "./types.js";

const MAX_ACTIONS_PER_SEC = 12;

const STRIKE_SKILLS = new Set([
  "auto_attack",
  "auto_attack_off",
  "slash",
  "shot",
  "hook_shot",
  "shockwave",
  "shove",
  "pull",
  "stun_bolt",
  "cannon_flame",
  "cleave",
  "arrow_rain",
  "arcane_nova",
  "knife_fan",
  "thunderstorm",
  "explosion",
]);

/** Client SkillCastSec after the 1.75× combat-pacing pass. Auto uses full CD instead. */
export function castRecoveryMs(skillId: string): number {
  if (skillId === "auto_attack" || skillId === "auto_attack_off") {
    return 1225;
  }
  if (skillId === "dash") {
    return 438;
  }
  if (STRIKE_SKILLS.has(skillId) || skillId.endsWith("_hit")) {
    return 788;
  }
  return 613;
}

let combatRng: DamageRng = Math.random;

/** Tests pin this to a constant so variance/crit cannot flake comparisons. */
export function setCombatRng(rng?: DamageRng): void {
  combatRng = rng ?? Math.random;
}
const MAX_MOVES_PER_SEC = 30;
const SHOVE_MOVE_LOCK_MS = 250;

let findEntityHook: (id: string) => Entity | undefined = () => undefined;
let getPlayersHook: () => PlayerSession[] = () => [];
let listHostilesHook: () => Entity[] = () => [];
let listNpcsHook: () => Entity[] = () => [];
let attachMonsterStatus: (id: string, status: StatusInstance) => void = () => undefined;
let clampImmortalHook: (entity: Entity) => void = () => undefined;

export function bindCombatWorld(
  findEntity: (id: string) => Entity | undefined,
  getPlayers: () => PlayerSession[],
  attachStatus: (id: string, status: StatusInstance) => void,
  listHostiles: () => Entity[] = () => [],
  listNpcs: () => Entity[] = () => [],
  clampImmortal: (entity: Entity) => void = () => undefined,
): void {
  findEntityHook = findEntity;
  getPlayersHook = getPlayers;
  attachMonsterStatus = attachStatus;
  listHostilesHook = listHostiles;
  listNpcsHook = listNpcs;
  clampImmortalHook = clampImmortal;
}

function applyDamageHp(target: Entity, damage: number): void {
  target.hp = Math.max(0, target.hp - damage);
  clampImmortalHook(target);
}

export type CastHit = {
  targetId: string;
  damage: number;
  hpAfter: number;
  crit: boolean;
  element?: string;
  missed?: boolean;
  advantage?: string;
  resistHint?: number;
};

export type CastAim = {
  aimDx?: number;
  aimDy?: number;
  aimX?: number;
  aimY?: number;
};

export type CastOk = {
  ok: true;
  hits: CastHit[];
  mpAfter: number;
  moved: boolean;
  movedEntities: { id: string; x: number; y: number }[];
  primaryTargetId: string;
  aoe: boolean;
  aimX?: number;
  aimY?: number;
  aoeRadius?: number;
  projectile?: {
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
  };
};

function distance(ax: number, ay: number, bx: number, by: number): number {
  return Math.hypot(ax - bx, ay - by);
}

function mapFor(entity: Entity): MapDef {
  return mapById(resolveBaseMapId(entity.mapId)) ?? mapById("town_ashen")!;
}

function isBlocked(x: number, y: number, map: MapDef): boolean {
  return isSolidAt(x, y, map);
}

function rateLimited(session: PlayerSession, now: number): boolean {
  session.actionTimes = session.actionTimes.filter((time) => now - time < 1000);
  if (session.actionTimes.length >= MAX_ACTIONS_PER_SEC) {
    return true;
  }
  session.actionTimes.push(now);
  return false;
}

function moveRateLimited(session: PlayerSession, now: number): boolean {
  session.moveTimes = session.moveTimes.filter((time) => now - time < 1000);
  if (session.moveTimes.length >= MAX_MOVES_PER_SEC) {
    return true;
  }
  session.moveTimes.push(now);
  return false;
}

function hitRadiusOf(entity: Entity): number {
  return entity.hitRadius > 0 ? entity.hitRadius : 0.4;
}

/** Center distance minus both hit radii (edge-to-edge). */
export function rangeGap(a: Entity, b: Entity): number {
  return Math.max(0, distance(a.x, a.y, b.x, b.y) - hitRadiusOf(a) - hitRadiusOf(b));
}

/** True if mover at (x,y) would overlap a blocking entity. Combatants may overlap mobs. */
export function entityBlockedAt(
  x: number,
  y: number,
  mover: Entity,
  ignoreIds?: ReadonlySet<string> | string[],
): boolean {
  const ignore = ignoreIds
    ? (ignoreIds instanceof Set ? ignoreIds : new Set(ignoreIds))
    : null;
  const moverR = hitRadiusOf(mover);
  const overlaps = (other: Entity): boolean => {
    if (other.id === mover.id || other.hp <= 0 || other.mapId !== mover.mapId) {
      return false;
    }
    if (ignore?.has(other.id)) {
      return false;
    }
    if (mover.kind === "monster" && other.kind === "monster") {
      return false;
    }
    // Player ↔ monster: stand in melee without body-block (NosTale-style).
    if (
      (mover.kind === "player" && other.kind === "monster") ||
      (mover.kind === "monster" && other.kind === "player")
    ) {
      return false;
    }
    // NPCs occupy floor pads; they must not act as invisible walls.
    if (other.kind === "npc" || mover.kind === "npc") {
      return false;
    }
    return distance(x, y, other.x, other.y) < moverR + hitRadiusOf(other) - 0.02;
  };

  for (const session of getPlayersHook()) {
    if (overlaps(session.entity)) {
      return true;
    }
  }
  for (const monster of listHostilesHook()) {
    if (overlaps(monster)) {
      return true;
    }
  }
  for (const npc of listNpcsHook()) {
    if (overlaps(npc)) {
      return true;
    }
  }
  return false;
}

export function isStunned(session: PlayerSession, now: number): boolean {
  return session.statuses.some((status) => status.kind === "stun" && status.until > now);
}

export function isBlinded(session: PlayerSession, now: number): boolean {
  return session.statuses.some((status) => status.kind === "blind" && status.until > now);
}

export function isMoveLocked(session: PlayerSession, now: number): boolean {
  return session.moveLockUntil > now;
}

export function hasShoveResist(targetId: string, now: number): boolean {
  const player = getPlayersHook().find((p) => p.entity.id === targetId);
  if (player) {
    return player.statuses.some((s) => s.kind === "shove_resist" && s.until > now);
  }
  return false;
}

export function pruneStatuses(session: PlayerSession, now: number): void {
  session.statuses = session.statuses.filter((status) => {
    if (status.until <= now) {
      return false;
    }
    if ((status.kind === "shield_phys" || status.kind === "shield_mag") && (status.shieldHp ?? 0) <= 0) {
      return false;
    }
    return true;
  });
}

export function moveSpeedMult(session: PlayerSession, now: number): number {
  pruneStatuses(session, now);
  return session.statuses.reduce((mult, status) => {
    if (status.kind === "speed_mult") {
      return mult * (status.moveSpeedMult ?? 1);
    }
    return mult;
  }, 1);
}

export function attackSpeedMult(session: PlayerSession, now: number): number {
  pruneStatuses(session, now);
  return session.statuses.reduce((mult, status) => {
    if (status.kind === "speed_mult") {
      return mult * (status.attackSpeedMult ?? 1);
    }
    return mult;
  }, 1);
}

function attrBonus(session: PlayerSession, attr: AttrName, now: number): number {
  pruneStatuses(session, now);
  let bonus = 0;
  for (const status of session.statuses) {
    if (status.kind === "attr_up" && status.attr === attr) {
      bonus += status.amount ?? 0;
    }
    if (attr === "atk" && status.atkBonus) {
      bonus += status.atkBonus;
    }
  }
  return bonus;
}

function isAutoAttackSkill(skill: SkillDef): boolean {
  return skill.id === "auto_attack" || skill.id === "auto_attack_off";
}

export function resolveAttackElement(session: PlayerSession, skill: SkillDef, weapon: WeaponDef | undefined): Element {
  if (isAutoAttackSkill(skill)) {
    const spirit = session.equippedSpiritId ? spiritById(session.equippedSpiritId) : undefined;
    return spirit?.element ?? weapon?.element ?? skill.element;
  }
  return skill.element;
}

function elemDmgMult(session: PlayerSession, element: Element, now: number): number {
  pruneStatuses(session, now);
  const spirit = session.equippedSpiritId ? spiritById(session.equippedSpiritId) : undefined;
  let mult = 1;
  if (spirit && spirit.element === element) {
    mult += spirit.elemDmgBonus;
  }
  for (const status of session.statuses) {
    if (status.kind === "elem_dmg_up" && status.element === element) {
      mult += status.elemDmgMult ?? 0;
    }
  }
  return mult;
}

function absorbShield(targetId: string, damage: number, scaling: Scaling, now: number): number {
  const player = getPlayersHook().find((p) => p.entity.id === targetId);
  if (!player || damage <= 0) {
    return damage;
  }
  pruneStatuses(player, now);
  const kind = scaling === "magic" ? "shield_mag" : "shield_phys";
  let remaining = damage;
  for (const status of player.statuses) {
    if (status.kind !== kind || (status.shieldHp ?? 0) <= 0) {
      continue;
    }
    const absorbed = Math.min(status.shieldHp ?? 0, remaining);
    status.shieldHp = (status.shieldHp ?? 0) - absorbed;
    remaining -= absorbed;
    if (remaining <= 0) {
      break;
    }
  }
  pruneStatuses(player, now);
  return remaining;
}

export function validateMove(
  session: PlayerSession,
  x: number,
  y: number,
  now: number,
): ServerMessage | { ok: true; x: number; y: number } {
  if (isStunned(session, now)) {
    return { type: "error", code: "stunned", message: "You are stunned" };
  }
  if (session.talkingNpcId) {
    return { type: "error", code: "talking", message: "In conversation" };
  }
  if (isMoveLocked(session, now)) {
    return { type: "error", code: "move_locked", message: "You were shoved" };
  }
  if (moveRateLimited(session, now)) {
    return { type: "error", code: "rate_limited", message: "Too many moves" };
  }
  if (!Number.isFinite(x) || !Number.isFinite(y)) {
    return { type: "error", code: "invalid_move", message: "Coordinates must be numbers" };
  }
  const map = mapFor(session.entity);
  const walked = resolveWalk(session.entity.x, session.entity.y, x, y, map);
  x = walked.x;
  y = walked.y;
  if (entityBlockedAt(x, y, session.entity)) {
    return { type: "error", code: "blocked_entity", message: "Blocked by entity" };
  }

  const elapsedSec = Math.max(0, (now - session.lastMoveAt) / 1000);
  const speed = session.entity.moveSpeed * moveSpeedMult(session, now);
  // Floor matches client ~12 Hz sends + jitter so bunched packets do not rubber-band.
  const maxDist = speed * Math.max(elapsedSec, 1 / 10) + 0.2;
  const dist = distance(session.entity.x, session.entity.y, x, y);
  if (dist <= maxDist) {
    if (dist > 0.04) {
      cancelRest(session);
    }
    return { ok: true, x, y };
  }

  // Teleport / speed hack — still reject. Small overshoot is clamped along the path.
  if (dist > speed * 0.45 + 1.5) {
    return { type: "error", code: "too_fast", message: "Move exceeds walk speed" };
  }

  const t = maxDist / dist;
  const clamped = resolveWalk(
    session.entity.x,
    session.entity.y,
    session.entity.x + (x - session.entity.x) * t,
    session.entity.y + (y - session.entity.y) * t,
    map,
  );
  if (distance(session.entity.x, session.entity.y, clamped.x, clamped.y) > 0.04) {
    cancelRest(session);
  }
  return {
    ok: true,
    x: clamped.x,
    y: clamped.y,
  };
}

function stepEntity(
  entity: Entity,
  dx: number,
  dy: number,
  tiles: number,
  ignoreIds?: ReadonlySet<string> | string[],
): boolean {
  let moved = false;
  const map = mapFor(entity);
  // Normalize to unit cardinal/diagonal steps so fractional dirs still advance.
  const len = Math.hypot(dx, dy);
  const stepX = len > 1e-6 ? dx / len : 0;
  const stepY = len > 1e-6 ? dy / len : 0;
  const steps = Math.max(1, Math.abs(tiles));
  for (let i = 0; i < steps; i += 1) {
    const nx = entity.x + stepX;
    const ny = entity.y + stepY;
    if (isBlocked(nx, ny, map)) {
      break;
    }
    if (entityBlockedAt(nx, ny, entity, ignoreIds)) {
      break;
    }
    entity.x = nx;
    entity.y = ny;
    moved = true;
  }
  return moved;
}

function needsProjectile(skill: SkillDef, weapon: WeaponDef | undefined): boolean {
  if (skill.selfTarget || skill.damageType === "aoe" || skill.heal > 0) {
    return false;
  }
  if (skill.movement) {
    return false;
  }
  if (isAutoAttackSkill(skill)) {
    return weapon?.style === "ranged";
  }
  return (skill.projectileSpeed ?? 0) > 0;
}

function projectileSpeedOf(skill: SkillDef, weapon: WeaponDef | undefined): number {
  if (isAutoAttackSkill(skill) && weapon?.style === "ranged") {
    return skill.projectileSpeed && skill.projectileSpeed > 0 ? skill.projectileSpeed : 14;
  }
  return skill.projectileSpeed ?? 14;
}

function applyDash(session: PlayerSession, tiles: number): void {
  const fx = session.facingX === 0 && session.facingY === 0 ? 1 : Math.sign(session.facingX) || 0;
  const fy = fx === 0 ? Math.sign(session.facingY) || 0 : 0;
  stepEntity(session.entity, fx, fy, Math.abs(tiles));
}

function dirFromTo(ax: number, ay: number, bx: number, by: number): { dx: number; dy: number } {
  const dx = Math.sign(bx - ax);
  const dy = Math.sign(by - ay);
  if (dx === 0 && dy === 0) {
    return { dx: 1, dy: 0 };
  }
  if (Math.abs(bx - ax) >= Math.abs(by - ay)) {
    return { dx, dy: 0 };
  }
  return { dx: 0, dy };
}

function lockPlayerMove(targetId: string, now: number): void {
  const player = getPlayersHook().find((p) => p.entity.id === targetId);
  if (player) {
    player.moveLockUntil = now + SHOVE_MOVE_LOCK_MS;
  }
}

function resolveScaling(skill: SkillDef, weapon: WeaponDef | undefined): Scaling {
  if (isAutoAttackSkill(skill)) {
    return weapon?.scaling ?? skill.scaling;
  }
  return skill.scaling;
}

function computeDamage(
  session: PlayerSession,
  target: Entity,
  skill: SkillDef,
  weapon: WeaponDef | undefined,
  now: number,
  absorb = true,
): { damage: number; crit: boolean; missed: boolean; element: string; advantage: string; resistHint: number } {
  const scaling = resolveScaling(skill, weapon);
  const powerAtk = session.entity.atk + (weapon?.atkBonus ?? 0) + attrBonus(session, "atk", now);
  const powerMatk = session.entity.magicAtk + (weapon?.magicAtkBonus ?? 0) + attrBonus(session, "magicAtk", now);
  const element = resolveAttackElement(session, skill, weapon);
  const extraMult = elemDmgMult(session, element, now);
  const targetSession = target.kind === "player"
    ? getPlayersHook().find((p) => p.entity.id === target.id)
    : undefined;
  const defBonus = targetSession ? attrBonus(targetSession, "def", now) : 0;
  const mdefBonus = targetSession ? attrBonus(targetSession, "magicResist", now) : 0;

  let flat = skill.damage;
  let damageType = scaling === "magic" ? "magic" as const : "physical" as const;
  if (skill.damageType === "maxHpPercent") {
    flat = Math.floor(target.maxHp * 0.08) + Math.floor((scaling === "magic" ? powerMatk : powerAtk) * 0.25);
  }

  const resolved: DamageResult = resolveDamage({
    attacker: {
      atk: powerAtk,
      matk: powerMatk,
      critRate: session.entity.critChance + attrBonus(session, "critChance", now),
      critDamage: session.entity.critDamage,
      hitRate: 1,
    },
    defender: {
      def: target.def + defBonus,
      mdef: target.magicResist + mdefBonus,
      dodgeRate: 0,
      elementalResist: {
        [toCombatElement(element)]: (target.resist?.[element] ?? 0) / 100,
      },
      element: toCombatElement(target.element),
    },
    skill: {
      damageType,
      baseDamageMultiplier: 1,
      flatDamage: flat,
      element: toCombatElement(element),
    },
    extraMult,
    config: loadCombatConfig(),
    rng: combatRng,
  });

  if (resolved.missed) {
    return { damage: 0, crit: false, missed: true, element, advantage: resolved.advantage, resistHint: 0 };
  }

  let damage = resolved.damage;
  if (targetSession) {
    damage = applyIncomingDamageMult(targetSession, damage, now);
  }
  if (absorb) {
    damage = absorbShield(target.id, damage, scaling, now);
  }
  return {
    damage: Math.max(0, damage),
    crit: resolved.crit,
    missed: false,
    element,
    advantage: resolved.advantage,
    resistHint: resolved.resistHint,
  };
}

function resolveWeaponForSkill(session: PlayerSession, skill: SkillDef): WeaponDef | undefined {
  if (skill.id === "auto_attack_off" || skill.weaponSlot === 2) {
    return session.equippedWeapon2Id ? weaponById(session.equippedWeapon2Id) : undefined;
  }

  return weaponById(session.equippedWeaponId);
}

function statusFromDef(def: NonNullable<SkillDef["status"]>, now: number, elementOverride?: Element): StatusInstance {
  return {
    id: def.id,
    kind: def.kind,
    until: now + def.durationMs,
    nextTick: def.tickMs ? now + def.tickMs : undefined,
    potency: def.potency,
    atkBonus: def.atkBonus,
    attr: def.attr,
    amount: def.amount,
    moveSpeedMult: def.moveSpeedMult,
    attackSpeedMult: def.attackSpeedMult,
    shieldHp: def.shieldHp,
    element: def.kind === "elem_dmg_up" ? (elementOverride ?? def.element) : def.element,
    elemDmgMult: def.elemDmgMult,
    dmgTakenMult: def.dmgTakenMult,
  };
}

/** Apply decoy / DR statuses; consumes decoy on first hit. */
export function applyIncomingDamageMult(session: PlayerSession, damage: number, now: number): number {
  pruneStatuses(session, now);
  let out = damage;
  const decoyIdx = session.statuses.findIndex((s) => s.kind === "dmg_taken_mult" && s.until > now);
  if (decoyIdx >= 0) {
    const decoy = session.statuses[decoyIdx]!;
    const mult = decoy.dmgTakenMult ?? 0.2;
    out = Math.max(0, Math.floor(out * mult));
    session.statuses.splice(decoyIdx, 1);
  }
  if (out > 0) {
    cancelRest(session);
  }
  return out;
}

function applyStatusToTarget(targetId: string, status: StatusInstance, session: PlayerSession): void {
  if (status.until <= Date.now()) {
    return;
  }
  if (targetId === session.entity.id) {
    session.statuses = session.statuses.filter((s) => s.id !== status.id);
    session.statuses.push(status);
    return;
  }
  const playerTarget = getPlayersHook().find((p) => p.entity.id === targetId);
  if (playerTarget) {
    playerTarget.statuses = playerTarget.statuses.filter((s) => s.id !== status.id);
    playerTarget.statuses.push(status);
    return;
  }
  attachMonsterStatus(targetId, status);
}

export function resolveSkillAffects(skill: SkillDef): SkillAffects {
  if (skill.affects) {
    return skill.affects;
  }
  if (skill.selfTarget) {
    return "self";
  }
  if (skill.targetingType === "ALLY_TARGET") {
    return "friendly";
  }
  return "all";
}

export function resolveAoeOrigin(skill: SkillDef): AoeOrigin {
  if (skill.aoeOrigin) {
    return skill.aoeOrigin;
  }
  if (skill.targetingType === "GROUND_CIRCLE") {
    return "ground";
  }
  if (skill.damageType === "aoe") {
    return skill.selfTarget ? "caster" : "target";
  }
  return "none";
}

function candidatesForAffects(casterId: string, affects: SkillAffects): Entity[] {
  const caster = findEntityHook(casterId);
  const mapId = caster?.mapId;
  const sameMap = (entity: Entity): boolean =>
    entity.hp > 0 && (!mapId || entity.mapId === mapId);

  if (affects === "self") {
    return caster && sameMap(caster) ? [caster] : [];
  }

  const out: Entity[] = [];
  if (affects === "hostile" || affects === "all") {
    for (const entity of listHostilesHook()) {
      if (entity.id !== casterId && sameMap(entity)) {
        out.push(entity);
      }
    }
  }
  if (affects === "friendly") {
    for (const session of getPlayersHook()) {
      if (sameMap(session.entity)) {
        out.push(session.entity);
      }
    }
  } else if (affects === "all") {
    for (const session of getPlayersHook()) {
      if (session.entity.id !== casterId && sameMap(session.entity)) {
        out.push(session.entity);
      }
    }
  }
  return out;
}

function collectAoeTargets(
  center: Entity,
  radius: number,
  casterId: string,
  affects: SkillAffects = "all",
): Entity[] {
  return candidatesForAffects(casterId, affects).filter((entity) => {
    return rangeGap(center, entity) <= radius + 0.05;
  });
}

function collectAoeAtPoint(
  cx: number,
  cy: number,
  radius: number,
  casterId: string,
  affects: SkillAffects = "all",
): Entity[] {
  return candidatesForAffects(casterId, affects).filter((entity) => {
    const gap = Math.max(0, distance(cx, cy, entity.x, entity.y) - (entity.hitRadius || 0.4));
    return gap <= radius + 0.05;
  });
}

function aoeRadiusOf(skill: { range: number; aoeRadius?: number }): number {
  if (skill.aoeRadius != null && skill.aoeRadius > 0) {
    return skill.aoeRadius;
  }
  return Math.max(0.5, skill.range * 0.5);
}

function normalizeDir(dx: number, dy: number): { dx: number; dy: number } | null {
  const len = Math.hypot(dx, dy);
  if (len < 0.001) {
    return null;
  }
  return { dx: dx / len, dy: dy / len };
}

/** Distance from point P to segment AB. */
function distPointToSegment(px: number, py: number, ax: number, ay: number, bx: number, by: number): number {
  const abx = bx - ax;
  const aby = by - ay;
  const apx = px - ax;
  const apy = py - ay;
  const ab2 = abx * abx + aby * aby;
  if (ab2 < 1e-8) {
    return Math.hypot(apx, apy);
  }
  let t = (apx * abx + apy * aby) / ab2;
  t = Math.max(0, Math.min(1, t));
  const qx = ax + abx * t;
  const qy = ay + aby * t;
  return Math.hypot(px - qx, py - qy);
}

function collectLinearTargets(
  caster: Entity,
  dx: number,
  dy: number,
  range: number,
  width: number,
  maxHits: number,
  affects: SkillAffects = "all",
): Entity[] {
  const ax = caster.x;
  const ay = caster.y;
  const bx = ax + dx * range;
  const by = ay + dy * range;
  const hits: { entity: Entity; along: number }[] = [];
  for (const entity of candidatesForAffects(caster.id, affects)) {
    const d = distPointToSegment(entity.x, entity.y, ax, ay, bx, by);
    const thresh = width * 0.5 + (entity.hitRadius || 0.4);
    if (d > thresh) {
      continue;
    }
    const along = (entity.x - ax) * dx + (entity.y - ay) * dy;
    if (along < -0.05 || along > range + 0.05) {
      continue;
    }
    hits.push({ entity, along });
  }
  hits.sort((a, b) => a.along - b.along);
  return hits.slice(0, maxHits).map((h) => h.entity);
}

function collectConeTargets(
  caster: Entity,
  dx: number,
  dy: number,
  range: number,
  coneAngleDeg: number,
  affects: SkillAffects = "all",
): Entity[] {
  const half = (coneAngleDeg * Math.PI) / 180 / 2;
  const out: Entity[] = [];
  for (const entity of candidatesForAffects(caster.id, affects)) {
    const ex = entity.x - caster.x;
    const ey = entity.y - caster.y;
    const dist = Math.hypot(ex, ey);
    const gap = Math.max(0, dist - (entity.hitRadius || 0.4) - (caster.hitRadius || 0.4));
    if (gap > range + 0.05) {
      continue;
    }
    if (dist < 0.001) {
      out.push(entity);
      continue;
    }
    const nx = ex / dist;
    const ny = ey / dist;
    const dot = nx * dx + ny * dy;
    const ang = Math.acos(Math.max(-1, Math.min(1, dot)));
    if (ang <= half + 0.02) {
      out.push(entity);
    }
  }
  return out;
}

function resolveAimDir(
  session: PlayerSession,
  aim: CastAim | undefined,
  targetId: string,
): { dx: number; dy: number } | ServerMessage {
  const fromAim = normalizeDir(aim?.aimDx ?? 0, aim?.aimDy ?? 0);
  if (fromAim) {
    return fromAim;
  }
  const target = findEntityHook(targetId);
  if (target) {
    const toward = normalizeDir(target.x - session.entity.x, target.y - session.entity.y);
    if (toward) {
      return toward;
    }
  }
  const facing = normalizeDir(session.facingX, session.facingY);
  if (facing) {
    return facing;
  }
  return { type: "error", code: "bad_aim", message: "Missing aim direction" };
}

function resolveGroundPoint(
  session: PlayerSession,
  skill: SkillDef,
  aim: CastAim | undefined,
  targetId: string,
): { x: number; y: number } | ServerMessage {
  let gx = aim?.aimX;
  let gy = aim?.aimY;
  if (gx == null || gy == null || Number.isNaN(gx) || Number.isNaN(gy)) {
    const target = findEntityHook(targetId);
    if (target) {
      gx = target.x;
      gy = target.y;
    } else {
      return { type: "error", code: "bad_aim", message: "Missing aim point" };
    }
  }
  const dx = gx - session.entity.x;
  const dy = gy - session.entity.y;
  const dist = Math.hypot(dx, dy);
  if (dist > skill.range && dist > 0.001) {
    const s = skill.range / dist;
    gx = session.entity.x + dx * s;
    gy = session.entity.y + dy * s;
  }
  return { x: gx, y: gy };
}

export function validateCast(
  session: PlayerSession,
  skillId: string,
  targetId: string,
  now: number,
  aim?: CastAim,
): ServerMessage | CastOk {
  if (isStunned(session, now)) {
    return { type: "error", code: "stunned", message: "You are stunned" };
  }
  if (session.talkingNpcId) {
    return { type: "error", code: "talking", message: "In conversation" };
  }
  if (rateLimited(session, now)) {
    return { type: "error", code: "rate_limited", message: "Too many actions" };
  }

  const skill = skillById(skillId);
  if (!skill) {
    return { type: "error", code: "unknown_skill", message: `Unknown skill ${skillId}` };
  }
  if (skillId !== "rest") {
    cancelRest(session);
  } else if (isResting(session, now)) {
    cancelRest(session);
    session.skillReadyAt[skillId] = now + (skill.cooldownMs ?? 1000);
    session.skillCdMs = session.skillCdMs ?? {};
    session.skillCdMs[skillId] = skill.cooldownMs ?? 1000;
    session.busyUntil = now + castRecoveryMs(skillId);
    return {
      ok: true,
      hits: [{ targetId: session.entity.id, damage: 0, hpAfter: session.entity.hp, crit: false }],
      mpAfter: session.entity.mp,
      moved: false,
      movedEntities: [],
      primaryTargetId: session.entity.id,
      aoe: false,
    };
  }
  if (
    session.unlockedSkillIds.length > 0 &&
    !session.unlockedSkillIds.includes(skillId) &&
    skillId !== "auto_attack" &&
    skillId !== "auto_attack_off"
  ) {
    return { type: "error", code: "locked_skill", message: "Skill not unlocked" };
  }

  const affects = resolveSkillAffects(skill);
  const aoeOrigin = resolveAoeOrigin(skill);
  if (!skill.selfTarget && affects !== "self" && affects !== "friendly" && isBlinded(session, now)) {
    return { type: "error", code: "blinded", message: "You are blinded" };
  }

  const readyAt = session.skillReadyAt[skillId] ?? 0;
  if (now < readyAt) {
    return { type: "error", code: "on_cooldown", message: `${skill.name} is on cooldown` };
  }
  if (now < (session.busyUntil ?? 0)) {
    return { type: "error", code: "busy", message: "Still recovering" };
  }

  if (session.entity.mp < skill.manaCost) {
    return { type: "error", code: "not_enough_mana", message: "Not enough mana" };
  }

  const weapon = resolveWeaponForSkill(session, skill);
  if (skill.id === "auto_attack_off" && !weapon) {
    return { type: "error", code: "no_offhand", message: "No offhand equipped" };
  }

  const isAuto = isAutoAttackSkill(skill);
  const isAoe = skill.damageType === "aoe";

  let targets: Entity[] = [];
  let resolvedTargetId = skill.selfTarget ? session.entity.id : targetId;
  let primary: Entity | undefined = findEntityHook(resolvedTargetId);
  let aimX: number | undefined;
  let aimY: number | undefined;
  let dirDx = 0;
  let dirDy = 0;
  let directionalProjectile = false;
  const blastRadius = aoeRadiusOf(skill);

  if (skill.targetingType === "GROUND_CIRCLE") {
    const pt = resolveGroundPoint(session, skill, aim, targetId);
    if ("type" in pt) {
      return pt;
    }
    aimX = pt.x;
    aimY = pt.y;
    targets = collectAoeAtPoint(pt.x, pt.y, blastRadius, session.entity.id, affects);
    resolvedTargetId = targets[0]?.id ?? "";
    primary = targets[0];
  } else if (skill.targetingType === "SKILLSHOT_LINEAR" || skill.targetingType === "SKILLSHOT_CONE") {
    const dir = resolveAimDir(session, aim, targetId);
    if ("type" in dir) {
      return dir;
    }
    dirDx = dir.dx;
    dirDy = dir.dy;
    session.facingX = dirDx;
    session.facingY = dirDy;
    if (skill.targetingType === "SKILLSHOT_LINEAR") {
      const width = skill.width ?? 0.7;
      if ((skill.projectileSpeed ?? 0) > 0) {
        directionalProjectile = true;
        targets = []; // hits resolved in flight
      } else {
        targets = collectLinearTargets(session.entity, dirDx, dirDy, skill.range, width, 3, affects);
      }
    } else {
      targets = collectConeTargets(session.entity, dirDx, dirDy, skill.range, skill.coneAngleDeg ?? 60, affects);
    }
    resolvedTargetId = targets[0]?.id ?? "";
    primary = targets[0];
  } else if (skill.targetingType === "ALLY_TARGET") {
    const requested = findEntityHook(targetId);
    if (requested && requested.kind !== "player") {
      return { type: "error", code: "invalid_target", message: "Target must be an ally" };
    }
    if (!requested || requested.hp <= 0 || requested.mapId !== session.entity.mapId) {
      primary = session.entity;
    } else {
      primary = requested;
    }
    const range = rangeGap(session.entity, primary);
    if (range > skill.range + 0.05) {
      return { type: "error", code: "out_of_range", message: "Target out of range" };
    }
    targets = [primary];
    resolvedTargetId = primary.id;
  } else if (aoeOrigin === "caster" && skill.targetingType === "NO_TARGET") {
    if (skill.movement?.kind === "dash" && isMoveLocked(session, now)) {
      return { type: "error", code: "move_locked", message: "You were shoved" };
    }
    primary = session.entity;
    resolvedTargetId = session.entity.id;
    targets = collectAoeTargets(session.entity, blastRadius, session.entity.id, affects);
    if (!targets.some((t) => t.id === session.entity.id)) {
      targets.unshift(session.entity);
    }
  } else {
    // UNIT / NO_TARGET / self
    if (!primary) {
      return { type: "error", code: "invalid_target", message: "Target not found" };
    }
    if (primary.mapId !== session.entity.mapId) {
      return { type: "error", code: "invalid_target", message: "Target is on another map" };
    }
    if (primary.hp <= 0) {
      return { type: "error", code: "target_dead", message: "Target is already dead" };
    }
    if (affects === "hostile" && primary.kind !== "monster") {
      return { type: "error", code: "invalid_target", message: "Target must be an enemy" };
    }

    if (skill.movement?.kind === "dash" && isMoveLocked(session, now)) {
      return { type: "error", code: "move_locked", message: "You were shoved" };
    }

    const range = rangeGap(session.entity, primary);
    const rangeLimit = isAuto ? (weapon?.range ?? 1.5) : skill.range;
    if (!skill.selfTarget && range > rangeLimit + 0.05) {
      return { type: "error", code: "out_of_range", message: "Target out of range" };
    }

    if (aoeOrigin === "target") {
      targets = collectAoeTargets(primary, blastRadius, session.entity.id, affects);
      if (!targets.some((t) => t.id === primary.id)) {
        targets.unshift(primary);
      }
    } else if (aoeOrigin === "caster") {
      targets = collectAoeTargets(session.entity, blastRadius, session.entity.id, affects);
      if (!targets.some((t) => t.id === session.entity.id)) {
        targets.unshift(session.entity);
      }
    } else if (isAoe) {
      targets = skill.selfTarget
        ? collectAoeTargets(session.entity, blastRadius, session.entity.id, affects)
        : collectAoeTargets(primary, blastRadius, session.entity.id, affects);
    } else {
      targets = [primary];
    }
  }

  const allowEmpty =
    skill.selfTarget ||
    skill.targetingType === "ALLY_TARGET" ||
    aoeOrigin === "caster" ||
    (aoeOrigin === "target" && Boolean(primary));
  if (
    !allowEmpty &&
    (isAoe || skill.targetingType === "GROUND_CIRCLE" || skill.targetingType === "SKILLSHOT_CONE") &&
    targets.length === 0 &&
    !directionalProjectile
  ) {
    return { type: "error", code: "no_targets", message: "No targets in range" };
  }

  // Linear projectile may fire into empty air
  if (skill.targetingType === "SKILLSHOT_LINEAR" && !directionalProjectile && targets.length === 0) {
    return { type: "error", code: "no_targets", message: "No targets in range" };
  }

  session.entity.mp -= skill.manaCost;
  const asMult = attackSpeedMult(session, now);
  const as = Math.max(0.5, (session.entity.attackSpeed + (weapon?.attackSpeedBonus ?? 0)) * asMult);
  const cdScale = Math.max(0.5, 1 / as);
  const duration = skill.cooldownMs * cdScale;
  session.skillReadyAt[skillId] = now + duration;
  session.skillCdMs = session.skillCdMs ?? {};
  session.skillCdMs[skillId] = duration;
  session.busyUntil = now + (isAuto ? duration : castRecoveryMs(skillId));

  const movedEntities: { id: string; x: number; y: number }[] = [];
  let moved = false;

  if (skill.movement?.kind === "dash") {
    applyDash(session, skill.movement.tiles);
    moved = true;
    movedEntities.push({ id: session.entity.id, x: session.entity.x, y: session.entity.y });
  }

  if ((skill.movement?.kind === "shove" || skill.movement?.kind === "pull") && primary) {
    if (!hasShoveResist(primary.id, now)) {
      const ignore = [session.entity.id];
      // Hook Shot: adjacent → AoE shove; otherwise pull primary.
      if (skill.id === "hook_shot") {
        const gap = rangeGap(session.entity, primary);
        const near = gap <= 1.55;
        if (near) {
          const radius = skill.aoeRadius ?? 1.8;
          const around = collectAoeTargets(session.entity, radius, session.entity.id);
          for (const t of around) {
            if (hasShoveResist(t.id, now)) continue;
            const dir = { dx: t.x - session.entity.x, dy: t.y - session.entity.y };
            if (stepEntity(t, dir.dx, dir.dy, skill.movement.tiles, ignore)) {
              movedEntities.push({ id: t.id, x: t.x, y: t.y });
              lockPlayerMove(t.id, now);
            }
          }
        } else {
          const dir = { dx: session.entity.x - primary.x, dy: session.entity.y - primary.y };
          if (stepEntity(primary, dir.dx, dir.dy, skill.movement.tiles, ignore)) {
            movedEntities.push({ id: primary.id, x: primary.x, y: primary.y });
            lockPlayerMove(primary.id, now);
          }
        }
      } else {
        const dir = skill.movement.kind === "shove"
          ? { dx: primary.x - session.entity.x, dy: primary.y - session.entity.y }
          : { dx: session.entity.x - primary.x, dy: session.entity.y - primary.y };
        if (stepEntity(primary, dir.dx, dir.dy, skill.movement.tiles, ignore)) {
          movedEntities.push({ id: primary.id, x: primary.x, y: primary.y });
          lockPlayerMove(primary.id, now);
        }
      }
    }
  }

  const useProjectile = directionalProjectile || needsProjectile(skill, weapon);
  let pendingStatus: StatusInstance | null = null;
  let statusDurationMs = 0;
  if (skill.status && skill.status.durationMs > 0) {
    const focusElement = skill.id === "elemental_focus"
      ? resolveAttackElement(session, { ...skill, id: "auto_attack" }, weapon)
      : skill.status.element;
    const status = statusFromDef(skill.status, now, focusElement);
    const applySelfOnly = skill.selfTarget || affects === "self";
    if (useProjectile && !applySelfOnly) {
      pendingStatus = status;
      statusDurationMs = skill.status.durationMs;
    } else if (applySelfOnly) {
      applyStatusToTarget(session.entity.id, status, session);
    } else {
      const statusTargets = targets.length > 0 ? targets : (primary ? [primary] : [session.entity]);
      for (const t of statusTargets) {
        applyStatusToTarget(t.id, { ...status }, session);
      }
    }
  }

  const hits: CastHit[] = [];
  const pendingHits: { targetId: string; damage: number; crit: boolean }[] = [];

  for (const target of targets) {
    if (skill.heal > 0 || (skill.healMp ?? 0) > 0) {
      target.hp = Math.min(target.maxHp, target.hp + skill.heal);
      if (target.id === session.entity.id) {
        session.entity.mp = Math.min(session.entity.maxMp, session.entity.mp + (skill.healMp ?? 0));
      }
      hits.push({ targetId: target.id, damage: skill.heal, hpAfter: target.hp, crit: false });
      continue;
    }

    if (skill.damage <= 0 && skill.damageType !== "maxHpPercent" && skill.damageType !== "aoe") {
      hits.push({ targetId: target.id, damage: 0, hpAfter: target.hp, crit: false });
      continue;
    }

    if (skill.damage > 0 || skill.damageType === "maxHpPercent" || skill.damageType === "aoe") {
      const { damage, crit, missed, element, advantage, resistHint } = computeDamage(session, target, skill, weapon, now, !useProjectile);
      if (useProjectile && !directionalProjectile) {
        pendingHits.push({ targetId: target.id, damage, crit });
      } else if (!directionalProjectile) {
        target.hp = Math.max(0, target.hp - damage);
        clampImmortalHook(target);
        hits.push({
          targetId: target.id,
          damage,
          hpAfter: target.hp,
          crit,
          element,
          missed,
          advantage,
          resistHint,
        });
      }
    }
  }

  if (!useProjectile && hits.length === 0 && primary) {
    hits.push({ targetId: primary.id, damage: 0, hpAfter: primary.hp, crit: false });
  }
  if (!useProjectile && hits.length === 0 && !primary && skill.selfTarget) {
    hits.push({ targetId: session.entity.id, damage: 0, hpAfter: session.entity.hp, crit: false });
  }

  return {
    ok: true,
    hits,
    mpAfter: session.entity.mp,
    moved,
    movedEntities,
    primaryTargetId: resolvedTargetId || session.entity.id,
    aoe: isAoe || skill.targetingType === "GROUND_CIRCLE" || aoeOrigin === "caster" || aoeOrigin === "target",
    aimX,
    aimY,
    aoeRadius: aoeOrigin !== "none" ? blastRadius : undefined,
    projectile: useProjectile
      ? {
          speed: projectileSpeedOf(skill, weapon),
          targetId: directionalProjectile ? "" : resolvedTargetId,
          skillId,
          vx: directionalProjectile ? dirDx : undefined,
          vy: directionalProjectile ? dirDy : undefined,
          maxRange: directionalProjectile ? skill.range : undefined,
          width: directionalProjectile ? (skill.width ?? 0.7) : undefined,
          pendingHits,
          pendingStatus,
          statusDurationMs,
        }
      : undefined,
  };
}

export function applyPendingHit(
  targetId: string,
  damage: number,
  crit: boolean,
  skillId: string,
  now: number,
): CastHit | null {
  const target = findEntityHook(targetId);
  if (!target || target.hp <= 0) {
    return null;
  }
  const skill = skillById(skillId);
  const scaling = skill?.scaling ?? "atk";
  const finalDmg = absorbShield(targetId, damage, scaling, now);
  target.hp = Math.max(0, target.hp - finalDmg);
  clampImmortalHook(target);
  return { targetId, damage: finalDmg, hpAfter: target.hp, crit };
}

/** Live skillshot collision: compute + apply damage from caster session. */
export function hitFromCaster(
  casterId: string,
  targetId: string,
  skillId: string,
  now: number,
): CastHit | null {
  const caster = getPlayersHook().find((p) => p.entity.id === casterId);
  const target = findEntityHook(targetId);
  const skill = skillById(skillId);
  if (!caster || !target || !skill || target.hp <= 0) {
    return null;
  }
  const weapon = resolveWeaponForSkill(caster, skill);
  const { damage, crit, missed, element, advantage, resistHint } = computeDamage(caster, target, skill, weapon, now, true);
  target.hp = Math.max(0, target.hp - damage);
  clampImmortalHook(target);
  return { targetId, damage, hpAfter: target.hp, crit, element, missed, advantage, resistHint };
}

export function findHostilesNearPoint(x: number, y: number, radius: number, excludeId: string): Entity[] {
  return listHostilesHook().filter((entity) => {
    if (entity.id === excludeId || entity.hp <= 0) {
      return false;
    }
    return Math.hypot(entity.x - x, entity.y - y) <= radius + (entity.hitRadius || 0.4);
  });
}

export function applyStatusOnHit(targetId: string, status: StatusInstance, durationMs: number, now: number): void {
  const applied: StatusInstance = { ...status, until: now + durationMs };
  const playerTarget = getPlayersHook().find((p) => p.entity.id === targetId);
  if (playerTarget) {
    playerTarget.statuses = playerTarget.statuses.filter((s) => s.id !== applied.id);
    playerTarget.statuses.push(applied);
    return;
  }
  attachMonsterStatus(targetId, applied);
}
