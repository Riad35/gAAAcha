import assert from "node:assert/strict";
import { test } from "node:test";
import { npcs, itemById } from "./data.js";
import { acceptQuest, noteTalk, turnInQuest } from "./quest.js";
import { addItem } from "./shop.js";
import { totalXpToReach } from "./xp.js";
import {
  changeClass,
  debugSetClass,
  debugSetLevel,
  equipGear,
  resetWorld,
  spawnPlayer,
  setTransformed,
} from "./world.js";

test("quest turn-in grants XP and L1-20 stays 3192", () => {
  assert.equal(totalXpToReach(20), 3192);
  resetWorld();
  const p = spawnPlayer("xp_q");
  assert.equal(acceptQuest(p, "q_meet_trainer").error, undefined);
  noteTalk(p, "npc_trainer");
  const before = p.xp + totalXpToReach(p.level);
  const result = turnInQuest(p, "q_meet_trainer");
  assert.equal(result.error, undefined);
  const after = p.xp + totalXpToReach(p.level);
  assert.ok(after - before >= 80);
});

test("debug set level 20 does not auto-pick a class", () => {
  resetWorld();
  const p = spawnPlayer("dbg_lv");
  assert.equal(debugSetLevel(p, 20).error, undefined);
  assert.equal(p.level, 20);
  assert.equal(p.classId, "adventurer");
});

test("class cards no longer change class", async () => {
  const { useInventoryItem } = await import("./shop.js");
  resetWorld();
  const p = spawnPlayer("card_dead");
  p.level = 20;
  addItem(p, "card_fighter", 1);
  const slot = p.inventory.find((s) => s.itemId === "card_fighter");
  assert.ok(slot);
  const used = useInventoryItem(p, slot.slotIndex, Date.now(), {
    teleportHome: () => ({}),
    changeClass: (classId, cardId) => changeClass(p, classId, cardId),
  });
  assert.equal(used.error?.code, "use_npc");
  assert.equal(p.classId, "adventurer");
});

test("class master and specialist NPCs exist with player idle sprites", () => {
  const master = npcs.find((n) => n.id === "npc_class_master");
  const spec = npcs.find((n) => n.id === "npc_specialist");
  assert.equal(master?.interact, "class_change");
  assert.equal(master?.sprite, "player_idle");
  assert.equal(spec?.interact, "subclass");
  assert.equal(spec?.sprite, "player_idle");
});

test("subclass items gate on class, level, and transform lock", () => {
  resetWorld();
  const p = spawnPlayer("spec_t");
  debugSetLevel(p, 30);
  debugSetClass(p, "mage");
  addItem(p, "subclass_fighter_berserker", 1);
  const wrong = equipGear(p, "subclass", "subclass_fighter_berserker");
  assert.equal("type" in wrong && wrong.code, "wrong_class");

  addItem(p, "subclass_mage_fire", 1);
  const ok = equipGear(p, "subclass", "subclass_mage_fire");
  assert.ok("ok" in ok);
  assert.equal(p.equippedSubclassId, "subclass_mage_fire");

  assert.equal(setTransformed(p, true).error, undefined);
  assert.equal(p.transformed, true);
  const blocked = equipGear(p, "subclass", null);
  assert.equal("type" in blocked && blocked.code, "transformed");
  assert.equal(setTransformed(p, false).error, undefined);
  const off = equipGear(p, "subclass", null);
  assert.ok("ok" in off);
  assert.equal(p.equippedSubclassId, null);
});

test("L30 specialist quest rewards subclass items", () => {
  resetWorld();
  const p = spawnPlayer("spec_q");
  debugSetLevel(p, 30);
  debugSetClass(p, "mage");
  assert.ok(itemById("subclass_mage_fire"));
  assert.equal(acceptQuest(p, "q_spec_30").error, undefined);
  noteTalk(p, "npc_specialist");
  assert.equal(turnInQuest(p, "q_spec_30").error, undefined);
  assert.ok(p.inventory.some((s) => s.itemId === "subclass_mage_fire"));
});
