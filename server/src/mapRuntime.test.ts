import assert from "node:assert/strict";
import { test } from "node:test";
import { getMapRuntime, occupiedMapIds } from "./mapRuntime.js";
import { liveMonsters, resetWorld, spawnPlayer, tickWorld } from "./world.js";

test("resetWorld indexes monsters per map", () => {
  resetWorld();
  const ridge = getMapRuntime("field_ridge");
  assert.ok(ridge);
  assert.ok(ridge.monsterIds.has("monster_slime_1"));
  assert.equal(ridge.occupied, false);
});

test("spawnPlayer marks that map occupied", () => {
  resetWorld();
  const player = spawnPlayer("maprt_occ");
  const rt = getMapRuntime(player.entity.mapId);
  assert.ok(rt);
  assert.ok(rt.playerIds.has(player.entity.id));
  assert.equal(rt.occupied, true);
  assert.ok(occupiedMapIds().has(player.entity.mapId));
});

test("tickWorld skips monster AI on maps with no players", () => {
  resetWorld();
  const slime = liveMonsters.get("monster_slime_1");
  assert.ok(slime);
  const x = slime.x;
  const y = slime.y;
  for (let i = 0; i < 40; i += 1) {
    tickWorld(1_000_000 + i * 400);
  }
  assert.equal(slime.x, x);
  assert.equal(slime.y, y);
});
