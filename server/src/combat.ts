import { defaultMap, skillById } from "./data.js";
import { findEntity } from "./world.js";
import type { PlayerSession, ServerMessage } from "./types.js";

const MAX_ACTIONS_PER_SEC = 10;

function distance(ax: number, ay: number, bx: number, by: number): number {
  return Math.hypot(ax - bx, ay - by);
}

function inBounds(x: number, y: number): boolean {
  return x >= 0 && y >= 0 && x <= defaultMap.width - 1 && y <= defaultMap.height - 1;
}

function isBlocked(x: number, y: number): boolean {
  const tx = Math.round(x);
  const ty = Math.round(y);
  return defaultMap.blocked.some((tile) => tile.x === tx && tile.y === ty);
}

function rateLimited(session: PlayerSession, now: number): boolean {
  session.actionTimes = session.actionTimes.filter((time) => now - time < 1000);
  if (session.actionTimes.length >= MAX_ACTIONS_PER_SEC) {
    return true;
  }
  session.actionTimes.push(now);
  return false;
}

export function validateMove(
  session: PlayerSession,
  x: number,
  y: number,
  now: number,
): ServerMessage | { ok: true; x: number; y: number } {
  if (rateLimited(session, now)) {
    return { type: "error", code: "rate_limited", message: "Too many actions" };
  }
  if (!Number.isFinite(x) || !Number.isFinite(y)) {
    return { type: "error", code: "invalid_move", message: "Coordinates must be numbers" };
  }
  if (!inBounds(x, y) || isBlocked(x, y)) {
    return { type: "error", code: "blocked", message: "Tile is not walkable" };
  }

  const elapsedSec = Math.max(0, (now - session.lastMoveAt) / 1000);
  const maxDist = session.entity.moveSpeed * Math.max(elapsedSec, 1 / 20);
  const dist = distance(session.entity.x, session.entity.y, x, y);
  if (dist > maxDist + 0.05) {
    return { type: "error", code: "too_fast", message: "Move exceeds walk speed" };
  }

  return { ok: true, x, y };
}

export function validateCast(
  session: PlayerSession,
  skillId: string,
  targetId: string,
  now: number,
): ServerMessage | { ok: true; damage: number; hpAfter: number; mpAfter: number; targetId: string } {
  if (rateLimited(session, now)) {
    return { type: "error", code: "rate_limited", message: "Too many actions" };
  }

  const skill = skillById(skillId);
  if (!skill) {
    return { type: "error", code: "unknown_skill", message: `Unknown skill ${skillId}` };
  }

  const readyAt = session.skillReadyAt[skillId] ?? 0;
  if (now < readyAt) {
    return { type: "error", code: "on_cooldown", message: `${skill.name} is on cooldown` };
  }

  if (session.entity.mp < skill.manaCost) {
    return { type: "error", code: "not_enough_mana", message: "Not enough mana" };
  }

  const resolvedTargetId = skill.selfTarget ? session.entity.id : targetId;
  const target = findEntity(resolvedTargetId);
  if (!target) {
    return { type: "error", code: "invalid_target", message: "Target not found" };
  }

  const range = distance(session.entity.x, session.entity.y, target.x, target.y);
  if (!skill.selfTarget && range > skill.range) {
    return { type: "error", code: "out_of_range", message: "Target out of range" };
  }

  session.entity.mp -= skill.manaCost;
  session.skillReadyAt[skillId] = now + skill.cooldownMs;

  let hpAfter = target.hp;
  let damage = 0;
  if (skill.heal > 0) {
    target.hp = Math.min(target.maxHp, target.hp + skill.heal);
    hpAfter = target.hp;
  } else {
    damage = Math.max(1, session.entity.atk + skill.damage - target.def);
    target.hp = Math.max(0, target.hp - damage);
    hpAfter = target.hp;
  }

  return { ok: true, damage, hpAfter, mpAfter: session.entity.mp, targetId: resolvedTargetId };
}
