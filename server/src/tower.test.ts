/**
 * Character slot + class card + tower gate smoke tests.
 */
import assert from "node:assert/strict";
import { test } from "node:test";
import { listCharSlots, saveCharSlot, deleteCharSlot } from "./chars.js";
import { changeClass, createCharacter, resetWorld, spawnPlayer, bumpTowerFloor } from "./world.js";
import { usePortal } from "./portal.js";
import type { GuestSave } from "./persist.js";
import { emptyInventory } from "./gacha.js";

test("char list returns 8 slots", () => {
  const token = `slot_test_${Date.now()}`;
  for (let i = 0; i < 8; i += 1) {
    deleteCharSlot(token, i);
  }
  const empty = listCharSlots(token);
  assert.equal(empty.length, 8);
  assert.ok(empty.every((s) => s.empty));

  const save: GuestSave = {
    guestToken: token,
    slotIndex: 2,
    classId: "adventurer",
    name: "Tester",
    mapId: "town_ashen",
    x: 6,
    y: 9,
    hp: 100,
    mp: 50,
    inventory: emptyInventory(),
    pity: {},
    charNameSet: true,
    level: 3,
    updatedAt: Date.now(),
  };
  saveCharSlot(save, 2);
  const listed = listCharSlots(token);
  assert.equal(listed[2]?.empty, false);
  assert.equal(listed[2]?.name, "Tester");
  assert.equal(listed.filter((s) => s.empty).length, 7);
  deleteCharSlot(token, 2);
});

test("class pick at L20 seeds weapons and is not a card use", () => {
  resetWorld();
  const p = spawnPlayer("card_t");
  createCharacter(p, "Hero", "adventurer");
  assert.equal(p.classId, "adventurer");
  p.level = 20;
  assert.equal(changeClass(p, "marksman").error, undefined);
  assert.equal(p.classId, "marksman");
  assert.equal(p.equippedWeaponId, "bow_hunter");
  assert.equal(p.equippedWeapon2Id, "charm_leaf");
});

test("tower gate locks until cleared floor + switch", () => {
  resetWorld();
  const p = spawnPlayer("tower_t");
  createCharacter(p, "Climber", "adventurer");
  p.entity.mapId = "tower_f1";
  p.entity.x = 28;
  p.entity.y = 10;
  p.towerClearedFloor = 0;
  p.switchFlags = {};
  let r = usePortal(p, "portal_tower_f1_to_f2");
  assert.equal(r.error?.code, "tower_locked");
  bumpTowerFloor(p, 1);
  r = usePortal(p, "portal_tower_f1_to_f2");
  assert.equal(r.error?.code, "switch_locked");
  p.switchFlags.sw_tower_f1 = true;
  r = usePortal(p, "portal_tower_f1_to_f2");
  assert.ok(r.ok);
  assert.equal(p.entity.mapId, "tower_f2");
});

test("legacy class ids migrate on load", () => {
  resetWorld();
  const p = spawnPlayer("legacy_t", {
    enterWorld: true,
    save: {
      guestToken: "legacy_t",
      classId: "warrior",
      name: "Old",
      mapId: "town_ashen",
      x: 6,
      y: 9,
      hp: 100,
      mp: 30,
      inventory: emptyInventory(),
      pity: {},
      charNameSet: true,
      updatedAt: Date.now(),
    },
  });
  assert.equal(p.classId, "fighter");
});
