import assert from "node:assert/strict";
import { test } from "node:test";
import { killXpFor, lootTableFor, rollKillRewards } from "./loot.js";
import { grantXp, totalXpToReach, xpToNextLevel } from "./xp.js";
import { emptyInventory } from "./gacha.js";
import type { PlayerSession } from "./types.js";

function rngAlways(value: number): () => number {
  return () => value;
}

function session(): PlayerSession {
  return {
    entity: {
      id: "p1",
      kind: "player",
      name: "Wanderer",
      x: 3,
      y: 10,
      hp: 100,
      maxHp: 100,
      mp: 50,
      maxMp: 50,
      atk: 12,
      magicAtk: 8,
      def: 4,
      magicResist: 2,
      attackSpeed: 1,
      hpRegen: 1,
      mpRegen: 1,
      critChance: 0.05,
      critDamage: 1.5,
      moveSpeed: 6,
      hitRadius: 0.4,
      resist: { wind: 0, fire: 0, water: 0, earth: 0, holy: 0, dark: 0 },
      mapId: "graybox_01",
    },
    classId: "adventurer",
    guestToken: "test",
    characterId: undefined,
    slotIndex: 0,
    lastActionAt: 0,
    lastMoveAt: 0,
    facingX: 1,
    facingY: 0,
    actionTimes: [],
    skillReadyAt: {},
    inventory: emptyInventory(),
    pity: {},
    statuses: [],
    weaponIds: ["sword_iron"],
    equippedWeaponId: "sword_iron",
    equippedWeapon2Id: null,
    spiritIds: [],
    equippedSpiritId: null,
    moveLockUntil: 0,
    moveTimes: [],
    partyId: null,
    guildId: null,
    gold: 100,
    homeMapId: "town_ashen",
    homeX: 6,
    homeY: 9,
    quests: [],
    completedQuestIds: [],
    charNameSet: true,
    homestoneReadyAt: 0,
    unlockedSkillIds: [],
    skillPoints: 0,
    level: 1,
    xp: 0,
    equippedArmorId: null,
    equippedHelmId: null,
    equippedBootsId: null,
    equippedGlovesId: null,
    equippedAccessoryId: null,
    friends: [],
    classCardId: null,
    equippedSkinId: null,
    towerClearedFloor: 0,
    switchFlags: {},
    inWorld: true,
  };
}

test("slime always drops dust; pest does not at high rolls", () => {
  const slime = rollKillRewards(lootTableFor("slime"), rngAlways(0));
  assert.ok(slime.items.some((item) => item.itemId === "item_dust"));
  const pest = rollKillRewards(lootTableFor("pest"), rngAlways(0.99));
  assert.equal(pest.items.some((item) => item.itemId === "item_dust"), false);
  assert.ok(pest.gold >= lootTableFor("pest").goldMin);
});

test("tower XP outpaces field trash and L20 is a session-chain budget", () => {
  assert.ok(killXpFor("tower_boss_f5") > killXpFor("slime") * 10);
  assert.ok(killXpFor("tower_rat") > killXpFor("slime"));
  assert.equal(xpToNextLevel(1), 42);
  assert.equal(totalXpToReach(20), 3192);
  const player = session();
  grantXp(player, totalXpToReach(20));
  assert.equal(player.level, 20);
});

test("crypt can drop iron helm on the gear ladder", () => {
  const loot = rollKillRewards(lootTableFor("crypt"), rngAlways(0));
  assert.ok(loot.items.some((item) => item.itemId === "helm_iron"));
  const elite = lootTableFor("tower_elite");
  assert.ok(elite.drops.some((d) => d.itemId === "armor_ash"));
});

test("ragdoll lab dummies are not a farm", () => {
  const table = lootTableFor("ragdoll");
  assert.equal(table.xp, 4);
  assert.equal(table.drops.length, 0);
});
