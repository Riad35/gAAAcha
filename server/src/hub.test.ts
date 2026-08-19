import assert from "node:assert/strict";
import { test } from "node:test";
import { notePlayerDamageThreat } from "./world.js";
import { acceptQuest, noteKill, turnInQuest } from "./quest.js";
import { buyFromShop } from "./shop.js";
import { usePortal, setHomestone, teleportHome } from "./portal.js";
import { liveMonsters, resetWorld, spawnPlayer, tickWorld } from "./world.js";

test("portal transfers player between town and field", () => {
  resetWorld();
  const p = spawnPlayer("portal_t");
  p.entity.mapId = "town_ashen";
  p.entity.x = 22;
  p.entity.y = 9;
  const ok = usePortal(p, "portal_town_to_field");
  assert.ok(ok.ok);
  assert.equal(p.entity.mapId, "field_ridge");
  assert.equal(p.entity.x, 3);
});

test("shop buy spends gold", () => {
  resetWorld();
  const p = spawnPlayer("shop_t");
  p.gold = 100;
  const before = p.gold;
  const result = buyFromShop(p, "shop_cook", "item_stew", 1);
  assert.equal(result.error, undefined);
  assert.ok(p.gold < before);
  assert.ok(p.inventory.some((s) => s.itemId === "item_stew"));
});

test("quest kill step advances", () => {
  resetWorld();
  const p = spawnPlayer("quest_t");
  assert.equal(acceptQuest(p, "q_clear_pests").error, undefined);
  noteKill(p, "pest");
  noteKill(p, "pest");
  noteKill(p, "pest");
  const q = p.quests.find((x) => x.questId === "q_clear_pests");
  assert.ok(q?.completed);
  const turn = turnInQuest(p, "q_clear_pests");
  assert.equal(turn.error, undefined);
  assert.ok(p.completedQuestIds.includes("q_clear_pests"));
});

test("neutral pest does not aggro until damaged", () => {
  resetWorld();
  const p = spawnPlayer("neu_t");
  p.entity.mapId = "field_ridge";
  p.entity.x = 10;
  p.entity.y = 12;
  const before = p.entity.hp;
  const idle = tickWorld(Date.now());
  assert.equal(idle.some((m) => m.type === "sync_skill" && m.casterId === "monster_pest_1"), false);
  assert.equal(p.entity.hp, before);
  notePlayerDamageThreat(p.entity.id, "monster_pest_1", 20, Date.now());
  const fight = tickWorld(Date.now() + 50);
  assert.ok(
    fight.some((m) => m.type === "sync_skill" && m.casterId === "monster_pest_1") ||
      fight.some((m) => m.type === "sync_threat" && m.monsterId === "monster_pest_1"),
  );
});

test("homestone set and teleport", () => {
  resetWorld();
  const p = spawnPlayer("home_t");
  p.entity.mapId = "field_ridge";
  p.entity.x = 8;
  p.entity.y = 8;
  setHomestone(p);
  assert.equal(p.homeMapId, "field_ridge");
  p.entity.mapId = "town_ashen";
  p.entity.x = 6;
  p.entity.y = 9;
  const result = teleportHome(p, Date.now(), false);
  assert.ok(result.ok);
  assert.equal(p.entity.mapId, "field_ridge");
  assert.equal(p.entity.x, 8);
});

test("crypt portal opens private dungeon instance", () => {
  resetWorld();
  const p = spawnPlayer("crypt_t");
  p.entity.mapId = "field_ridge";
  p.entity.x = 34;
  p.entity.y = 10;
  const ok = usePortal(p, "portal_field_to_crypt", Date.now());
  assert.ok(ok.ok);
  assert.ok(p.entity.mapId.startsWith("dungeon_crypt#"));
  const monsters = [...liveMonsters.values()].filter((m) => m.mapId === p.entity.mapId);
  assert.ok(monsters.length >= 1);
});

test("marsh and ruins portals + instance", () => {
  resetWorld();
  const p = spawnPlayer("marsh_t");
  p.entity.mapId = "town_ashen";
  p.entity.x = 4;
  p.entity.y = 16;
  assert.ok(usePortal(p, "portal_town_to_marsh", Date.now()).ok);
  assert.equal(p.entity.mapId, "field_marsh");
  p.entity.x = 28;
  p.entity.y = 11;
  assert.ok(usePortal(p, "portal_marsh_to_ruins", Date.now()).ok);
  assert.ok(p.entity.mapId.startsWith("dungeon_ruins#"));
});

test("friends add and trade invite flow", async () => {
  resetWorld();
  const { addFriend, friendsSnapshot } = await import("./friends.js");
  const { inviteTrade, respondTradeInvite, updateTradeOffer, confirmTrade } = await import("./trade.js");
  const a = spawnPlayer("tr_a");
  const b = spawnPlayer("tr_b");
  a.entity.mapId = "town_ashen";
  b.entity.mapId = "town_ashen";
  a.entity.x = 6;
  a.entity.y = 9;
  b.entity.x = 6.5;
  b.entity.y = 9;
  assert.ok(addFriend(a, b.entity.id).ok);
  assert.equal(friendsSnapshot(a).type, "sync_friends");
  const inv = inviteTrade(a, b.entity.id, Date.now());
  assert.ok(inv.toMsg);
  const opened = respondTradeInvite(b, (inv.toMsg as { inviteId: string }).inviteId, true, Date.now());
  assert.equal(opened.error, undefined);
  a.gold = 50;
  const offer = updateTradeOffer(a, 10, []);
  assert.equal(offer.error, undefined);
  confirmTrade(a);
  const done = confirmTrade(b);
  assert.ok(done.done);
  assert.equal(b.gold, 110);
});

test("skill unlock spends points; auction list/buy", async () => {
  resetWorld();
  const { unlockSkill, starterSkillsFor } = await import("./skills.js");
  const { listAuctionItem, buyAuction, auctionSnapshot } = await import("./auction.js");
  const { addItem } = await import("./shop.js");
  const p = spawnPlayer("sk_t");
  p.unlockedSkillIds = starterSkillsFor("fighter");
  p.classId = "fighter";
  p.skillPoints = 2;
  const unlockable = (await import("./skills.js")).unlockableSkills(p);
  assert.ok(unlockable.length > 0);
  assert.ok(unlockSkill(p, unlockable[0]).ok);
  assert.equal(p.skillPoints, 1);

  const seller = spawnPlayer("auc_s");
  const buyer = spawnPlayer("auc_b");
  addItem(seller, "item_dust", 3);
  seller.gold = 0;
  buyer.gold = 100;
  assert.ok(listAuctionItem(seller, "item_dust", 2, 25).msg);
  const snap = auctionSnapshot();
  assert.equal(snap.type, "sync_auction");
  const id = (snap as { listings: { id: string }[] }).listings[0]?.id;
  assert.ok(id);
  const bought = buyAuction(buyer, id);
  assert.equal(bought.error, undefined);
  assert.equal(buyer.gold, 75);
  assert.equal(seller.gold, 25);
});

test("equip armor raises def and grantXp levels up", async () => {
  resetWorld();
  const { equipGear, applyGearStats } = await import("./world.js");
  const { grantXp, xpToNextLevel } = await import("./xp.js");
  const { addItem } = await import("./shop.js");
  const p = spawnPlayer("gear_xp_t");
  const beforeDef = p.entity.def;
  addItem(p, "armor_leather", 1);
  const eq = equipGear(p, "armor", "armor_leather");
  assert.ok("ok" in eq);
  assert.equal(p.equippedArmorId, "armor_leather");
  assert.ok(p.entity.def > beforeDef);
  const need = xpToNextLevel(p.level);
  const msgs = grantXp(p, need, applyGearStats);
  assert.equal(p.level, 2);
  assert.ok(msgs.some((m) => m.type === "sync_xp" && m.level === 2));
});
