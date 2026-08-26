import assert from "node:assert/strict";
import { test } from "node:test";
import { isSolidAt, resolveWalk } from "./tileCollision.js";

const map = {
  width: 8,
  height: 8,
  blocked: [{ x: 4, y: 4 }],
};

test("tile center of a wall is solid", () => {
  assert.equal(isSolidAt(4, 4, map), true);
});

test("tile center of a floor is open", () => {
  assert.equal(isSolidAt(3, 3, map), false);
});

test("floor corner that rounds into a diagonal wall stays open", () => {
  // Math.round(3.5)=4, Math.round(3.5)=4 → old collision treated this as the wall.
  assert.equal(isSolidAt(3.5, 3.5, map), false);
  assert.equal(isSolidAt(3.49, 3.49, map), false);
});

test("a step inside the wall cube is solid", () => {
  assert.equal(isSolidAt(3.6, 3.6, map), true);
  assert.equal(isSolidAt(4.4, 4, map), true);
});

test("shared face between floor and wall is open", () => {
  assert.equal(isSolidAt(3.5, 4, map), false);
});

test("out of bounds interior is solid", () => {
  assert.equal(isSolidAt(-0.6, 3, map), true);
  assert.equal(isSolidAt(7.6, 3, map), true);
});

test("walk into a wall center stops on the face, no reject", () => {
  const at = resolveWalk(3, 4, 4, 4, map);
  assert.equal(isSolidAt(at.x, at.y, map), false);
  assert.ok(at.x > 3.4 && at.x < 3.51, `x=${at.x}`);
  assert.equal(at.y, 4);
});
