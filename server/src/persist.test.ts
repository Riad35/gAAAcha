import assert from "node:assert/strict";
import { test } from "node:test";
import { markSessionDirty, writeSession } from "./persist.js";
import { createCharacter, resetWorld, spawnPlayer } from "./world.js";

test("writeSession skips until dirty; flush clears the flag", () => {
  resetWorld();
  const p = spawnPlayer("persist_t");
  createCharacter(p, "Saver", "adventurer");
  p.dirty = false;
  assert.equal(writeSession(p, true), false);
  markSessionDirty(p);
  assert.equal(p.dirty, true);
  assert.equal(writeSession(p, true), true);
  assert.equal(p.dirty, false);
  assert.equal(writeSession(p, true), false);
});
