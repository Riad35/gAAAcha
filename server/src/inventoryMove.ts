import type { InventorySlot, PlayerSession, ServerMessage } from "./types.js";
import { INVENTORY_SIZE } from "./gacha.js";

function slotAt(inv: InventorySlot[], index: number): InventorySlot | undefined {
  return inv.find((s) => s.slotIndex === index);
}

export function swapInventorySlots(
  session: PlayerSession,
  fromIndex: number,
  toIndex: number,
): ServerMessage | { ok: true } {
  if (
    !Number.isInteger(fromIndex) ||
    !Number.isInteger(toIndex) ||
    fromIndex < 0 ||
    toIndex < 0 ||
    fromIndex >= INVENTORY_SIZE ||
    toIndex >= INVENTORY_SIZE
  ) {
    return { type: "error", code: "bad_slot", message: "Invalid inventory slot" };
  }
  if (fromIndex === toIndex) {
    return { ok: true };
  }
  const a = slotAt(session.inventory, fromIndex);
  const b = slotAt(session.inventory, toIndex);
  if (!a || !b) {
    return { type: "error", code: "bad_slot", message: "Invalid inventory slot" };
  }
  if (a.itemId && b.itemId && a.itemId === b.itemId) {
    b.quantity += a.quantity;
    a.itemId = null;
    a.quantity = 0;
    return { ok: true };
  }
  const id = a.itemId;
  const qty = a.quantity;
  a.itemId = b.itemId;
  a.quantity = b.quantity;
  b.itemId = id;
  b.quantity = qty;
  return { ok: true };
}
