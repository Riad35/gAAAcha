/**
 * Smoke the playable loop: create → quests → kill → pull → L20 class pick.
 * Lists remaining product holes in the last assertion comments.
 */
import assert from "node:assert/strict";
import { test } from "node:test";
import { mapById, shopById } from "./data.js";
import { pullGacha } from "./gacha.js";
import { acceptQuest, noteKill, noteTalk, turnInQuest } from "./quest.js";
import { addItem, buyFromShop, useInventoryItem } from "./shop.js";
import { unlockSkill } from "./skills.js";
import { grantXp, totalXpToReach } from "./xp.js";
import { changeClass, createCharacter, resetWorld, spawnPlayer } from "./world.js";

test("loop: create, main quests, shop, pull, tome SP, L20 class pick", () => {
  resetWorld();
  const p = spawnPlayer("loop_t");
  createCharacter(p, "Looper", "adventurer");
  assert.equal(p.classId, "adventurer");
  assert.equal(p.level, 1);
  assert.ok(p.inventory.some((s) => s.itemId === "sword_iron"));
  assert.ok(p.inventory.some((s) => s.itemId === "bow_hunter"));

  assert.equal(acceptQuest(p, "q_meet_trainer").error, undefined);
  noteTalk(p, "npc_trainer");
  assert.equal(turnInQuest(p, "q_meet_trainer").error, undefined);

  assert.equal(acceptQuest(p, "q_clear_pests").error, undefined);
  noteKill(p, "pest");
  noteKill(p, "pest");
  noteKill(p, "pest");
  assert.equal(turnInQuest(p, "q_clear_pests").error, undefined);

  assert.equal(acceptQuest(p, "q_delve_depths").error, undefined);
  noteKill(p, "crypt");
  noteKill(p, "crypt");
  noteKill(p, "marsh");
  noteKill(p, "marsh");
  assert.equal(turnInQuest(p, "q_delve_depths").error, undefined);

  p.gold = 200;
  const town2 = shopById("shop_town2");
  assert.ok(town2?.entries.some((e) => e.itemId === "item_ticket"));
  const town3 = shopById("shop_town3");
  assert.ok(town3?.entries.some((e) => e.itemId === "item_skill_tome"));
  assert.equal(buyFromShop(p, "shop_cook", "item_stew", 1).error, undefined);

  addItem(p, "item_dust", 20);
  const pull = pullGacha(p, "starter", 1, () => 0.5);
  assert.ok("ok" in pull);
  assert.ok(pull.results.length === 1);

  addItem(p, "item_skill_tome", 1);
  const beforeSp = p.skillPoints;
  const tomeSlot = p.inventory.find((s) => s.itemId === "item_skill_tome");
  assert.ok(tomeSlot);
  const tome = useInventoryItem(p, tomeSlot.slotIndex, Date.now(), { teleportHome: () => ({}) });
  assert.equal(tome.error, undefined);
  assert.equal(p.skillPoints, beforeSp + 1);
  grantXp(p, totalXpToReach(3));
  assert.ok(p.level >= 3);
  assert.ok(unlockSkill(p, "shockwave").ok);

  grantXp(p, totalXpToReach(20));
  assert.ok(p.level >= 20);

  p.completedQuestIds.push("q_tower_f2", "q_tower_f5");
  assert.equal(acceptQuest(p, "q_class_path").error, undefined);
  noteTalk(p, "npc_class_master");
  assert.equal(turnInQuest(p, "q_class_path").error, undefined);

  assert.equal(changeClass(p, "fighter").error, undefined);
  assert.equal(p.classId, "fighter");
  assert.equal(changeClass(p, "mage").error?.code, "already_classed");

  const f1 = mapById("tower_f1");
  assert.ok((f1?.blocked.length ?? 0) >= 20);

  const town = mapById("town_ashen");
  assert.ok(town?.props?.some((p) => p.kind === "fountain" && p.x === 9 && p.y === 9));
  assert.ok(town?.blocked.some((t) => t.x === 9 && t.y === 9));
  assert.ok(town?.blocked.some((t) => t.x === 4 && t.y === 6));
  const ridge = mapById("field_ridge");
  assert.ok(ridge?.props?.some((p) => p.kind === "rock"));
  assert.ok(ridge?.props?.some((p) => p.kind === "gate" && p.x === 3));
  const crypt = mapById("dungeon_crypt");
  assert.ok(crypt?.props?.some((p) => p.kind === "chest"));
  const f2 = mapById("tower_f2");
  assert.ok(f2?.props?.some((p) => p.kind === "pillar"));
  const rest = mapById("tower_town_2");
  assert.ok(rest?.props?.some((p) => p.kind === "fountain"));
  const boss = mapById("tower_boss_f2");
  assert.ok(boss?.props?.some((p) => p.kind === "pillar"));
});
