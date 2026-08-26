import assert from "node:assert/strict";
import { test } from "node:test";
import { notePlayerDamageThreat } from "./world.js";
import { acceptQuest, noteKill, noteTalk, turnInQuest } from "./quest.js";
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
  assert.equal(acceptQuest(p, "q_meet_trainer").error, undefined);
  noteTalk(p, "npc_trainer");
  assert.equal(turnInQuest(p, "q_meet_trainer").error, undefined);
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

test("main chain locks pests until trainer is done; crypt then marsh; class path needs L20", () => {
  resetWorld();
  const p = spawnPlayer("chain_t");
  const locked = acceptQuest(p, "q_clear_pests");
  assert.equal(locked.error?.code, "quest_locked");
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
  let delve = p.quests.find((x) => x.questId === "q_delve_depths");
  assert.equal(delve?.stepIndex, 1);
  assert.equal(delve?.completed, false);
  noteKill(p, "marsh");
  noteKill(p, "marsh");
  delve = p.quests.find((x) => x.questId === "q_delve_depths");
  assert.ok(delve?.completed);
  assert.equal(turnInQuest(p, "q_delve_depths").error, undefined);
  const classEarly = acceptQuest(p, "q_class_path");
  assert.equal(classEarly.error?.code, "quest_locked");
  p.completedQuestIds.push("q_tower_f2", "q_tower_f5");
  p.level = 19;
  const low = acceptQuest(p, "q_class_path");
  assert.equal(low.error?.code, "level_too_low");
  p.level = 20;
  assert.equal(acceptQuest(p, "q_class_path").error, undefined);
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
  p.unlockedSkillIds = starterSkillsFor("adventurer");
  p.classId = "adventurer";
  p.level = 5;
  p.skillPoints = 2;
  const unlockable = (await import("./skills.js")).unlockableSkills(p);
  assert.ok(unlockable.includes("dash"));
  assert.ok(!unlockable.includes("decoy"));
  assert.ok(unlockSkill(p, "dash").ok);
  assert.equal(p.skillPoints, 1);
  const early = unlockSkill(p, "decoy");
  assert.equal(early.error?.code, "level_too_low");

  const tree = (await import("./skills.js")).skillTreeSnapshot(p);
  assert.equal(tree.type, "sync_skills");
  if (tree.type === "sync_skills") {
    const shot = tree.catalog?.find((c) => c.id === "shot");
    assert.ok(shot);
    assert.equal(shot?.name, "Shot");
    assert.equal(shot?.manaCost, 8);
    assert.equal(shot?.weaponSlot, 1);
    assert.equal(shot?.targetingType, "SKILLSHOT_LINEAR");
    assert.equal(shot?.range, 5);
    const aa = tree.catalog?.find((c) => c.id === "auto_attack");
    assert.equal(aa?.weaponSlot, 1);
    assert.ok(tree.classSkillIds?.includes("hook_shot"));
  }

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

test("hearty stew heals and applies timed ATK/DEF food buff", async () => {
  resetWorld();
  const { useInventoryItem } = await import("./shop.js");
  const { addItem } = await import("./shop.js");
  const p = spawnPlayer("stew_t");
  p.entity.hp = 20;
  addItem(p, "item_stew", 1);
  const slot = p.inventory.find((s) => s.itemId === "item_stew");
  assert.ok(slot);
  const now = Date.now();
  const used = useInventoryItem(p, slot.slotIndex, now, { teleportHome: () => ({}) });
  assert.equal(used.error, undefined);
  assert.ok(p.entity.hp > 20);
  const atk = p.statuses.find((s) => s.id === "food_atk");
  const def = p.statuses.find((s) => s.id === "food_def");
  assert.equal(atk?.kind, "attr_up");
  assert.equal(atk?.attr, "atk");
  assert.equal(atk?.amount, 6);
  assert.equal(def?.attr, "def");
  assert.equal(def?.amount, 4);
  assert.ok((atk?.until ?? 0) > now + 50_000);
  assert.ok(!p.inventory.some((s) => s.itemId === "item_stew" && s.quantity > 0));
  addItem(p, "item_stew", 1);
  const slot2 = p.inventory.find((s) => s.itemId === "item_stew");
  assert.ok(slot2);
  useInventoryItem(p, slot2.slotIndex, now + 1000, { teleportHome: () => ({}) });
  assert.equal(p.statuses.filter((s) => s.id === "food_atk").length, 1);
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

test("gear ladder iron needs L8 and ash needs L15; starter bag stays leather-free", async () => {
  resetWorld();
  const { equipGear } = await import("./world.js");
  const { addItem } = await import("./shop.js");
  const { STARTER_BAG } = await import("./gacha.js");
  const { itemById } = await import("./data.js");
  assert.equal(itemById("armor_iron")?.levelReq, 8);
  assert.equal(itemById("armor_ash")?.levelReq, 15);
  assert.ok(!STARTER_BAG.some((s) => s.id.includes("armor_") || s.id.includes("helm_") || s.id.includes("boots_") || s.id.includes("gloves_") || s.id.startsWith("acc_")));
  const p = spawnPlayer("ladder_t");
  addItem(p, "armor_iron", 1);
  const denied = equipGear(p, "armor", "armor_iron");
  assert.equal("type" in denied && denied.type === "error" && denied.code, "level_too_low");
  assert.equal(p.equippedArmorId, null);
  assert.ok(p.inventory.some((s) => s.itemId === "armor_iron" && s.quantity === 1));
  p.level = 8;
  const okIron = equipGear(p, "armor", "armor_iron");
  assert.ok("ok" in okIron);
  assert.equal(p.equippedArmorId, "armor_iron");
  addItem(p, "helm_ash", 1);
  const deniedAsh = equipGear(p, "helm", "helm_ash");
  assert.equal("type" in deniedAsh && deniedAsh.type === "error" && deniedAsh.code, "level_too_low");
  p.level = 15;
  const okAsh = equipGear(p, "helm", "helm_ash");
  assert.ok("ok" in okAsh);
  assert.equal(p.equippedHelmId, "helm_ash");
});
