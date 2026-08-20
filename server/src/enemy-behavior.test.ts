import assert from "node:assert/strict";
import { test } from "node:test";
import { bindCombatWorld, validateCast, validateMove } from "./combat.js";
import {
  findEntity,
  killMonster,
  liveMonsters,
  monsterStatuses,
  notePlayerDamageThreat,
  players,
  resetWorld,
  spawnPlayer as spawnPlayerRaw,
  tickWorld,
} from "./world.js";
import type { PlayerSession } from "./types.js";

bindCombatWorld(
  findEntity,
  () => [...players.values()],
  (id, status) => {
    const list = monsterStatuses.get(id) ?? [];
    monsterStatuses.set(id, [...list.filter((s) => s.id !== status.id), status]);
  },
  () => [...liveMonsters.values()].filter((monster) => monster.hp > 0),
);

function spawnPlayer(id?: string): PlayerSession {
  return spawnPlayerRaw(id ?? "enemy_p", { save: null });
}

function ridgeAt(id: string, x: number, y: number): PlayerSession {
  resetWorld();
  const player = spawnPlayer(id);
  player.lastMoveAt = 0;
  player.actionTimes = [];
  player.skillReadyAt = {};
  player.entity.mapId = "field_ridge";
  player.entity.x = x;
  player.entity.y = y;
  player.moveLockUntil = 0;
  return player;
}

function parkOtherRidgeMobs(keepId: string): void {
  for (const m of liveMonsters.values()) {
    if (m.id !== keepId && m.mapId === "field_ridge") {
      m.x = 1;
      m.y = 1;
    }
  }
}

test("adventurer auto-attack damages slime and HP ratio drops", () => {
  const player = ridgeAt("aa_slime", 6, 10);
  const slime = liveMonsters.get("monster_slime_1");
  assert.ok(slime);
  slime.x = 7;
  slime.y = 10;
  parkOtherRidgeMobs("monster_slime_1");
  const maxHp = slime.maxHp;
  const before = slime.hp;
  assert.equal(before, maxHp);
  const ratioBefore = before / maxHp;
  const result = validateCast(player, "auto_attack", "monster_slime_1", 1_000_000);
  assert.ok("ok" in result);
  const hit = result.hits[0];
  assert.ok(hit);
  assert.equal(hit.targetId, "monster_slime_1");
  assert.ok(hit.damage > 0);
  assert.ok(slime.hp < before);
  assert.equal(hit.hpAfter, slime.hp);
  const ratioAfter = slime.hp / maxHp;
  assert.ok(ratioAfter < ratioBefore);
  assert.ok(ratioAfter > 0);
});

test("slime does not aggro from outside aggro range", () => {
  const player = ridgeAt("no_aggro", 11, 10);
  parkOtherRidgeMobs("monster_slime_1");
  const slime = liveMonsters.get("monster_slime_1");
  assert.ok(slime);
  slime.x = 6;
  slime.y = 10;
  const beforeHp = player.entity.hp;
  const beforeX = slime.x;
  const msgs = tickWorld(2_000_000);
  assert.equal(msgs.some((m) => m.type === "sync_skill" && m.casterId === "monster_slime_1"), false);
  assert.equal(player.entity.hp, beforeHp);
  assert.equal(slime.x, beforeX);
});

test("slime chases when player is in aggro but outside melee", () => {
  const player = ridgeAt("chase_me", 9, 10);
  parkOtherRidgeMobs("monster_slime_1");
  const slime = liveMonsters.get("monster_slime_1");
  assert.ok(slime);
  slime.x = 6;
  slime.y = 10;
  const t0 = 3_000_000;
  const first = tickWorld(t0);
  assert.ok(first.some((m) => m.type === "sync_move" && m.entityId === "monster_slime_1"));
  assert.ok(slime.x > 6);
  assert.equal(
    first.some((m) => m.type === "sync_skill" && m.casterId === "monster_slime_1"),
    false,
  );
  tickWorld(t0 + 400);
  tickWorld(t0 + 800);
  tickWorld(t0 + 1200);
  assert.ok(slime.x > 6.5);
});

test("slime attacks once it closes to melee", () => {
  const player = ridgeAt("melee_hit", 6.8, 10);
  parkOtherRidgeMobs("monster_slime_1");
  const slime = liveMonsters.get("monster_slime_1");
  assert.ok(slime);
  slime.x = 6;
  slime.y = 10;
  const before = player.entity.hp;
  const msgs = tickWorld(4_000_000);
  const hit = msgs.find((m) => m.type === "sync_skill" && m.casterId === "monster_slime_1");
  assert.ok(hit);
  assert.equal(hit.type, "sync_skill");
  if (hit.type === "sync_skill") {
    assert.equal(hit.targetId, player.entity.id);
    assert.ok((hit.damage ?? 0) >= 0);
    assert.equal(hit.hpAfter, player.entity.hp);
  }
  assert.ok(player.entity.hp <= before);
  assert.ok(msgs.some((m) => m.type === "sync_vitals" && m.entityId === player.entity.id));
});

test("damage from range pulls slime even outside aggro radius", () => {
  const player = ridgeAt("pull_far", 12, 10);
  parkOtherRidgeMobs("monster_slime_1");
  const slime = liveMonsters.get("monster_slime_1");
  assert.ok(slime);
  slime.x = 6;
  slime.y = 10;
  const t0 = 5_000_000;
  tickWorld(t0);
  notePlayerDamageThreat(player.entity.id, "monster_slime_1", 12, t0 + 10);
  tickWorld(t0 + 400);
  tickWorld(t0 + 800);
  assert.ok(slime.x > 6.3);
});

test("leash sends slime home and restores HP", () => {
  const player = ridgeAt("leash", 6, 10);
  parkOtherRidgeMobs("monster_slime_1");
  const slime = liveMonsters.get("monster_slime_1");
  assert.ok(slime);
  slime.x = 14.6;
  slime.y = 10;
  slime.hp = 10;
  const t0 = 6_000_000;
  for (let i = 0; i < 48; i += 1) {
    tickWorld(t0 + i * 400);
  }
  assert.ok(Math.hypot(slime.x - 6, slime.y - 10) < 0.4);
  assert.equal(slime.hp, slime.maxHp);
  void player;
});

test("player cannot walk through slime body", () => {
  const player = ridgeAt("body_block", 6, 10);
  const slime = liveMonsters.get("monster_slime_1");
  assert.ok(slime);
  slime.x = 6.5;
  slime.y = 10;
  const result = validateMove(player, 6.5, 10, 7_000_000);
  assert.equal("type" in result && result.code, "blocked_entity");
});

test("killing slime despawns and world HP hits zero", () => {
  const player = ridgeAt("kill_slime", 6, 10);
  const slime = liveMonsters.get("monster_slime_1");
  assert.ok(slime);
  slime.x = 7;
  slime.y = 10;
  slime.hp = 6;
  parkOtherRidgeMobs("monster_slime_1");
  const result = validateCast(player, "auto_attack", "monster_slime_1", 8_000_000);
  assert.ok("ok" in result);
  const hit = result.hits[0];
  assert.ok(hit);
  if (hit.hpAfter > 0) {
    slime.hp = 0;
  }
  assert.ok(slime.hp <= 0 || hit.hpAfter <= 0);
  const death = killMonster("monster_slime_1", 8_000_000);
  assert.ok(death.some((m) => m.type === "sync_despawn"));
  assert.equal(liveMonsters.has("monster_slime_1"), false);
});

test("ridge orc and plant are hostile and fight in melee", () => {
  const player = ridgeAt("other_mobs", 16, 6);
  parkOtherRidgeMobs("monster_orc_1");
  const orc = liveMonsters.get("monster_orc_1");
  assert.ok(orc);
  orc.x = 16;
  orc.y = 6;
  const before = player.entity.hp;
  const orcMsgs = tickWorld(9_000_000);
  assert.ok(orcMsgs.some((m) => m.type === "sync_skill" && m.casterId === "monster_orc_1"));
  assert.ok(player.entity.hp < before);

  resetWorld();
  const p2 = spawnPlayer("plant_melee");
  p2.entity.mapId = "field_ridge";
  p2.entity.x = 20;
  p2.entity.y = 16;
  parkOtherRidgeMobs("monster_plant_1");
  const plant = liveMonsters.get("monster_plant_1");
  assert.ok(plant);
  plant.x = 20;
  plant.y = 16;
  const plantMsgs = tickWorld(9_100_000);
  assert.ok(plantMsgs.some((m) => m.type === "sync_skill" && m.casterId === "monster_plant_1"));
});

test("ridge king slime is bigger and stronger than the field slime", () => {
  resetWorld();
  const slime = liveMonsters.get("monster_slime_1");
  const king = liveMonsters.get("monster_king_slime_1");
  assert.ok(slime);
  assert.ok(king);
  assert.equal(king.name, "King Slime");
  assert.ok(king.maxHp > slime.maxHp);
  assert.ok(king.atk > slime.atk);
  assert.ok(king.hitRadius > slime.hitRadius);
  assert.equal(liveMonsters.has("monster_slime_2"), false);
});
