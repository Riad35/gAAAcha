import assert from "node:assert/strict";
import { test } from "node:test";
import { bindCombatWorld, validateCast } from "./combat.js";
import {
  buildInspect,
  checkBossPhase,
  findEntity,
  grantKillLoot,
  killMonster,
  liveMonsterCountOnMap,
  liveMonsters,
  monsterCapForMap,
  monsterMeta,
  monsterStatuses,
  notePlayerDamageThreat,
  players,
  resetWorld,
  respawnAtHome,
  spawnMonster,
  spawnPlayer,
  tickWorld,
} from "./world.js";
import { monsters } from "./data.js";

bindCombatWorld(
  findEntity,
  () => [...players.values()],
  (id, status) => {
    const list = monsterStatuses.get(id) ?? [];
    monsterStatuses.set(id, [...list.filter((s) => s.id !== status.id), status]);
  },
  () => [...liveMonsters.values()].filter((monster) => monster.hp > 0),
);

test("tickWorld reaps monsters left at 0 hp", () => {
  resetWorld();
  const player = spawnPlayer("reap_test");
  player.entity.mapId = "field_ridge";
  const slime = liveMonsters.get("monster_slime_1");
  assert.ok(slime);
  slime.hp = 0;
  const msgs = tickWorld(Date.now());
  assert.ok(msgs.some((m) => m.type === "sync_despawn" && m.entityId === "monster_slime_1"));
  assert.equal(liveMonsters.has("monster_slime_1"), false);
});

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
  const result = validateCast(player, "auto_attack", "monster_slime_1", Date.now());
  assert.equal("type" in result && result.code, "invalid_target");
});

test("kill loot grants star dust from slime tables", () => {
  resetWorld();
  const player = spawnPlayer("loot_test");
  const loot = grantKillLoot(player, "slime", () => 0);
  assert.equal(loot.type, "sync_loot");
  assert.equal(loot.itemId, "item_dust");
  assert.ok(player.inventory.some((slot) => slot.itemId === "item_dust" && slot.quantity >= 1));
});

test("adventurer seeds sword mainhand and no offhand by default", () => {
  resetWorld();
  const p = spawnPlayer("dual_wep", { save: null });
  assert.equal(p.equippedWeaponId, "sword_iron");
  assert.equal(p.equippedWeapon2Id, null);
});

test("world spawns multiple monster types", () => {
  resetWorld();
  assert.ok(liveMonsters.has("monster_slime_1"));
  assert.ok(liveMonsters.has("monster_ember_1"));
  assert.ok(liveMonsters.has("monster_gust_1"));
  assert.ok(liveMonsters.has("monster_brute_1"));
  assert.ok(liveMonsters.has("monster_orc_1"));
  assert.ok(liveMonsters.has("monster_plant_1"));
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

test("open field maps cap live monsters to unique species", () => {
  resetWorld();
  const cap = monsterCapForMap("field_ridge");
  assert.ok(cap >= 6);
  const live = [...liveMonsters.values()].filter((m) => m.mapId === "field_ridge" && m.hp > 0);
  assert.equal(live.length, cap);
  assert.equal(liveMonsterCountOnMap("field_ridge"), cap);
  assert.ok(liveMonsters.has("monster_slime_1"));
  assert.equal(liveMonsters.has("monster_ember_2"), false);
  assert.equal(liveMonsters.has("monster_pest_2"), false);

  const species = new Map<string, string>();
  for (const m of liveMonsters.values()) {
    const def = monsterMeta.get(m.id);
    assert.ok(def);
    const key = m.mapId + ":" + def.id;
    assert.equal(species.has(key), false, "duplicate species " + key);
    species.set(key, m.id);
  }
});

test("player death stays dead until respawnAtHome", () => {
  resetWorld();
  const player = spawnPlayer("dead_stay");
  player.entity.mapId = "field_ridge";
  player.entity.x = 8;
  player.entity.y = 8;
  player.entity.hp = 0;
  assert.equal(player.entity.hp, 0);
  assert.equal(player.entity.mapId, "field_ridge");
  respawnAtHome(player);
  assert.equal(player.entity.hp, player.entity.maxHp);
  assert.equal(player.entity.mapId, player.homeMapId);
});

test("tower and crypt bosses phase and telegraph before the hit", () => {
  resetWorld();
  const f2 = monsters.find((m) => m.id === "tower_boss_f2");
  assert.ok(f2);
  const boss = spawnMonster(f2, "m_boss_f2", 10, 7);
  boss.hp = Math.floor(boss.maxHp * 0.5);
  const phaseMsgs = checkBossPhase("m_boss_f2");
  assert.ok(phaseMsgs.some((m) => m.type === "sync_chat" && m.text.includes("phase 2")));
  const p = spawnPlayer("boss_t");
  p.entity.mapId = boss.mapId;
  p.entity.x = 10.4;
  p.entity.y = 7;
  const now = Date.now();
  const windup = tickWorld(now);
  assert.ok(windup.some((m) => m.type === "sync_fx" && m.kind === "telegraph"));
  assert.equal(windup.some((m) => m.type === "sync_skill" && m.casterId === "m_boss_f2"), false);
  const hit = tickWorld(now + 800);
  assert.ok(hit.some((m) => m.type === "sync_skill" && m.casterId === "m_boss_f2"));
});
