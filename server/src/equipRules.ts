import { itemById, weaponById } from "./data.js";
import type { ItemDef, PlayerSession } from "./types.js";

export const CLASS_CHANGE_LEVEL = 20;
export const SPECIALIST_UNLOCK_LEVELS = [30, 40, 50, 60, 70] as const;

export type ArmorWeight = "light" | "medium" | "heavy" | "plate";
export type HandSlot = "mainhand" | "offhand" | "both";
export type Grip = "1h" | "2h";
export type WeaponKind = "sword" | "dagger" | "staff" | "bow" | "gun" | "tome" | "charm" | "orb";

const MAIN_KINDS: Record<string, WeaponKind[]> = {
  adventurer: ["sword", "dagger", "staff", "bow", "gun", "tome"],
  fighter: ["sword"],
  marksman: ["bow"],
  mage: ["staff", "tome"],
  rogue: ["dagger", "sword"],
};

const OFF_KINDS: Record<string, WeaponKind[]> = {
  adventurer: ["sword", "dagger", "charm", "orb"],
  fighter: ["sword"],
  marksman: ["charm"],
  mage: ["orb"],
  rogue: ["dagger"],
};

const ARMOR: Record<string, ArmorWeight[]> = {
  adventurer: ["light", "medium", "heavy", "plate"],
  fighter: ["light", "medium", "heavy", "plate"],
  marksman: ["light", "medium"],
  mage: ["light"],
  rogue: ["light", "medium"],
};

export type EquipFail = { ok: false; code: string; message: string };
export type EquipOk = { ok: true };

function fail(code: string, message: string): EquipFail {
  return { ok: false, code, message };
}

export function classKey(classId: string): string {
  return classId || "adventurer";
}

export function weaponKindOf(itemId: string): WeaponKind | null {
  const item = itemById(itemId);
  if (item?.weaponKind) {
    return item.weaponKind;
  }
  const w = weaponById(itemId);
  if (!w) {
    return null;
  }
  return w.category as WeaponKind;
}

export function handOf(itemId: string): HandSlot {
  const item = itemById(itemId);
  if (item?.hand) {
    return item.hand;
  }
  const w = weaponById(itemId);
  if (w?.hand) {
    return w.hand;
  }
  return "mainhand";
}

export function gripOf(itemId: string): Grip {
  const item = itemById(itemId);
  if (item?.grip) {
    return item.grip;
  }
  const w = weaponById(itemId);
  if (w?.grip) {
    return w.grip;
  }
  return "1h";
}

export function isTwoHanded(itemId: string | null | undefined): boolean {
  if (!itemId) {
    return false;
  }
  return gripOf(itemId) === "2h";
}

export function armorWeightOf(item: ItemDef | undefined): ArmorWeight {
  if (item?.armorWeight) {
    return item.armorWeight;
  }
  const id = item?.id ?? "";
  if (id.includes("plate") || id.includes("_ash")) {
    return "plate";
  }
  if (id.includes("_iron")) {
    return "heavy";
  }
  if (id.includes("hide")) {
    return "medium";
  }
  return "light";
}

export function canEquipMainhand(classId: string, itemId: string): EquipOk | EquipFail {
  const kind = weaponKindOf(itemId);
  if (!kind) {
    return fail("unknown_weapon", `Cannot equip ${itemId}`);
  }
  const hand = handOf(itemId);
  if (hand === "offhand") {
    return fail("wrong_hand", "That item is offhand only");
  }
  if (classKey(classId) === "adventurer") {
    return { ok: true };
  }
  const allowed = MAIN_KINDS[classKey(classId)] ?? [];
  if (!allowed.includes(kind)) {
    return fail("class_locked", `${kind} cannot be a mainhand for this class`);
  }
  if (classKey(classId) === "rogue" && kind === "sword" && isTwoHanded(itemId)) {
    return fail("class_locked", "Rogue cannot wield a two-handed sword");
  }
  return { ok: true };
}

export function canEquipOffhand(
  classId: string,
  itemId: string,
  mainhandId: string | null,
): EquipOk | EquipFail {
  const kind = weaponKindOf(itemId);
  if (!kind) {
    return fail("unknown_weapon", `Cannot equip ${itemId}`);
  }
  const hand = handOf(itemId);
  if (hand === "mainhand") {
    return fail("wrong_hand", "That item is mainhand only");
  }
  if (isTwoHanded(mainhandId)) {
    return fail("two_handed", "Two-handed mainhand blocks the offhand");
  }
  if (isTwoHanded(itemId)) {
    return fail("wrong_hand", "Two-handed weapons cannot go in offhand");
  }
  if (classKey(classId) === "adventurer") {
    return { ok: true };
  }
  const allowed = OFF_KINDS[classKey(classId)] ?? [];
  if (!allowed.includes(kind)) {
    return fail("class_locked", `${kind} cannot be an offhand for this class`);
  }
  return { ok: true };
}

export function canEquipArmor(classId: string, item: ItemDef): EquipOk | EquipFail {
  const weight = armorWeightOf(item);
  if (classKey(classId) === "adventurer") {
    return { ok: true };
  }
  const allowed = ARMOR[classKey(classId)] ?? ["light"];
  if (!allowed.includes(weight)) {
    return fail("class_locked", `${weight} armor is not allowed for this class`);
  }
  return { ok: true };
}

export function stripInvalidGear(session: PlayerSession): void {
  if (session.equippedWeaponId) {
    const chk = canEquipMainhand(session.classId, session.equippedWeaponId);
    if (!chk.ok) {
      session.equippedWeaponId = "";
    }
  }
  if (session.equippedWeapon2Id) {
    const chk = canEquipOffhand(session.classId, session.equippedWeapon2Id, session.equippedWeaponId);
    if (!chk.ok) {
      session.equippedWeapon2Id = null;
    }
  }
  const slots: Array<{
    field: "equippedArmorId" | "equippedHelmId" | "equippedBootsId" | "equippedGlovesId" | "equippedAmuletId" | "equippedRing1Id" | "equippedRing2Id";
  }> = [
    { field: "equippedArmorId" },
    { field: "equippedHelmId" },
    { field: "equippedBootsId" },
    { field: "equippedGlovesId" },
    { field: "equippedAmuletId" },
    { field: "equippedRing1Id" },
    { field: "equippedRing2Id" },
  ];
  for (const { field } of slots) {
    const id = session[field];
    if (!id) {
      continue;
    }
    const item = itemById(id);
    if (!item || !canEquipArmor(session.classId, item).ok) {
      session[field] = null;
    }
  }
}
