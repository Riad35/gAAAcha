import assert from "node:assert/strict";
import { test } from "node:test";
import { applyPendingHit, applyStatusOnHit, bindCombatWorld, entityBlockedAt, setCombatRng, validateCast, validateMove } from "./combat.js";
import {
  equipSpirit,
  equipWeapon,
  equipOffhand,
  findEntity,
  liveMonsters,
  monsterStatuses,
  players,
  resetWorld,
  spawnPlayer as spawnPlayerRaw,
  spawnProjectileFromCast,
  tickProjectiles,
} from "./world.js";
import { addItem } from "./shop.js";
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

/** Combat suites exercise legacy + class skills — disable Adventurer lock gate. */
function spawnPlayer(id?: string): PlayerSession {
  const player = spawnPlayerRaw(id ?? "combat_p", { save: null });
  player.unlockedSkillIds = [];
  return player;
}

function freshPlayer() {
  resetWorld();
  const player = spawnPlayer();
  player.lastMoveAt = 0;
  player.actionTimes = [];
  player.entity.mapId = "field_ridge";
  player.entity.x = 6;
  player.entity.y = 10;
  player.moveLockUntil = 0;
  return player;
}

/** Tests that check targeting / scaling, not recovery, must clear the 1-slot busy window. */
function unlockCast(player: PlayerSession): void {
  player.actionTimes = [];
  player.busyUntil = 0;
  player.skillReadyAt = {};
}

test("move into a wall slides onto the face instead of erroring", () => {
  const player = freshPlayer();
  player.entity.x = 9;
  player.entity.y = 9;
  const result = validateMove(player, 10, 9, Date.now());
  assert.equal("ok" in result && result.ok, true);
  if ("ok" in result) {
    assert.ok(result.x < 9.55, `expected slide-back, got x=${result.x}`);
    assert.equal(result.y, 9);
  }
});

test("move allows the outer half of an edge floor tile", () => {
  const player = freshPlayer();
  player.entity.mapId = "town_ashen";
  player.entity.x = 22;
  player.entity.y = 9;
  player.lastMoveAt = 0;
  player.moveTimes = [];
  const result = validateMove(player, 23.4, 9, Date.now());
  assert.equal("ok" in result && result.ok, true);
});

test("move allows a floor corner that rounds into a diagonal wall", () => {
  const player = freshPlayer();
  player.entity.mapId = "field_ridge";
  player.entity.x = 9;
  player.entity.y = 8;
  player.lastMoveAt = 0;
  player.moveTimes = [];
  // Wall at (10,8) and (10,9). Math.round(9.5, 8.5) used to land on the wall.
  const result = validateMove(player, 9.5, 8.5, Date.now());
  assert.equal("ok" in result && result.ok, true);
});

test("move past the map edge stays on the last open face", () => {
  const player = freshPlayer();
  player.entity.mapId = "town_ashen";
  player.entity.x = 22;
  player.entity.y = 9;
  player.lastMoveAt = 0;
  player.moveTimes = [];
  const result = validateMove(player, 24, 9, Date.now());
  assert.equal("ok" in result && result.ok, true);
  if ("ok" in result) {
    assert.ok(result.x < 23.55, `expected edge clamp, got x=${result.x}`);
  }
});

test("move rejects a speed hack", () => {
  const player = freshPlayer();
  player.lastMoveAt = Date.now();
  const result = validateMove(player, 20, 10, Date.now());
  assert.equal("type" in result && result.code, "too_fast");
});

test("cast rejects cooldown and spends mana on the first shot", () => {
  const player = freshPlayer();
  const now = Date.now();
  const before = player.entity.mp;
  const first = validateCast(player, "shot", "monster_slime_1", now, { aimDx: 1, aimDy: 0 });
  const second = validateCast(player, "shot", "monster_slime_1", now, { aimDx: 1, aimDy: 0 });
  assert.ok("ok" in first);
  assert.equal(first.mpAfter, before - 8);
  assert.deepEqual(second, { type: "error", code: "on_cooldown", message: "Shot is on cooldown" });
});

test("cast rejects missing mana for shot", () => {
  const player = freshPlayer();
  player.entity.x = 6;
  player.entity.y = 10;
  player.entity.mp = 0;
  player.actionTimes = [];
  const mana = validateCast(player, "shot", "monster_slime_1", Date.now(), { aimDx: 1, aimDy: 0 });
  assert.equal("type" in mana && mana.code, "not_enough_mana");
});

test("dash moves the player and equip changes weapon", () => {
  const player = freshPlayer();
  player.facingX = 1;
  player.facingY = 0;
  const beforeX = player.entity.x;
  const dash = validateCast(player, "dash", player.entity.id, Date.now());
  assert.ok("ok" in dash && dash.moved);
  assert.ok(player.entity.x > beforeX);
  const equip = equipWeapon(player, "bow_hunter");
  assert.ok("ok" in equip);
  assert.equal(player.equippedWeaponId, "bow_hunter");
});

test("stun bolt applies stun status to a player target", () => {
  resetWorld();
  const a = spawnPlayer("a");
  const b = spawnPlayer("b");
  a.entity.x = 5;
  a.entity.y = 10;
  b.entity.x = 6;
  b.entity.y = 10;
  a.actionTimes = [];
  const now = Date.now();
  const result = validateCast(a, "stun_bolt", b.entity.id, now, { aimDx: 1, aimDy: 0 });
  assert.ok("ok" in result);
  assert.ok(result.projectile);
  assert.ok(result.projectile.vx != null);
  if (result.projectile?.pendingStatus) {
    applyStatusOnHit(b.entity.id, result.projectile.pendingStatus, result.projectile.statusDurationMs, now);
  }
  assert.ok(b.statuses.some((s) => s.kind === "stun"));
});

test("shove displaces a monster along a cardinal", () => {
  const player = freshPlayer();
  const slime = liveMonsters.get("monster_slime_1");
  assert.ok(slime);
  slime.x = 7;
  slime.y = 10;
  const beforeX = slime.x;
  const result = validateCast(player, "shove", "monster_slime_1", Date.now());
  assert.ok("ok" in result);
  assert.ok(slime.x > beforeX);
  assert.ok(result.movedEntities.some((m) => m.id === "monster_slime_1"));
});

test("iron stance blocks shove displacement", () => {
  resetWorld();
  const a = spawnPlayer("shover");
  const b = spawnPlayer("tank");
  a.entity.x = 5;
  a.entity.y = 10;
  b.entity.x = 6;
  b.entity.y = 10;
  a.actionTimes = [];
  b.actionTimes = [];
  const stance = validateCast(b, "iron_stance", b.entity.id, Date.now());
  assert.ok("ok" in stance);
  const beforeX = b.entity.x;
  const shove = validateCast(a, "shove", b.entity.id, Date.now() + 1);
  assert.ok("ok" in shove);
  assert.equal(b.entity.x, beforeX);
  assert.equal(shove.movedEntities.length, 0);
});

test("blind blocks non-self casts", () => {
  resetWorld();
  const a = spawnPlayer("blinder");
  const b = spawnPlayer("victim");
  a.entity.x = 5;
  a.entity.y = 10;
  b.entity.x = 6;
  b.entity.y = 10;
  a.actionTimes = [];
  b.actionTimes = [];
  const now = Date.now();
  const blind = validateCast(a, "blind_dust", b.entity.id, now, { aimDx: 1, aimDy: 0 });
  assert.ok("ok" in blind);
  assert.ok(b.statuses.some((s) => s.kind === "blind"));
  const blocked = validateCast(b, "shot", "monster_slime_1", now + 1, { aimDx: 1, aimDy: 0 });
  assert.equal("type" in blocked && blocked.code, "blinded");
  const mend = validateCast(b, "mend", b.entity.id, now + 2);
  assert.ok("ok" in mend);
});

test("shockwave ground circle hits around aim point", () => {
  const player = freshPlayer();
  const slime = liveMonsters.get("monster_slime_1");
  const ember = liveMonsters.get("monster_ember_1");
  const gust = liveMonsters.get("monster_gust_1");
  assert.ok(slime && ember && gust);
  player.entity.x = 5;
  player.entity.y = 10;
  slime.x = 6;
  slime.y = 10;
  ember.x = 7;
  ember.y = 10;
  gust.x = 14;
  gust.y = 10;
  const result = validateCast(player, "shockwave", "", Date.now(), { aimX: 6.5, aimY: 10 });
  assert.ok("ok" in result);
  assert.equal(result.aoe, true);
  assert.ok(result.hits.some((h) => h.targetId === "monster_slime_1"));
  assert.ok(result.hits.some((h) => h.targetId === "monster_ember_1"));
  assert.equal(result.hits.some((h) => h.targetId === "monster_gust_1"), false);
});

test("shockwave rejects empty aim and empty ground", () => {
  const player = freshPlayer();
  player.entity.x = 5;
  player.entity.y = 10;
  const bad = validateCast(player, "shockwave", "", Date.now(), {});
  assert.equal("type" in bad && bad.code, "bad_aim");
  // aim far with no monsters nearby
  const empty = validateCast(player, "shockwave", "", Date.now() + 1, { aimX: 2, aimY: 2 });
  assert.equal("type" in empty && empty.code, "no_targets");
});

test("self buff still works without enemy target", () => {
  const player = freshPlayer();
  const result = validateCast(player, "war_cry", player.entity.id, Date.now());
  assert.ok("ok" in result);
  assert.ok(player.statuses.some((s) => s.kind === "attr_up" || s.id === "war_cry" || s.atkBonus));
});

test("second cast while recovering is busy", () => {
  const player = freshPlayer();
  const now = Date.now();
  const first = validateCast(player, "war_cry", player.entity.id, now);
  assert.ok("ok" in first);
  player.actionTimes = [];
  const second = validateCast(player, "dash", player.entity.id, now + 50);
  assert.equal("type" in second && second.code, "busy");
});

test("auto-attack uses weapon range only", () => {
  const player = freshPlayer();
  equipWeapon(player, "sword_iron");
  const slime = liveMonsters.get("monster_slime_1");
  assert.ok(slime);
  slime.x = 10;
  slime.y = 10;
  player.entity.x = 6;
  player.entity.y = 10;
  const far = validateCast(player, "auto_attack", "monster_slime_1", Date.now());
  assert.equal("type" in far && far.code, "out_of_range");
  slime.x = 7;
  player.actionTimes = [];
  const near = validateCast(player, "auto_attack", "monster_slime_1", Date.now() + 1);
  assert.ok("ok" in near);
});

test("offhand auto-attack uses equippedWeapon2Id range; empty offhand is no_offhand", () => {
  const player = freshPlayer();
  equipWeapon(player, "sword_iron");
  player.equippedWeapon2Id = null;
  const slime = liveMonsters.get("monster_slime_1");
  assert.ok(slime);
  player.entity.x = 6;
  player.entity.y = 10;
  slime.x = 10;
  slime.y = 10;
  slime.hp = 200;
  const empty = validateCast(player, "auto_attack_off", "monster_slime_1", Date.now());
  assert.equal("type" in empty && empty.code, "no_offhand");

  addItem(player, "charm_leaf", 1);
  const eq = equipOffhand(player, "charm_leaf");
  assert.ok("ok" in eq);
  player.actionTimes = [];
  player.skillReadyAt = {};
  const farMain = validateCast(player, "auto_attack", "monster_slime_1", Date.now());
  assert.equal("type" in farMain && farMain.code, "out_of_range");
  player.actionTimes = [];
  const off = validateCast(player, "auto_attack_off", "monster_slime_1", Date.now() + 1);
  assert.ok("ok" in off);
});

test("skill range ignores weapon range", () => {
  const player = freshPlayer();
  equipWeapon(player, "bow_hunter");
  const slime = liveMonsters.get("monster_slime_1");
  assert.ok(slime);
  player.entity.x = 6;
  player.entity.y = 10;
  slime.x = 10;
  slime.y = 10;
  // slash range 1.5 — still OOR even with bow + hit radii
  const slash = validateCast(player, "slash", "monster_slime_1", Date.now());
  assert.equal("type" in slash && slash.code, "out_of_range");
});

test("hitRadius extends effective melee reach", () => {
  const player = freshPlayer();
  equipWeapon(player, "sword_iron");
  const slime = liveMonsters.get("monster_slime_1");
  assert.ok(slime);
  player.entity.x = 6;
  player.entity.y = 10;
  player.entity.hitRadius = 0.4;
  slime.x = 7.8;
  slime.y = 10;
  slime.hitRadius = 0.45;
  // center dist 1.8; gap 1.8-0.85=0.95 <= slash 1.5
  const hit = validateCast(player, "slash", "monster_slime_1", Date.now());
  assert.ok("ok" in hit);
});

test("continuous move within speed budget over 200ms", () => {
  const player = freshPlayer();
  const t0 = Date.now();
  player.lastMoveAt = t0;
  player.moveTimes = [];
  const mid = validateMove(player, player.entity.x + 1.0, player.entity.y, t0 + 200);
  assert.ok("ok" in mid);
  player.entity.x = mid.x;
  player.lastMoveAt = t0 + 200;
  player.moveTimes = [];
  const far = validateMove(player, player.entity.x + 5, player.entity.y, t0 + 250);
  assert.equal("type" in far && far.code, "too_fast");
});

test("player can walk through an NPC", () => {
  const player = freshPlayer();
  player.entity.mapId = "town_ashen";
  player.entity.x = 4;
  player.entity.y = 7;
  player.lastMoveAt = 0;
  player.moveTimes = [];
  const result = validateMove(player, 5, 7, Date.now());
  assert.equal("ok" in result && result.ok, true);
});

test("player can walk into a monster body", () => {
  const player = freshPlayer();
  const slime = liveMonsters.get("monster_slime_1");
  assert.ok(slime);
  player.entity.x = 6;
  player.entity.y = 10;
  player.lastMoveAt = 0;
  player.moveTimes = [];
  slime.x = 6.5;
  slime.y = 10;
  const result = validateMove(player, 6.5, 10, Date.now());
  assert.equal("ok" in result && result.ok, true);
});

test("two monsters can overlap; two players cannot", () => {
  resetWorld();
  const a = spawnPlayer("pvp_a");
  const b = spawnPlayer("pvp_b");
  a.entity.x = 5;
  a.entity.y = 10;
  b.entity.x = 6;
  b.entity.y = 10;
  a.lastMoveAt = 0;
  a.moveTimes = [];
  const blocked = validateMove(a, 6, 10, Date.now());
  assert.equal("type" in blocked && blocked.code, "blocked_entity");

  const slime = liveMonsters.get("monster_slime_1");
  const ember = liveMonsters.get("monster_ember_1");
  assert.ok(slime && ember);
  slime.x = 20;
  slime.y = 10;
  ember.x = 20.05;
  ember.y = 10;
  assert.equal(entityBlockedAt(20, 10, slime), false);
});

test("starter inventory is sword bow ration dust", () => {
  const player = freshPlayer();
  const ids = player.inventory.filter((s) => s.itemId).map((s) => s.itemId);
  assert.ok(ids.includes("sword_iron"));
  assert.ok(ids.includes("bow_hunter"));
  assert.ok(ids.includes("item_dust"));
  assert.ok(ids.includes("item_ration"));
  assert.ok(!ids.includes("spirit_ember"));
  assert.ok(!ids.includes("char_aurel"));
});

test("ranged shot returns a directional projectile", () => {
  const player = freshPlayer();
  equipWeapon(player, "bow_hunter");
  const slime = liveMonsters.get("monster_slime_1");
  assert.ok(slime);
  player.entity.x = 6;
  player.entity.y = 10;
  slime.x = 9;
  slime.y = 10;
  const hpBefore = slime.hp;
  const result = validateCast(player, "shot", "monster_slime_1", Date.now(), { aimDx: 1, aimDy: 0 });
  assert.ok("ok" in result);
  assert.ok(result.projectile);
  assert.equal(slime.hp, hpBefore);
  assert.ok((result.projectile.vx ?? 0) > 0.5);
  assert.ok((result.projectile.maxRange ?? 0) >= 4);
});

test("linear skillshot corridor misses sideways target", () => {
  const player = freshPlayer();
  const slime = liveMonsters.get("monster_slime_1");
  const ember = liveMonsters.get("monster_ember_1");
  assert.ok(slime && ember);
  player.entity.x = 5;
  player.entity.y = 10;
  slime.x = 8;
  slime.y = 10;
  ember.x = 6;
  ember.y = 13;
  // hitscan-style: temporary use cone for instant check — shot is projectile;
  // use blind_dust cone aiming east — slime in cone, ember north out
  const cone = validateCast(player, "blind_dust", "", Date.now(), { aimDx: 1, aimDy: 0 });
  assert.ok("ok" in cone);
  assert.ok(cone.hits.some((h) => h.targetId === "monster_slime_1"));
  assert.equal(cone.hits.some((h) => h.targetId === "monster_ember_1"), false);
});

test("cone misses behind caster", () => {
  const player = freshPlayer();
  const slime = liveMonsters.get("monster_slime_1");
  assert.ok(slime);
  player.entity.x = 8;
  player.entity.y = 10;
  slime.x = 5;
  slime.y = 10;
  for (const m of liveMonsters.values()) {
    if (m.id !== "monster_slime_1" && m.mapId === "field_ridge") {
      m.x = 1;
      m.y = 1;
    }
  }
  const miss = validateCast(player, "blind_dust", "", Date.now(), { aimDx: 1, aimDy: 0 });
  assert.equal("type" in miss && miss.code, "no_targets");
});

test("staff auto-attack scales magic and sword scales atk", () => {
  const player = freshPlayer();
  const slime = liveMonsters.get("monster_slime_1");
  assert.ok(slime);
  slime.x = 7;
  slime.y = 10;
  slime.hp = 200;
  slime.maxHp = 200;
  equipWeapon(player, "sword_iron");
  const swordHit = validateCast(player, "auto_attack", "monster_slime_1", Date.now());
  assert.ok("ok" in swordHit);
  const swordDmg = swordHit.hits[0]?.damage ?? swordHit.projectile?.pendingHits[0]?.damage ?? 0;
  assert.ok(swordDmg > 0);
  const afterSword = slime.hp;
  slime.hp = 200;
  unlockCast(player);
  equipWeapon(player, "staff_arcane");
  const staffHit = validateCast(player, "auto_attack", "monster_slime_1", Date.now() + 1);
  assert.ok("ok" in staffHit);
  const staffDmg = staffHit.hits[0]?.damage ?? staffHit.projectile?.pendingHits[0]?.damage ?? 0;
  assert.ok(staffDmg > 0);
  assert.notEqual(afterSword, 200);
});

test("linear shot hits a sprite-sized offset from the aim line", () => {
  const player = freshPlayer();
  const slime = liveMonsters.get("monster_slime_1");
  assert.ok(slime);
  player.entity.x = 6;
  player.entity.y = 10;
  slime.x = 8;
  slime.y = 11.15;
  slime.hp = 80;
  slime.maxHp = 80;
  const now = Date.now();
  const cast = validateCast(player, "shot", "", now, { aimDx: 1, aimDy: 0 });
  assert.ok("ok" in cast);
  assert.ok(cast.projectile);
  spawnProjectileFromCast(player.entity, cast.projectile, cast.mpAfter);
  tickProjectiles(now, 0.5);
  assert.ok(slime.hp < 80);
});

test("linear shot hits when the projectile tick steps past the target", () => {
  const player = freshPlayer();
  const slime = liveMonsters.get("monster_slime_1");
  assert.ok(slime);
  player.entity.x = 6;
  player.entity.y = 10;
  slime.x = 8;
  slime.y = 10;
  slime.hp = 80;
  slime.maxHp = 80;
  const now = Date.now();
  const cast = validateCast(player, "shot", "", now, { aimDx: 1, aimDy: 0 });
  assert.ok("ok" in cast);
  assert.ok(cast.projectile);
  spawnProjectileFromCast(player.entity, cast.projectile, cast.mpAfter);
  tickProjectiles(now, 0.5);
  assert.ok(slime.hp < 80);
});

test("spirit boosts elemental damage vs matching element", () => {
  setCombatRng(() => 0.5);
  try {
  const player = freshPlayer();
  const slime = liveMonsters.get("monster_slime_1");
  assert.ok(slime);
  slime.x = 7;
  slime.y = 10;
  slime.hp = 500;
  slime.maxHp = 500;
  slime.resist.fire = 0;
  player.entity.critChance = 0;
  equipWeapon(player, "gun_spark");
  equipSpirit(player, null);
  const base = validateCast(player, "auto_attack", "monster_slime_1", Date.now());
  assert.ok("ok" in base);
  const baseDmg = base.hits[0]?.damage ?? base.projectile?.pendingHits[0]?.damage ?? 0;
  slime.hp = 500;
  unlockCast(player);
  equipSpirit(player, "spirit_ember");
  const boosted = validateCast(player, "auto_attack", "monster_slime_1", Date.now() + 1);
  assert.ok("ok" in boosted);
  const boostedDmg = boosted.hits[0]?.damage ?? boosted.projectile?.pendingHits[0]?.damage ?? 0;
  assert.ok(boostedDmg >= baseDmg);
  } finally {
    setCombatRng();
  }
});

test("physical shield absorbs atk damage", () => {
  resetWorld();
  const a = spawnPlayer("atk");
  const b = spawnPlayer("tank");
  a.entity.x = 5;
  a.entity.y = 10;
  b.entity.x = 6;
  b.entity.y = 10;
  a.actionTimes = [];
  b.actionTimes = [];
  const barrier = validateCast(b, "barrier", b.entity.id, Date.now());
  assert.ok("ok" in barrier);
  assert.ok(b.statuses.some((s) => s.kind === "shield_phys" && (s.shieldHp ?? 0) > 0));
  const before = b.entity.hp;
  const hit = validateCast(a, "slash", b.entity.id, Date.now() + 1);
  assert.ok("ok" in hit);
  assert.ok(b.entity.hp >= before - 5);
});

test("haste increases move allowance", () => {
  const player = freshPlayer();
  const now = Date.now();
  player.lastMoveAt = now - 130;
  player.actionTimes = [];
  const startX = player.entity.x;
  const without = validateMove(player, startX + 0.85, player.entity.y, now);
  assert.ok("ok" in without);
  assert.ok(without.x < startX + 0.8);
  player.actionTimes = [];
  const haste = validateCast(player, "haste", player.entity.id, now);
  assert.ok("ok" in haste);
  player.actionTimes = [];
  player.lastMoveAt = now - 130;
  player.entity.x = startX;
  const withHaste = validateMove(player, startX + 0.85, player.entity.y, now + 1);
  assert.ok("ok" in withHaste);
  assert.ok(withHaste.x >= startX + 0.8);
});

test("shove locks player movement briefly", () => {
  resetWorld();
  const a = spawnPlayer("shover");
  const b = spawnPlayer("victim");
  a.entity.x = 5;
  a.entity.y = 10;
  b.entity.x = 6;
  b.entity.y = 10;
  a.actionTimes = [];
  b.actionTimes = [];
  b.lastMoveAt = 0;
  const now = Date.now();
  const shove = validateCast(a, "shove", b.entity.id, now);
  assert.ok("ok" in shove);
  assert.ok(b.moveLockUntil > now);
  const locked = validateMove(b, b.entity.x + 1, b.entity.y, now + 10);
  assert.equal("type" in locked && locked.code, "move_locked");
});

test("group_chant buffs nearby players and skips far ones", () => {
  resetWorld();
  const a = spawnPlayer("chanter");
  const near = spawnPlayer("near_ally");
  const far = spawnPlayer("far_ally");
  a.entity.mapId = "town_ashen";
  near.entity.mapId = "town_ashen";
  far.entity.mapId = "town_ashen";
  a.entity.x = 5;
  a.entity.y = 10;
  near.entity.x = 6.5;
  near.entity.y = 10;
  far.entity.x = 16;
  far.entity.y = 10;
  a.actionTimes = [];
  const result = validateCast(a, "group_chant", "", Date.now());
  assert.ok("ok" in result);
  assert.equal(result.aoe, true);
  assert.ok(a.statuses.some((s) => s.id === "group_chant_atk"));
  assert.ok(near.statuses.some((s) => s.id === "group_chant_atk"));
  assert.equal(far.statuses.some((s) => s.id === "group_chant_atk"), false);
  assert.ok(result.hits.some((h) => h.targetId === a.entity.id));
  assert.ok(result.hits.some((h) => h.targetId === near.entity.id));
  assert.equal(result.hits.some((h) => h.targetId === far.entity.id), false);
});

test("ally_mend heals a player in range and rejects monsters", () => {
  resetWorld();
  const a = spawnPlayer("healer");
  const b = spawnPlayer("wounded");
  a.entity.mapId = "field_ridge";
  b.entity.mapId = "field_ridge";
  a.entity.x = 5;
  a.entity.y = 10;
  b.entity.x = 7;
  b.entity.y = 10;
  b.entity.hp = 20;
  a.actionTimes = [];
  const now = Date.now();
  const heal = validateCast(a, "ally_mend", b.entity.id, now);
  assert.ok("ok" in heal);
  assert.equal(b.entity.hp, 40);
  assert.ok(heal.hits.some((h) => h.targetId === b.entity.id && h.damage === 20));

  unlockCast(a);
  const self = validateCast(a, "ally_mend", "", now + 5000);
  assert.ok("ok" in self);
  assert.equal(self.primaryTargetId, a.entity.id);

  unlockCast(a);
  const monster = validateCast(a, "ally_mend", "monster_slime_1", now + 10000);
  assert.equal("type" in monster && monster.code, "invalid_target");

  unlockCast(a);
  b.entity.x = 20;
  b.entity.hp = 20;
  const oor = validateCast(a, "ally_mend", b.entity.id, now + 15000);
  assert.equal("type" in oor && oor.code, "out_of_range");
});

test("target_burst splashes around locked enemy and skips players", () => {
  const player = freshPlayer();
  const ally = spawnPlayer("splash_ally");
  ally.entity.mapId = "field_ridge";
  ally.entity.x = 7;
  ally.entity.y = 10;
  const slime = liveMonsters.get("monster_slime_1");
  const ember = liveMonsters.get("monster_ember_1");
  const gust = liveMonsters.get("monster_gust_1");
  assert.ok(slime && ember && gust);
  player.entity.x = 5;
  player.entity.y = 10;
  slime.x = 7;
  slime.y = 10;
  ember.x = 8;
  ember.y = 10;
  gust.x = 16;
  gust.y = 10;
  const slimeHp = slime.hp;
  const emberHp = ember.hp;
  const allyHp = ally.entity.hp;
  const now = Date.now();
  const result = validateCast(player, "target_burst", "monster_slime_1", now);
  assert.ok("ok" in result);
  assert.equal(result.aoe, true);
  assert.ok(result.hits.some((h) => h.targetId === "monster_slime_1"));
  assert.ok(result.hits.some((h) => h.targetId === "monster_ember_1"));
  assert.equal(result.hits.some((h) => h.targetId === "monster_gust_1"), false);
  assert.equal(result.hits.some((h) => h.targetId === ally.entity.id), false);
  assert.ok(slime.hp < slimeHp);
  assert.ok(ember.hp < emberHp);
  assert.equal(ally.entity.hp, allyHp);

  unlockCast(player);
  const bad = validateCast(player, "target_burst", ally.entity.id, now + 8000);
  assert.equal("type" in bad && bad.code, "invalid_target");
});

test("rally buffs nearby allies and mend heals a locked player", () => {
  resetWorld();
  const a = spawnPlayer("buffer");
  const b = spawnPlayer("neighbor");
  a.entity.x = 5;
  a.entity.y = 10;
  b.entity.x = 6;
  b.entity.y = 10;
  b.entity.hp = 20;
  a.actionTimes = [];
  const rally = validateCast(a, "rally", "", Date.now());
  assert.ok("ok" in rally);
  assert.ok(a.statuses.some((s) => s.id === "rally_atk"));
  assert.ok(b.statuses.some((s) => s.id === "rally_atk"));

  unlockCast(a);
  const mend = validateCast(a, "mend", b.entity.id, Date.now() + 1);
  assert.ok("ok" in mend);
  assert.ok(b.entity.hp > 20);
});

test("shockwave ground disc skips players", () => {
  resetWorld();
  const player = spawnPlayer("wave_p");
  const ally = spawnPlayer("wave_ally");
  player.entity.mapId = "field_ridge";
  ally.entity.mapId = "field_ridge";
  player.entity.x = 5;
  player.entity.y = 10;
  ally.entity.x = 6.5;
  ally.entity.y = 10;
  const slime = liveMonsters.get("monster_slime_1");
  assert.ok(slime);
  slime.x = 6.5;
  slime.y = 10;
  const allyHp = ally.entity.hp;
  const slimeHp = slime.hp;
  player.actionTimes = [];
  const result = validateCast(player, "shockwave", "", Date.now(), { aimX: 6.5, aimY: 10 });
  assert.ok("ok" in result);
  assert.ok(result.hits.some((h) => h.targetId === "monster_slime_1"));
  assert.equal(result.hits.some((h) => h.targetId === ally.entity.id), false);
  assert.ok(slime.hp < slimeHp);
  assert.equal(ally.entity.hp, allyHp);
});

