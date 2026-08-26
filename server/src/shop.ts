import { itemById, shopById, spiritById, weaponById } from "./data.js";
import { skillTreeSnapshot } from "./skills.js";
import { canEquipMainhand, canEquipOffhand, handOf } from "./equipRules.js";
import type { InventorySlot, ItemDef, PlayerSession, ServerMessage, ShopDef } from "./types.js";

const DEFAULT_FOOD_MS = 60_000;

function applyFoodBuff(session: PlayerSession, def: ItemDef, now: number): void {
  const until = now + (def.durationMs ?? DEFAULT_FOOD_MS);
  session.statuses = session.statuses.filter((s) => s.id !== "food_atk" && s.id !== "food_def");
  const atk = def.atkBonus ?? 6;
  const defAmt = def.defBonus ?? 4;
  session.statuses.push({
    id: "food_atk",
    kind: "attr_up",
    until,
    attr: "atk",
    amount: atk,
    atkBonus: atk,
  });
  session.statuses.push({
    id: "food_def",
    kind: "attr_up",
    until,
    attr: "def",
    amount: defAmt,
  });
}

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
  if (spiritById(itemId) && !session.spiritIds.includes(itemId)) {
    session.spiritIds.push(itemId);
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
  const qty = Math.max(1, Math.min(20, Math.floor(quantity)));
  const entry = shop.entries.find((e) => e.itemId === itemId);
  const item = itemById(itemId) ?? weaponById(itemId);
  if (!item && !entry) {
    return { error: { type: "error", code: "bad_item", message: "Shop will not buy that" } };
  }
  const sellPrice = entry?.sellPrice ?? (item && "rarity" in item && item.rarity === "ssr" ? 80 : item && "rarity" in item && item.rarity === "sr" ? 20 : 5);
  if (!removeItem(session, itemId, qty)) {
    return { error: { type: "error", code: "missing_item", message: "You do not have that" } };
  }
  session.gold += sellPrice * qty;
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
  const asWeapon = weaponById(slot.itemId);
  if (!def?.use) {
    if (asWeapon) {
      if (!session.weaponIds.includes(slot.itemId)) {
        session.weaponIds.push(slot.itemId);
      }
      const hand = handOf(slot.itemId);
      if (hand === "offhand") {
        const chk = canEquipOffhand(session.classId, slot.itemId, session.equippedWeaponId);
        if (!chk.ok) {
          return { error: { type: "error", code: chk.code, message: chk.message }, messages: [] };
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
              text: `Offhand: ${asWeapon.name}`,
              serverTime: now,
            },
          ],
        };
      }
      const chk = canEquipMainhand(session.classId, slot.itemId);
      if (!chk.ok) {
        return { error: { type: "error", code: chk.code, message: chk.message }, messages: [] };
      }
      session.equippedWeaponId = slot.itemId;
      return {
        messages: [
          { type: "sync_inventory", inventory: session.inventory, gold: session.gold },
          {
            type: "sync_chat",
            channel: "server",
            fromId: "system",
            fromName: "System",
            text: `Mainhand: ${asWeapon.name}`,
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
        {
          type: "sync_fx",
          kind: "homestone",
          entityId: session.entity.id,
          x: session.entity.x,
          y: session.entity.y,
        },
      ],
    };
  }

  if (def.use === "heal") {
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

  if (def.use === "buff_food") {
    session.entity.hp = Math.min(session.entity.maxHp, session.entity.hp + (def.healHp ?? 0));
    session.entity.mp = Math.min(session.entity.maxMp, session.entity.mp + (def.healMp ?? 0));
    applyFoodBuff(session, def, now);
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
        {
          type: "sync_status",
          entityId: session.entity.id,
          statuses: session.statuses,
          serverTime: now,
        },
        {
          type: "sync_fx",
          kind: "food",
          entityId: session.entity.id,
          x: session.entity.x,
          y: session.entity.y,
        },
        {
          type: "sync_chat",
          channel: "server",
          fromId: "system",
          fromName: "System",
          text: `${def.name}: +${def.atkBonus ?? 0} ATK / +${def.defBonus ?? 0} DEF`,
          serverTime: now,
        },
      ],
    };
  }

  if (def.use === "skill_unlock") {
    removeItem(session, slot.itemId, 1);
    session.skillPoints += 1;
    return {
      messages: [
        { type: "sync_inventory", inventory: session.inventory, gold: session.gold },
        skillTreeSnapshot(session),
        {
          type: "sync_chat",
          channel: "server",
          fromId: "system",
          fromName: "System",
          text: "You study the tome. +1 skill point. Spend it at the Trainer.",
          serverTime: now,
        },
      ],
    };
  }

  if (def.use === "class_card") {
    return {
      error: {
        type: "error",
        code: "use_npc",
        message: "Choose your class at the Class Master in town (level 20).",
      },
      messages: [],
    };
  }

  if (def.use === "skin" || def.use === "portrait") {
    session.equippedSkinId = slot.itemId;
    return {
      messages: [
        {
          type: "sync_inventory",
          inventory: session.inventory,
          gold: session.gold,
          equippedSkinId: session.equippedSkinId,
        },
        {
          type: "sync_chat",
          channel: "server",
          fromId: "system",
          fromName: "System",
          text: `Portrait set: ${def.name}`,
          serverTime: now,
        },
      ],
    };
  }

  return { error: { type: "error", code: "not_usable", message: "Cannot use that" }, messages: [] };
}
