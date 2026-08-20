import { lootTables } from "./data.js";
import { addItem } from "./shop.js";
import type { LootDropDef, LootTable, PlayerSession, ServerMessage } from "./types.js";

export type Rng = () => number;

export type RolledLoot = {
  gold: number;
  xp: number;
  items: { itemId: string; quantity: number }[];
};

function randInt(min: number, max: number, rng: Rng): number {
  if (max <= min) {
    return min;
  }
  return min + Math.floor(rng() * (max - min + 1));
}

export function lootTableFor(monsterType: string): LootTable {
  return lootTables[monsterType] ?? lootTables.default;
}

export function killXpFor(monsterType: string): number {
  return lootTableFor(monsterType).xp;
}

function rollDrop(drop: LootDropDef, rng: Rng): { itemId: string; quantity: number } | null {
  if (rng() >= drop.chance) {
    return null;
  }
  const minQty = drop.minQty ?? 1;
  const maxQty = drop.maxQty ?? minQty;
  return { itemId: drop.itemId, quantity: randInt(minQty, maxQty, rng) };
}

export function rollKillRewards(table: LootTable, rng: Rng): RolledLoot {
  const gold = randInt(table.goldMin, table.goldMax, rng);
  const items: { itemId: string; quantity: number }[] = [];
  for (const drop of table.drops) {
    const rolled = rollDrop(drop, rng);
    if (rolled && rolled.quantity > 0) {
      items.push(rolled);
    }
  }
  return { gold, xp: table.xp, items };
}

export function applyKillLoot(session: PlayerSession, rolled: RolledLoot): ServerMessage {
  session.gold += rolled.gold;
  for (const item of rolled.items) {
    addItem(session, item.itemId, item.quantity);
  }
  const primary = rolled.items[0];
  return {
    type: "sync_loot",
    itemId: primary?.itemId ?? "gold",
    quantity: primary?.quantity ?? rolled.gold,
    inventory: session.inventory,
    gold: session.gold,
  };
}
