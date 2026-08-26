import assert from "node:assert/strict";
import { test } from "node:test";
import { validateCast, validateMove } from "./combat.js";
import { sessionCond } from "./cond.js";
import { itemById } from "./data.js";
import {
  beginNpcTalk,
  closeNpcTalk,
  enhanceCost,
  enhanceGear,
  enhanceLevelOf,
  NPC_TALK_RANGE,
} from "./enhance.js";
import { addItem } from "./shop.js";
import { sellToShop } from "./shop.js";
import { equipGear, resetWorld, spawnPlayer, applyGearStats } from "./world.js";

test("talking freezes move and act until dialog close", () => {
  resetWorld();
  const p = spawnPlayer("talk_fr");
  const now = 1_000;
  assert.equal(sessionCond(p, now).canMove, true);
  beginNpcTalk(p, "npc_weapon");
  assert.equal(sessionCond(p, now).canMove, false);
  assert.equal(sessionCond(p, now).canAct, false);
  const move = validateMove(p, p.entity.x + 0.2, p.entity.y, now);
  assert.equal("type" in move && move.code, "talking");
  p.unlockedSkillIds = [];
  const cast = validateCast(p, "dash", p.entity.id, now);
  assert.equal("type" in cast && cast.code, "talking");
  closeNpcTalk(p);
  assert.equal(sessionCond(p, now).canMove, true);
  assert.equal(sessionCond(p, now).canAct, true);
});

test("talk range constant matches client 2.2", () => {
  assert.equal(NPC_TALK_RANGE, 2.2);
});

test("jewelry slots: amulet, ring A, ring B", () => {
  assert.equal(itemById("acc_ring")?.slot, "ring1");
  assert.equal(itemById("acc_amulet")?.slot, "amulet");
  assert.equal(itemById("acc_ring2")?.slot, "ring2");
  resetWorld();
  const p = spawnPlayer("jew");
  addItem(p, "acc_amulet", 1);
  addItem(p, "acc_ring", 1);
  addItem(p, "acc_ring2", 1);
  assert.ok("ok" in equipGear(p, "amulet", "acc_amulet"));
  assert.equal(p.equippedAmuletId, "acc_amulet");
  assert.ok("ok" in equipGear(p, "ring1", "acc_ring"));
  assert.equal(p.equippedRing1Id, "acc_ring");
  assert.ok("ok" in equipGear(p, "ring2", "acc_ring2"));
  assert.equal(p.equippedRing2Id, "acc_ring2");
  addItem(p, "acc_ring", 1);
  const wrong = equipGear(p, "amulet", "acc_ring");
  assert.equal("type" in wrong && wrong.code, "bad_gear");
});

test("legacy accessory slot name maps to amulet", () => {
  resetWorld();
  const p = spawnPlayer("acc_alias");
  addItem(p, "acc_amulet", 1);
  assert.ok("ok" in equipGear(p, "accessory", "acc_amulet"));
  assert.equal(p.equippedAmuletId, "acc_amulet");
});

test("enhance spends gold and dust up to +5", () => {
  resetWorld();
  const p = spawnPlayer("enh");
  p.gold = 5000;
  addItem(p, "item_dust", 40);
  addItem(p, "armor_leather", 1);
  const eq = equipGear(p, "armor", "armor_leather");
  assert.ok("ok" in eq);
  const beforeDef = p.entity.def;
  const first = enhanceGear(p, "armor");
  assert.ok("ok" in first);
  applyGearStats(p);
  assert.equal(enhanceLevelOf(p, "armor"), 1);
  assert.ok(p.entity.def > beforeDef);
  const cost1 = enhanceCost(0);
  assert.equal(cost1.gold, 50);
  assert.equal(cost1.dust, 2);
  for (let i = 0; i < 4; i += 1) {
    const r = enhanceGear(p, "armor");
    assert.ok("ok" in r, `enhance ${i + 2}`);
  }
  const capped = enhanceGear(p, "armor");
  assert.equal("type" in capped && capped.code, "enhance_max");
});

test("vendor buys loot not on the shop list", () => {
  resetWorld();
  const p = spawnPlayer("sell_any");
  addItem(p, "item_dust", 3);
  const gold = p.gold;
  const sold = sellToShop(p, "shop_weapon", "item_dust", 1);
  assert.equal(sold.error, undefined);
  assert.ok(p.gold > gold);
});
