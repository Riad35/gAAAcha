import type { PlayerSession, ServerMessage } from "./types.js";

/** XP from `level` to `level + 1`. Sum L1→20 is 3192 — a tower session chain can finish it. */
export function xpToNextLevel(level: number): number {
  return 28 + level * 14;
}

export function totalXpToReach(targetLevel: number): number {
  let sum = 0;
  for (let from = 1; from < targetLevel; from += 1) {
    sum += xpToNextLevel(from);
  }
  return sum;
}

/** Level-up HP/MP bumps; caller should re-apply gear after level changes if needed. */
export function grantXp(
  session: PlayerSession,
  amount: number,
  onLevel?: (session: PlayerSession) => void,
): ServerMessage[] {
  if (amount <= 0) {
    return [];
  }
  session.xp += amount;
  const out: ServerMessage[] = [];
  let guard = 0;
  let leveled = false;
  while (session.xp >= xpToNextLevel(session.level) && guard < 80) {
    session.xp -= xpToNextLevel(session.level);
    session.level += 1;
    session.skillPoints += 1;
    session.entity.maxHp += 8;
    session.entity.maxMp += 4;
    leveled = true;
    guard += 1;
  }
  if (leveled) {
    onLevel?.(session);
    session.entity.hp = session.entity.maxHp;
    session.entity.mp = session.entity.maxMp;
  }
  out.push({
    type: "sync_xp",
    level: session.level,
    xp: session.xp,
    xpToLevel: xpToNextLevel(session.level),
    skillPoints: session.skillPoints,
  });
  out.push({
    type: "sync_vitals",
    entityId: session.entity.id,
    hp: session.entity.hp,
    maxHp: session.entity.maxHp,
    mp: session.entity.mp,
    maxMp: session.entity.maxMp,
    gold: session.gold,
  });
  return out;
}
