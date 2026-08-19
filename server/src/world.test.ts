import assert from "node:assert/strict";
import { test } from "node:test";
import { bindCombatWorld, validateCast } from "./combat.js";
import {
  buildInspect,
  findEntity,
  grantKillLoot,
  killMonster,
  liveMonsters,
  monsterStatuses,
  notePlayerDamageThreat,
  players,
  resetWorld,
  spawnPlayer,
  tickWorld,
} from "./world.js";

bindCombatWorld(
  findEntity,
  () => [...players.values()],
  (id, status) => {
    const list = monsterStatuses.get(id) ?? [];
    monsterStatuses.set(id, [...list.filter((s) => s.id !== status.id), status]);
  },
  () => [...liveMonsters.values()].filter((monster) => monster.hp > 0),
);

test("killing a monster despawns and respawns after delay", () => {
  resetWorld();
  const now = Date.now();
  const slime = liveMonsters.get("monster_slime_1");
  assert.ok(slime);
  slime.hp = 0;
  const death = killMonster("monster_slime_1", now);
  assert.equal(death[0]?.type, "sync_despawn");
  assert.equal(liveMonsters.has("monster_slime_1"), false);

  const early = tickWorld(now + 1000);
  assert.equal(early.some((m) => m.type === "sync_spawn"), false);

  const later = tickWorld(now + 9000);
  assert.ok(later.some((m) => m.type === "sync_spawn"));
  assert.ok(liveMonsters.get("monster_slime_1")?.hp === 40);
});

test("monster in aggro range hits the player", () => {
  resetWorld();
  const player = spawnPlayer("tick_test");
  player.entity.mapId = "field_ridge";
  player.entity.x = 6;
  player.entity.y = 10;
  const before = player.entity.hp;
  const msgs = tickWorld(Date.now() + 100);
  assert.ok(msgs.some((m) => m.type === "sync_skill" && m.casterId === "monster_slime_1"));
  assert.ok(player.entity.hp < before);
});

test("dead targets cannot be cast on", () => {
  resetWorld();
  const player = spawnPlayer("dead_cast");
  player.entity.mapId = "field_ridge";
  player.entity.x = 6;
  player.entity.y = 10;
  const slime = liveMonsters.get("monster_slime_1");
  assert.ok(slime);
  slime.hp = 0;
  liveMonsters.delete("monster_slime_1");
  const result = validateCast(player, "slash", "monster_slime_1", Date.now());
  assert.equal("type" in result && result.code, "invalid_target");
});

test("kill loot grants star dust", () => {
  resetWorld();
  const player = spawnPlayer("loot_test");
  const loot = grantKillLoot(player);
  assert.equal(loot.type, "sync_loot");
  assert.equal(loot.itemId, "item_dust");
  assert.ok(player.inventory.some((slot) => slot.itemId === "item_dust" && slot.quantity >= 1));
});

test("world spawns multiple monster types", () => {
  resetWorld();
  assert.ok(liveMonsters.has("monster_slime_1"));
  assert.ok(liveMonsters.has("monster_ember_1"));
  assert.ok(liveMonsters.has("monster_gust_1"));
  assert.ok(liveMonsters.has("monster_brute_1"));
});

test("threat rises on damage and picks top aggressor", () => {
  resetWorld();
  const a = spawnPlayer("threat_a");
  const b = spawnPlayer("threat_b");
  a.entity.mapId = "field_ridge";
  b.entity.mapId = "field_ridge";
  a.entity.x = 6;
  a.entity.y = 10;
  b.entity.x = 6.5;
  b.entity.y = 10;
  const slime = liveMonsters.get("monster_slime_1");
  assert.ok(slime);
  slime.x = 6;
  slime.y = 10;
  const now = Date.now();
  const t1 = notePlayerDamageThreat(a.entity.id, "monster_slime_1", 20, now);
  assert.ok(t1 && t1.type === "sync_threat");
  assert.equal(t1.topId, a.entity.id);
  notePlayerDamageThreat(b.entity.id, "monster_slime_1", 80, now + 1);
  const t2 = notePlayerDamageThreat(b.entity.id, "monster_slime_1", 5, now + 2);
  assert.ok(t2 && t2.type === "sync_threat");
  assert.equal(t2.topId, b.entity.id);
  assert.ok(t2.entries.some((e) => e.playerId === a.entity.id && e.pct > 0));
});

test("inspect returns player equip and monster combat fields", () => {
  resetWorld();
  const player = spawnPlayer("inspect_me");
  const p = buildInspect(player.entity.id, Date.now());
  assert.equal(p.type, "sync_inspect");
  if (p.type === "sync_inspect") {
    assert.equal(p.kind, "player");
    assert.ok(p.weaponId);
    assert.ok(p.atk > 0);
  }
  const m = buildInspect("monster_slime_1", Date.now());
  assert.equal(m.type, "sync_inspect");
  if (m.type === "sync_inspect") {
    assert.equal(m.kind, "monster");
    assert.equal(m.weaponId, undefined);
    assert.ok(m.monsterType);
  }
});
