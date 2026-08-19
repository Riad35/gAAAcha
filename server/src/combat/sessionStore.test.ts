import assert from "node:assert/strict";
import { test } from "node:test";
import {
  MemoryCombatSessionStore,
  RedisCombatSessionStore,
  combatSessionKey,
  createEmptyCombatSession,
} from "./sessionStore.js";
import { loadCombatConfig } from "./config.js";

test("combat session key schema is stable", () => {
  assert.equal(combatSessionKey("abc"), "combat:session:abc");
});

test("memory store set/get round-trips and isolates clones", async () => {
  const store = new MemoryCombatSessionStore();
  const session = createEmptyCombatSession("s1", "test_arena");
  session.units.u1 = {
    id: "u1",
    name: "Hero",
    kind: "player",
    mapId: "test_arena",
    x: 3,
    y: 4,
    hitRadius: 0.4,
    element: "earth",
    stats: {
      hp: 100,
      maxHp: 100,
      mp: 50,
      maxMp: 50,
      atk: 10,
      matk: 5,
      def: 3,
      mdef: 2,
      critRate: 0.05,
      critDamage: 1.5,
      hitRate: 0.9,
      dodgeRate: 0.05,
      moveSpeed: 6,
      attackSpeed: 1,
      elementalResist: { fire: 0.1 },
    },
    state: "Idle",
    targetId: null,
    manualLock: false,
    transformGauge: 0,
    transformedUntil: 0,
    statuses: [],
  };
  await store.set(session);

  const loaded = await store.get("s1");
  assert.ok(loaded);
  assert.equal(loaded.mapId, "test_arena");
  assert.equal(loaded.units.u1?.stats.hp, 100);

  loaded.units.u1!.stats.hp = 1;
  const again = await store.get("s1");
  assert.equal(again?.units.u1?.stats.hp, 100);
});

test("memory store expires sessions past expiresAt", async () => {
  const store = new MemoryCombatSessionStore();
  const now = Date.now();
  const session = createEmptyCombatSession("exp", "town", now);
  session.expiresAt = now - 1;
  await store.set(session);
  assert.equal(await store.get("exp"), null);
});

test("touch extends expiresAt", async () => {
  const store = new MemoryCombatSessionStore();
  const now = Date.now();
  const session = createEmptyCombatSession("t1", "town", now);
  await store.set(session);
  const later = now + 60_000;
  await store.touch("t1", later);
  const loaded = await store.get("t1");
  assert.equal(loaded?.expiresAt, later);
});

test("delete removes session", async () => {
  const store = new MemoryCombatSessionStore();
  await store.set(createEmptyCombatSession("d1", "town"));
  await store.delete("d1");
  assert.equal(await store.get("d1"), null);
});

test("redis store scaffolding falls back to memory without client", async () => {
  const store = new RedisCombatSessionStore(undefined);
  await store.set(createEmptyCombatSession("r1", "field_ridge"));
  assert.ok(await store.get("r1"));
  await store.delete("r1");
  assert.equal(await store.get("r1"), null);
});

test("combat-config.json loads with expected tick and element cycle", () => {
  const cfg = loadCombatConfig();
  assert.equal(cfg.tickHz, 12);
  assert.deepEqual(cfg.element.cycle, ["water", "fire", "wind", "earth"]);
  assert.ok(cfg.sessionTtlSec > 0);
});
