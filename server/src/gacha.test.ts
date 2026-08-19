import assert from "node:assert/strict";
import { test } from "node:test";
import { defaultBanner } from "./data.js";
import {
  canFitDrops,
  emptyInventory,
  grantDrop,
  pityView,
  pullGacha,
  ssrChance,
} from "./gacha.js";
import type { PlayerSession } from "./types.js";

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
    spiritIds: ["spirit_ember"],
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
    towerClearedFloor: 0,
    switchFlags: {},
    inWorld: true,
  };
}

function rngAlways(value: number): () => number {
  return () => value;
}

test("ssr chance is base before soft pity and 100% at hard pity", () => {
  assert.equal(ssrChance(defaultBanner, 0), 0.02);
  assert.equal(ssrChance(defaultBanner, 48), 0.02);
  assert.equal(ssrChance(defaultBanner, 49), 0.04);
  assert.equal(ssrChance(defaultBanner, 79), 1);
});

test("pity view exposes the next pull chance", () => {
  const view = pityView(defaultBanner, { bannerId: "starter", pity: 49, totalPulls: 49 });
  assert.equal(view.count, 49);
  assert.equal(view.hardPity, 80);
  assert.equal(view.nextSsrChance, 0.04);
});

test("hard pity forces SSR on pull 80 and resets the counter", () => {
  const player = session();
  let last;
  for (let i = 0; i < 80; i += 1) {
    last = pullGacha(player, "starter", 1, rngAlways(0.99));
  }
  assert.ok(last && "ok" in last);
  assert.equal(last.results[0].rarity, "ssr");
  assert.equal(last.pity.count, 0);
  assert.equal(player.pity.starter.totalPulls, 80);
});

test("a natural SSR resets pity immediately", () => {
  const result = pullGacha(session(), "starter", 1, rngAlways(0));
  assert.ok("ok" in result);
  assert.equal(result.results[0].rarity, "ssr");
  assert.equal(result.pity.count, 0);
});

test("ten-pull returns 10 drops and floors an all-R batch to one SR", () => {
  const result = pullGacha(session(), "starter", 10, rngAlways(0.99));
  assert.ok("ok" in result);
  assert.equal(result.results.length, 10);
  assert.equal(result.results.filter((drop) => drop.rarity === "sr").length, 1);
  assert.equal(result.results.at(-1)?.rarity, "sr");
});

test("full inventory rejects the pull and does not spend pity", () => {
  const player = session();
  for (const slot of player.inventory) {
    slot.itemId = `filler_${slot.slotIndex}`;
    slot.quantity = 1;
  }
  const result = pullGacha(player, "starter", 1, rngAlways(0));
  assert.deepEqual(result, { type: "error", code: "inventory_full", message: "Not enough inventory slots" });
  assert.equal(player.pity.starter.pity, 0);
  assert.equal(player.pity.starter.totalPulls, 0);
});

test("unknown banner and bad count are rejected", () => {
  const player = session();
  const unknown = pullGacha(player, "missing", 1, rngAlways(0));
  const badCount = pullGacha(player, "starter", 3, rngAlways(0));
  assert.equal("type" in unknown && unknown.code, "unknown_banner");
  assert.equal("type" in badCount && badCount.code, "invalid_pull");
});

test("drops stack on the same item id", () => {
  const slots = emptyInventory();
  assert.equal(grantDrop(slots, "item_dust"), true);
  assert.equal(grantDrop(slots, "item_dust"), true);
  assert.equal(slots[0].quantity, 2);
  assert.equal(canFitDrops(slots, [{ itemId: "item_dust", rarity: "r" }]), true);
});
