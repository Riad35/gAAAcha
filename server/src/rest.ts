import type { PlayerSession } from "./types.js";

export const REST_STATUS_ID = "resting";
export const REST_DURATION_MS = 60 * 60 * 1000;

export function isResting(session: PlayerSession, now = Date.now()): boolean {
  return session.statuses.some((s) => s.id === REST_STATUS_ID && s.until > now);
}

export function cancelRest(session: PlayerSession): boolean {
  const before = session.statuses.length;
  session.statuses = session.statuses.filter((s) => s.id !== REST_STATUS_ID);
  return session.statuses.length !== before;
}

export function startRest(session: PlayerSession, now: number): void {
  session.statuses = session.statuses.filter((s) => s.id !== REST_STATUS_ID);
  session.statuses.push({
    id: REST_STATUS_ID,
    kind: "buff",
    until: now + REST_DURATION_MS,
  });
}
