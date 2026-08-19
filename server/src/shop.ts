import { itemById, shopById, weaponById } from "./data.js";
import type { InventorySlot, PlayerSession, ServerMessage, ShopDef } from "./types.js";

function findStack(inv: InventorySlot[], itemId: string): InventorySlot | undefined {
  return inv.find((s) => s.itemId === itemId);
}

function findEmpty(inv: InventorySlot[]): InventorySlot | undefined {
  return inv.find((s) => s.itemId === null);
}

export function addItem(session: PlayerSession, itemId: string, quantity: number): boolean {
  if (quantity <= 0) {
    return false;
  }
  const stack = findStack(session.inventory, itemId);
  if (stack) {
    stack.quantity += quantity;
    return true;
  }
  const empty = findEmpty(session.inventory);
  if (!empty) {
    return false;
  }
  empty.itemId = itemId;
  empty.quantity = quantity;
  return true;
}

export function removeItem(session: PlayerSession, itemId: string, quantity: number): boolean {
  const stack = findStack(session.inventory, itemId);
  if (!stack || stack.quantity < quantity) {
    return false;
  }
  stack.quantity -= quantity;
  if (stack.quantity <= 0) {
    stack.itemId = null;
    stack.quantity = 0;
  }
  return true;
}

export function buyFromShop(
  session: PlayerSession,
  shopId: string,
  itemId: string,
  quantity = 1,
): { error?: ServerMessage; shop?: ShopDef } {
  const shop = shopById(shopId);
  if (!shop) {
    return { error: { type: "error", code: "bad_shop", message: "Unknown shop" } };
  }
  const entry = shop.entries.find((e) => e.itemId === itemId);
  if (!entry) {
    return { error: { type: "error", code: "bad_item", message: "Not sold here" } };
  }
  const qty = Math.max(1, Math.min(20, Math.floor(quantity)));
  const cost = entry.buyPrice * qty;
  if (session.gold < cost) {
    return { error: { type: "error", code: "not_enough_gold", message: "Not enough gold" } };
  }
  if (!addItem(session, itemId, qty)) {
    return { error: { type: "error", code: "inventory_full", message: "Inventory full" } };
  }
  session.gold -= cost;
  if (weaponById(itemId) && !session.weaponIds.includes(itemId)) {
    session.weaponIds.push(itemId);
  }
  return { shop };
}

export function sellToShop(
  session: PlayerSession,
  shopId: string,
  itemId: string,
  quantity = 1,
): { error?: ServerMessage; shop?: ShopDef } {
  const shop = shopById(shopId);
  if (!shop) {
    return { error: { type: "error", code: "bad_shop", message: "Unknown shop" } };
  }
  const entry = shop.entries.find((e) => e.itemId === itemId);
  if (!entry) {
    return { error: { type: "error", code: "bad_item", message: "Shop will not buy that" } };
  }
  const qty = Math.max(1, Math.min(20, Math.floor(quantity)));
  if (!removeItem(session, itemId, qty)) {
    return { error: { type: "error", code: "missing_item", message: "You do not have that" } };
  }
  session.gold += entry.sellPrice * qty;
  return { shop };
}

export function useInventoryItem(
  session: PlayerSession,
  slotIndex: number,
  now: number,
  helpers: {
    teleportHome: (requireCooldown: boolean) => { error?: ServerMessage };
    changeClass?: (classId: string, cardItemId: string) => { error?: ServerMessage };
  },
): { error?: ServerMessage; messages: ServerMessage[] } {
  const slot = session.inventory.find((s) => s.slotIndex === slotIndex);
  if (!slot?.itemId || slot.quantity <= 0) {
    return { error: { type: "error", code: "empty_slot", message: "Empty slot" }, messages: [] };
  }
  const def = itemById(slot.itemId);
  if (!def?.use) {
    // Allow equipping owned weapons from inventory as secondary
    if (def && weaponById(slot.itemId) && session.weaponIds.includes(slot.itemId)) {
      if (session.equippedWeaponId === slot.itemId) {
        return { error: { type: "error", code: "already_primary", message: "Already primary" }, messages: [] };
      }
      session.equippedWeapon2Id = slot.itemId;
      return {
        messages: [
          { type: "sync_inventory", inventory: session.inventory, gold: session.gold },
          {
            type: "sync_chat",
            channel: "server",
            fromId: "system",
            fromName: "System",
            text: `Secondary weapon: ${def.name}`,
            serverTime: now,
          },
        ],
      };
    }
    return { error: { type: "error", code: "not_usable", message: "Cannot use that" }, messages: [] };
  }
  if (session.entity.hp <= 0) {
    return { error: { type: "error", code: "you_are_dead", message: "You are dead" }, messages: [] };
  }

  if (def.use === "homestone") {
    const result = helpers.teleportHome(true);
    if (result.error) {
      return { error: result.error, messages: [] };
    }
    removeItem(session, slot.itemId, 1);
    return {
      messages: [
        { type: "sync_inventory", inventory: session.inventory, gold: session.gold },
      ],
    };
  }

  if (def.use === "heal" || def.use === "buff_food") {
    session.entity.hp = Math.min(session.entity.maxHp, session.entity.hp + (def.healHp ?? 0));
    session.entity.mp = Math.min(session.entity.maxMp, session.entity.mp + (def.healMp ?? 0));
    removeItem(session, slot.itemId, 1);
    return {
      messages: [
        {
          type: "sync_vitals",
          entityId: session.entity.id,
          hp: session.entity.hp,
          maxHp: session.entity.maxHp,
          mp: session.entity.mp,
          maxMp: session.entity.maxMp,
          gold: session.gold,
        },
        { type: "sync_inventory", inventory: session.inventory, gold: session.gold },
      ],
    };
  }

  if (def.use === "skill_unlock") {
    removeItem(session, slot.itemId, 1);
    session.gold += 10;
    return {
      messages: [
        { type: "sync_inventory", inventory: session.inventory, gold: session.gold },
        { type: "sync_gold", gold: session.gold },
        {
          type: "sync_chat",
          channel: "server",
          fromId: "system",
          fromName: "System",
          text: "You study the tome. (+10g study stipend)",
          serverTime: now,
        },
      ],
    };
  }

  if (def.use === "class_card" && def.classId && helpers.changeClass) {
    const cardId = slot.itemId;
    const result = helpers.changeClass(def.classId, cardId);
    if (result.error) {
      return { error: result.error, messages: [] };
    }
    removeItem(session, cardId, 1);
    return {
      messages: [
        { type: "sync_inventory", inventory: session.inventory, gold: session.gold },
        {
          type: "sync_chat",
          channel: "server",
          fromId: "system",
          fromName: "System",
          text: `Class changed to ${def.classId}.`,
          serverTime: now,
        },
      ],
    };
  }

  return { error: { type: "error", code: "not_usable", message: "Cannot use that" }, messages: [] };
}
