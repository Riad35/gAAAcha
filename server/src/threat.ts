import type { ServerMessage } from "./types.js";

/** Per-monster threat table: playerId → 0..100 */
export const monsterThreat = new Map<string, Map<string, number>>();

const lastThreatSync = new Map<string, number>();
const THREAT_SYNC_MS = 200;

export function clearThreat(monsterId: string): void {
  monsterThreat.delete(monsterId);
  lastThreatSync.delete(monsterId);
}

export function clearAllThreat(): void {
  monsterThreat.clear();
  lastThreatSync.clear();
}

function table(monsterId: string): Map<string, number> {
  let t = monsterThreat.get(monsterId);
  if (!t) {
    t = new Map();
    monsterThreat.set(monsterId, t);
  }
  return t;
}

export function addThreat(monsterId: string, playerId: string, amount: number): void {
  if (amount <= 0) {
    return;
  }
  const t = table(monsterId);
  const next = Math.min(100, (t.get(playerId) ?? 0) + amount);
  t.set(playerId, next);
}

export function seedThreat(monsterId: string, playerId: string, amount = 20): void {
  const t = table(monsterId);
  const cur = t.get(playerId) ?? 0;
  if (cur < amount) {
    t.set(playerId, amount);
  }
}

export function threatFromDamage(damage: number, maxHp: number): number {
  if (maxHp <= 0 || damage <= 0) {
    return 0;
  }
  return Math.min(25, Math.max(1, Math.floor((damage / maxHp) * 40)));
}

export function topThreatId(
  monsterId: string,
  alivePlayerIds: Set<string>,
  minThreat = 10,
): string | null {
  const t = monsterThreat.get(monsterId);
  if (!t) {
    return null;
  }
  let bestId: string | null = null;
  let best = -1;
  for (const [playerId, pct] of t) {
    if (!alivePlayerIds.has(playerId) || pct < minThreat) {
      continue;
    }
    if (pct > best) {
      best = pct;
      bestId = playerId;
    }
  }
  return bestId;
}

export function threatEntries(monsterId: string): { playerId: string; pct: number }[] {
  const t = monsterThreat.get(monsterId);
  if (!t) {
    return [];
  }
  return [...t.entries()]
    .map(([playerId, pct]) => ({ playerId, pct: Math.round(pct) }))
    .filter((e) => e.pct > 0)
    .sort((a, b) => b.pct - a.pct);
}

export function decayThreat(dtSec: number): void {
  const step = 2 * dtSec;
  for (const [monsterId, t] of monsterThreat) {
    for (const [playerId, pct] of t) {
      const next = pct - step;
      if (next <= 0) {
        t.delete(playerId);
      } else {
        t.set(playerId, next);
      }
    }
    if (t.size === 0) {
      monsterThreat.delete(monsterId);
    }
  }
}

export function syncThreatMessage(
  monsterId: string,
  now: number,
  force = false,
): ServerMessage | null {
  const last = lastThreatSync.get(monsterId) ?? 0;
  if (!force && now - last < THREAT_SYNC_MS) {
    return null;
  }
  lastThreatSync.set(monsterId, now);
  const entries = threatEntries(monsterId);
  const topId = entries[0]?.playerId ?? null;
  return { type: "sync_threat", monsterId, entries, topId };
}

export function pruneDeadPlayers(monsterId: string, alive: Set<string>): void {
  const t = monsterThreat.get(monsterId);
  if (!t) {
    return;
  }
  for (const playerId of [...t.keys()]) {
    if (!alive.has(playerId)) {
      t.delete(playerId);
    }
  }
}
