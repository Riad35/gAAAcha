import { removeItem } from "./shop.js";
import type { PlayerSession, ServerMessage } from "./types.js";

export const NPC_TALK_RANGE = 2.2;

export type EnhanceSlot =
  | "mainhand"
  | "armor"
  | "helm"
  | "boots"
  | "gloves"
  | "amulet"
  | "ring1"
  | "ring2"
  | "subclass";

export const ENHANCE_SLOTS: EnhanceSlot[] = [
  "mainhand",
  "armor",
  "helm",
  "boots",
  "gloves",
  "amulet",
  "ring1",
  "ring2",
  "subclass",
];

export const ENHANCE_MAX = 5;
export const ENHANCE_STAT_PER_LEVEL = 2;

export function enhanceCost(currentLevel: number): { gold: number; dust: number } {
  const n = Math.max(1, currentLevel + 1);
  return { gold: 50 * n, dust: 2 * n };
}

export function equippedIdForEnhance(session: PlayerSession, slot: EnhanceSlot): string | null {
  if (slot === "mainhand") {
    return session.equippedWeaponId || null;
  }
  if (slot === "subclass") {
    return session.equippedSubclassId ?? null;
  }
  if (slot === "amulet") {
    return session.equippedAmuletId ?? session.equippedAccessoryId ?? null;
  }
  if (slot === "ring1") {
    return session.equippedRing1Id ?? null;
  }
  if (slot === "ring2") {
    return session.equippedRing2Id ?? null;
  }
  if (slot === "armor") {
    return session.equippedArmorId;
  }
  if (slot === "helm") {
    return session.equippedHelmId;
  }
  if (slot === "boots") {
    return session.equippedBootsId;
  }
  return session.equippedGlovesId;
}

export function enhanceLevelOf(session: PlayerSession, slot: EnhanceSlot): number {
  const n = session.enhanceLevels?.[slot] ?? 0;
  return Math.max(0, Math.min(ENHANCE_MAX, Math.floor(n)));
}

export function enhanceGear(
  session: PlayerSession,
  slot: string,
): ServerMessage | { ok: true; level: number; gold: number } {
  if (!ENHANCE_SLOTS.includes(slot as EnhanceSlot)) {
    return { type: "error", code: "bad_gear", message: "Cannot enhance that slot" };
  }
  const key = slot as EnhanceSlot;
  const itemId = equippedIdForEnhance(session, key);
  if (!itemId) {
    return { type: "error", code: "empty_slot", message: "Nothing equipped there" };
  }
  const cur = enhanceLevelOf(session, key);
  if (cur >= ENHANCE_MAX) {
    return { type: "error", code: "enhance_max", message: "Already +5" };
  }
  const cost = enhanceCost(cur);
  if (session.gold < cost.gold) {
    return { type: "error", code: "not_enough_gold", message: "Not enough gold" };
  }
  if (!removeItem(session, "item_dust", cost.dust)) {
    return { type: "error", code: "not_enough_dust", message: `Need ${cost.dust} star dust` };
  }
  session.gold -= cost.gold;
  session.enhanceLevels = session.enhanceLevels ?? {};
  session.enhanceLevels[key] = cur + 1;
  return { ok: true, level: cur + 1, gold: session.gold };
}

export function beginNpcTalk(session: PlayerSession, npcId: string): void {
  session.talkingNpcId = npcId;
}

export function closeNpcTalk(session: PlayerSession): void {
  session.talkingNpcId = null;
}

export function isTalking(session: PlayerSession): boolean {
  return Boolean(session.talkingNpcId);
}
