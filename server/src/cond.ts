import { isMoveLocked, isStunned } from "./combat.js";
import { isResting } from "./rest.js";
import type { PlayerSession, ServerMessage } from "./types.js";

export const HEARTBEAT_STALE_MS = 25_000;

export type SessionCond = {
  canMove: boolean;
  canAct: boolean;
  resting: boolean;
};

export function sessionCond(session: PlayerSession, now: number): SessionCond {
  const dead = session.entity.hp <= 0;
  const stunned = isStunned(session, now);
  const locked = isMoveLocked(session, now);
  const talking = Boolean(session.talkingNpcId);
  const resting = isResting(session, now);
  return {
    canMove: !dead && !stunned && !locked && !talking,
    resting,
    canAct: !dead && !stunned && !talking,
  };
}

export function condChanged(prev: SessionCond | undefined, next: SessionCond): boolean {
  return !prev || prev.canMove !== next.canMove || prev.canAct !== next.canAct || prev.resting !== next.resting;
}

/** Snapshot cond and return a packet only when flags flipped. */
export function takeCondSync(session: PlayerSession, now: number): ServerMessage | null {
  const next = sessionCond(session, now);
  if (!condChanged(session.lastCond, next)) {
    return null;
  }
  session.lastCond = next;
  return {
    type: "sync_cond",
    entityId: session.entity.id,
    canMove: next.canMove,
    canAct: next.canAct,
    resting: next.resting,
    serverTime: now,
  };
}

export function isSessionStale(
  lastHeardAt: number | undefined,
  now: number,
  staleMs = HEARTBEAT_STALE_MS,
): boolean {
  if (lastHeardAt == null || lastHeardAt <= 0) {
    return false;
  }
  return now - lastHeardAt > staleMs;
}
