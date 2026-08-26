import assert from "node:assert/strict";
import { test } from "node:test";
import {
  casterIdFromSync,
  isPrivateSync,
  mapIdFromSync,
  playerIdsOnMap,
} from "./interest.js";
import type { Entity, PlayerSession, ServerMessage } from "./types.js";

function stubSession(id: string, mapId: string, inWorld = true): PlayerSession {
  return {
    entity: { id, mapId } as Entity,
    inWorld,
  } as PlayerSession;
}

test("playerIdsOnMap skips lobby and other maps", () => {
  const a = stubSession("a", "town_ashen");
  const b = stubSession("b", "field_ridge");
  const c = stubSession("c", "town_ashen", false);
  assert.deepEqual(playerIdsOnMap("town_ashen", [a, b, c]), ["a"]);
});

test("mapIdFromSync uses spawn entity map and projectile mapId", () => {
  const lookup = (id: string) => (id === "p1" ? "field_ridge" : undefined);
  const spawn: ServerMessage = {
    type: "sync_spawn",
    entity: { id: "monster_1", mapId: "crypt_1" } as Entity,
  };
  assert.equal(mapIdFromSync(spawn, lookup), "crypt_1");
  assert.equal(mapIdFromSync({ type: "sync_move", entityId: "p1", x: 1, y: 2 }, lookup), "field_ridge");
  assert.equal(
    mapIdFromSync({ type: "sync_projectile_despawn", id: "proj_1", mapId: "field_ridge" }, lookup),
    "field_ridge",
  );
  assert.equal(mapIdFromSync({ type: "sync_loot", itemId: "item_dust", quantity: 1, inventory: [] }, lookup), undefined);
});

test("private sync types are loot/xp/inventory not world move", () => {
  assert.equal(isPrivateSync({ type: "sync_loot", itemId: "x", quantity: 1, inventory: [] }), true);
  assert.equal(isPrivateSync({ type: "sync_cond", entityId: "a", canMove: true, canAct: true, resting: false, serverTime: 1 }), true);
  assert.equal(isPrivateSync({ type: "sync_move", entityId: "a", x: 0, y: 0 }), false);
  assert.equal(casterIdFromSync({ type: "sync_skill", casterId: "p1", targetId: "m", skillId: "aa", damage: 1, hpAfter: 1, mpAfter: 1 }), "p1");
});
