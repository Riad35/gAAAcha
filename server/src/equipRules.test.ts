import assert from "node:assert/strict";
import test from "node:test";
import { canEquipArmor, canEquipMainhand, canEquipOffhand, CLASS_CHANGE_LEVEL } from "./equipRules.js";
import { itemById } from "./data.js";
import { swapInventorySlots } from "./inventoryMove.js";
import { emptyInventory } from "./gacha.js";
import { cancelRest, isResting, startRest } from "./rest.js";
import { applyIncomingDamageMult, validateMove } from "./combat.js";
import { changeClass, debugSetClass, equipOffhand, equipWeapon, resetWorld, spawnPlayer, tickWorld } from "./world.js";
import { skillById } from "./data.js";
import { starterSkillsFor } from "./skills.js";
import type { PlayerSession } from "./types.js";

function session(): PlayerSession {
  resetWorld();
  return spawnPlayer("eq_t");
}

test("class change unlocks at level 20 and is irreversible", () => {
  const p = session();
  p.level = 19;
  assert.equal(changeClass(p, "fighter").error?.code, "level_too_low");
  p.level = CLASS_CHANGE_LEVEL;
  assert.equal(changeClass(p, "fighter").error, undefined);
  assert.equal(p.classId, "fighter");
  assert.equal(changeClass(p, "mage").error?.code, "already_classed");
});

test("warrior cannot equip a staff; archer can equip charm offhand", () => {
  assert.equal(canEquipMainhand("fighter", "staff_arcane").ok, false);
  assert.equal(canEquipMainhand("fighter", "sword_iron").ok, true);
  assert.equal(canEquipMainhand("marksman", "bow_hunter").ok, true);
  assert.equal(canEquipOffhand("marksman", "charm_leaf", "bow_hunter").ok, true);
  assert.equal(canEquipOffhand("fighter", "charm_leaf", "sword_iron").ok, false);
});

test("two-handed mainhand blocks offhand", () => {
  assert.equal(canEquipOffhand("fighter", "sword_off", "sword_zwei").ok, false);
  assert.equal(canEquipOffhand("fighter", "sword_off", "sword_iron").ok, true);
});

test("mage cannot wear medium armor; warrior can wear plate", () => {
  assert.equal(canEquipArmor("mage", itemById("armor_hide")!).ok, false);
  assert.equal(canEquipArmor("mage", itemById("armor_leather")!).ok, true);
  assert.equal(canEquipArmor("fighter", itemById("armor_plate")!).ok, true);
  assert.equal(canEquipArmor("marksman", itemById("armor_iron")!).ok, false);
});

test("adventurer may equip anything", () => {
  assert.equal(canEquipMainhand("adventurer", "staff_arcane").ok, true);
  assert.equal(canEquipOffhand("adventurer", "orb_glass", "staff_arcane").ok, true);
  assert.equal(canEquipArmor("adventurer", itemById("armor_plate")!).ok, true);
});

test("inventory swap moves stacks", () => {
  const p = session();
  p.inventory = emptyInventory();
  p.inventory[0].itemId = "item_ration";
  p.inventory[0].quantity = 2;
  p.inventory[3].itemId = "item_dust";
  p.inventory[3].quantity = 5;
  assert.ok("ok" in swapInventorySlots(p, 0, 3));
  assert.equal(p.inventory[0].itemId, "item_dust");
  assert.equal(p.inventory[3].itemId, "item_ration");
});

test("rest heals until damage or cancel", () => {
  const p = session();
  const now = 1000;
  startRest(p, now);
  assert.equal(isResting(p, now), true);
  p.entity.hp = 50;
  p.entity.maxHp = 100;
  p.entity.hpRegen = 0;
  tickWorld(now);
  assert.equal(p.entity.hp, 52);
  applyIncomingDamageMult(p, 8, now);
  assert.equal(isResting(p, now), false);
  assert.equal(cancelRest(p), false);
});

test("rest cancels when the player walks", () => {
  const p = session();
  const now = 1000;
  startRest(p, now);
  p.entity.mapId = "field_ridge";
  p.entity.x = 6;
  p.entity.y = 10;
  p.lastMoveAt = 0;
  p.moveTimes = [];
  p.actionTimes = [];
  const result = validateMove(p, 7, 10, now);
  assert.equal("ok" in result, true);
  assert.equal(isResting(p, now), false);
});

test("debug class toggle applies warrior kit and locks staff", () => {
  const p = session();
  assert.equal(debugSetClass(p, "fighter").error, undefined);
  assert.equal(p.classId, "fighter");
  assert.ok(p.unlockedSkillIds.includes("cleave"));
  const staff = equipWeapon(p, "staff_arcane");
  assert.equal("ok" in staff, false);
  assert.ok("ok" in equipWeapon(p, "sword_zwei"));
  const off = equipOffhand(p, "sword_off");
  assert.equal("ok" in off, false);
});

test("class AoE skills exist in catalog", () => {
  assert.equal(skillById("cleave")?.targetingType, "GROUND_CIRCLE");
  assert.equal(skillById("arrow_rain")?.targetingType, "GROUND_CIRCLE");
  assert.equal(skillById("arcane_nova")?.targetingType, "GROUND_CIRCLE");
  assert.equal(skillById("knife_fan")?.targetingType, "SKILLSHOT_CONE");
  assert.ok(starterSkillsFor("fighter").includes("auto_attack"));
  assert.ok(starterSkillsFor("fighter").includes("rest"));
});
