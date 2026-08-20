import { bannerById, spiritById, weaponById } from "./data.js";
import { removeItem } from "./shop.js";
import type {
  BannerDef,
  GachaDrop,
  InventorySlot,
  PityCounter,
  PityView,
  PlayerSession,
  Rarity,
  ServerMessage,
} from "./types.js";

export const INVENTORY_SIZE = 144;
export const PULL_DUST_ID = "item_dust";
export const PULL_TICKET_ID = "item_ticket";
export const DEFAULT_PULL_COST_DUST = 10;
export const DEFAULT_TEN_PULL_COST_DUST = 90;
export type Rng = () => number;

export function emptyInventory(size = INVENTORY_SIZE): InventorySlot[] {
  return Array.from({ length: size }, (_, slotIndex) => ({
    slotIndex,
    itemId: null,
    quantity: 0,
  }));
}

/** Expand/truncate legacy saves to INVENTORY_SIZE. */
export function padInventory(slots?: InventorySlot[] | null): InventorySlot[] {
  const out = emptyInventory();
  if (!slots?.length) {
    return out;
  }
  for (const s of slots) {
    if (!s || typeof s.slotIndex !== "number") {
      continue;
    }
    if (s.slotIndex < 0 || s.slotIndex >= INVENTORY_SIZE) {
      continue;
    }
    out[s.slotIndex] = {
      slotIndex: s.slotIndex,
      itemId: s.itemId ?? null,
      quantity: s.quantity ?? 0,
    };
  }
  return out;
}

/** New Adventurer bag — pulls and shops must matter. Homestone is granted separately. */
export const STARTER_BAG: { id: string; qty: number }[] = [
  { id: "sword_iron", qty: 1 },
  { id: "bow_hunter", qty: 1 },
  { id: "item_ration", qty: 1 },
  { id: "item_dust", qty: 5 },
];

export function seedStarterInventory(slots: InventorySlot[]): void {
  const starter = STARTER_BAG;
  for (const slot of slots) {
    slot.itemId = null;
    slot.quantity = 0;
  }
  let i = 0;
  for (const item of starter) {
    if (i >= slots.length) {
      break;
    }
    slots[i].itemId = item.id;
    slots[i].quantity = item.qty;
    i += 1;
  }
}

export function pityFor(session: PlayerSession, bannerId: string): PityCounter {
  session.pity[bannerId] ??= { bannerId, pity: 0, totalPulls: 0 };
  return session.pity[bannerId];
}

export function ssrChance(banner: BannerDef, pity: number): number {
  const pullNumber = pity + 1;
  if (pullNumber >= banner.hardPity) {
    return 1;
  }
  if (pullNumber < banner.softPityStart) {
    return banner.baseSsrRate;
  }
  const extra = (pullNumber - banner.softPityStart + 1) * banner.softStep;
  return Math.min(1, banner.baseSsrRate + extra);
}

export function pityView(banner: BannerDef, counter: PityCounter): PityView {
  return {
    bannerId: banner.id,
    bannerName: banner.name,
    count: counter.pity,
    hardPity: banner.hardPity,
    softPityStart: banner.softPityStart,
    nextSsrChance: ssrChance(banner, counter.pity),
    baseSsrRate: banner.baseSsrRate,
    baseSrRate: banner.baseSrRate,
    pullCostDust: banner.pullCostDust ?? DEFAULT_PULL_COST_DUST,
    tenPullCostDust: banner.tenPullCostDust ?? DEFAULT_TEN_PULL_COST_DUST,
  };
}

export function rollRarity(banner: BannerDef, pity: number, rng: Rng): Rarity {
  const roll = rng();
  const ssr = ssrChance(banner, pity);
  if (roll < ssr) {
    return "ssr";
  }
  if (roll < ssr + banner.baseSrRate) {
    return "sr";
  }
  return "r";
}

export function pickItem(banner: BannerDef, rarity: Rarity, rng: Rng): string {
  const pool = banner.pool[rarity];
  const index = Math.min(pool.length - 1, Math.floor(rng() * pool.length));
  return pool[index];
}

function emptySlotCount(slots: InventorySlot[]): number {
  return slots.filter((slot) => slot.itemId === null).length;
}

function newItemCount(slots: InventorySlot[], drops: GachaDrop[]): number {
  const known = new Set(slots.flatMap((slot) => (slot.itemId ? [slot.itemId] : [])));
  const seen = new Set<string>();
  let fresh = 0;
  for (const drop of drops) {
    if (known.has(drop.itemId) || seen.has(drop.itemId)) {
      continue;
    }
    seen.add(drop.itemId);
    fresh += 1;
  }
  return fresh;
}

export function canFitDrops(slots: InventorySlot[], drops: GachaDrop[]): boolean {
  return newItemCount(slots, drops) <= emptySlotCount(slots);
}

export function grantDrop(slots: InventorySlot[], itemId: string): boolean {
  const stack = slots.find((slot) => slot.itemId === itemId);
  if (stack) {
    stack.quantity += 1;
    return true;
  }
  const empty = slots.find((slot) => slot.itemId === null);
  if (!empty) {
    return false;
  }
  empty.itemId = itemId;
  empty.quantity = 1;
  return true;
}

function applyPity(counter: PityCounter, rarity: Rarity): void {
  counter.totalPulls += 1;
  counter.pity = rarity === "ssr" ? 0 : counter.pity + 1;
}

function rollDrop(banner: BannerDef, counter: PityCounter, rng: Rng): GachaDrop {
  const rarity = rollRarity(banner, counter.pity, rng);
  applyPity(counter, rarity);
  return { itemId: pickItem(banner, rarity, rng), rarity };
}

function upgradeLastToSr(banner: BannerDef, drops: GachaDrop[], rng: Rng): void {
  if (drops.some((drop) => drop.rarity !== "r")) {
    return;
  }
  const last = drops[drops.length - 1];
  last.rarity = "sr";
  last.itemId = pickItem(banner, "sr", rng);
}

function pullMany(banner: BannerDef, counter: PityCounter, count: number, rng: Rng): GachaDrop[] {
  const drops = Array.from({ length: count }, () => rollDrop(banner, counter, rng));
  if (count === 10) {
    upgradeLastToSr(banner, drops, rng);
  }
  return drops;
}

function countOwned(slots: InventorySlot[], itemId: string): number {
  return slots.reduce((sum, slot) => (slot.itemId === itemId ? sum + slot.quantity : sum), 0);
}

export function pullDustCost(banner: BannerDef, count: number): number {
  if (count === 10) {
    return banner.tenPullCostDust ?? DEFAULT_TEN_PULL_COST_DUST;
  }
  return banner.pullCostDust ?? DEFAULT_PULL_COST_DUST;
}

function cannotAffordPull(session: PlayerSession, banner: BannerDef, count: number): ServerMessage | null {
  const dustCost = pullDustCost(banner, count);
  if (countOwned(session.inventory, PULL_TICKET_ID) >= count) {
    return null;
  }
  if (countOwned(session.inventory, PULL_DUST_ID) >= dustCost) {
    return null;
  }
  return {
    type: "error",
    code: "not_enough_dust",
    message: `Need ${dustCost} Star Dust or ${count} Banner Ticket${count === 1 ? "" : "s"}`,
  };
}

function spendPullCurrency(session: PlayerSession, banner: BannerDef, count: number): void {
  if (countOwned(session.inventory, PULL_TICKET_ID) >= count) {
    removeItem(session, PULL_TICKET_ID, count);
    return;
  }
  removeItem(session, PULL_DUST_ID, pullDustCost(banner, count));
}

function resolveBanner(bannerId: string, count: number): ServerMessage | BannerDef {
  if (count !== 1 && count !== 10) {
    return { type: "error", code: "invalid_pull", message: "Pull count must be 1 or 10" };
  }
  return bannerById(bannerId) ?? { type: "error", code: "unknown_banner", message: `Unknown banner ${bannerId}` };
}

function commitPull(session: PlayerSession, banner: BannerDef, counter: PityCounter, results: GachaDrop[]) {
  session.pity[banner.id] = counter;
  for (const drop of results) {
    grantDrop(session.inventory, drop.itemId);
    if (weaponById(drop.itemId) && !session.weaponIds.includes(drop.itemId)) {
      session.weaponIds.push(drop.itemId);
    }
    if (spiritById(drop.itemId) && !session.spiritIds.includes(drop.itemId)) {
      session.spiritIds.push(drop.itemId);
    }
  }
  return { ok: true as const, results, pity: pityView(banner, counter), inventory: session.inventory };
}

export function pullGacha(
  session: PlayerSession,
  bannerId: string,
  count: number,
  rng: Rng,
): ServerMessage | { ok: true; results: GachaDrop[]; pity: PityView; inventory: InventorySlot[] } {
  const banner = resolveBanner(bannerId, count);
  if ("type" in banner) {
    return banner;
  }
  const unpaid = cannotAffordPull(session, banner, count);
  if (unpaid) {
    return unpaid;
  }
  const counter = { ...pityFor(session, bannerId) };
  const results = pullMany(banner, counter, count, rng);
  if (!canFitDrops(session.inventory, results)) {
    return { type: "error", code: "inventory_full", message: "Not enough inventory slots" };
  }
  spendPullCurrency(session, banner, count);
  return commitPull(session, banner, counter, results);
}
