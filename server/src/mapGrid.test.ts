import assert from "node:assert/strict";
import { test } from "node:test";
import { compileGrid, MapGridError, parseMapTxt, TILE } from "./mapGrid.js";
import type { MapDef } from "./types.js";

function baseMap(over: Partial<MapDef> = {}): MapDef {
  return {
    id: "unit_lab",
    name: "Unit Lab",
    width: 5,
    height: 5,
    spawn: { x: 1, y: 1 },
    blocked: [],
    ...over,
  };
}

const TINY = `
# unit_lab  5x5
11111
10201
10301
15041
11111
`;

test("parseMapTxt compiles a tiny 5x5 and skips comments", () => {
  const tiles = parseMapTxt(TINY, 5, 5);
  assert.equal(tiles.length, 5);
  assert.equal(tiles[1][2], TILE.spawn);
  assert.equal(tiles[2][2], TILE.portal);
  assert.equal(tiles[3][1], TILE.prop);
  assert.equal(tiles[3][3], TILE.hazard);
});

test("compileGrid fills blocked, spawn, hazards, and default crate", () => {
  const tiles = parseMapTxt(TINY, 5, 5);
  const { map, warnings } = compileGrid(baseMap(), tiles, [
    { id: "p1", mapId: "unit_lab", x: 2, y: 2, targetMapId: "x", targetX: 0, targetY: 0, label: "P" },
  ]);
  assert.equal(warnings.length, 0);
  assert.deepEqual(map.spawn, { x: 2, y: 1 });
  assert.ok(map.blocked.some((t) => t.x === 0 && t.y === 0));
  assert.ok(map.blocked.some((t) => t.x === 1 && t.y === 3));
  assert.ok(!map.blocked.some((t) => t.x === 2 && t.y === 1));
  assert.ok(map.hazards?.some((h) => h.x === 3 && h.y === 3));
  assert.ok(map.props?.some((p) => p.x === 1 && p.y === 3 && p.kind === "crate"));
  assert.equal(map.tiles?.[1][2], TILE.spawn);
});

test("compileGrid blocks water (8) and keeps JSON prop kind", () => {
  const text = [
    "11111",
    "12001",
    "10801",
    "15001",
    "11111",
  ].join("\n");
  const tiles = parseMapTxt(text, 5, 5);
  const { map } = compileGrid(
    baseMap({ props: [{ x: 1, y: 3, kind: "stall" }] }),
    tiles,
  );
  assert.ok(map.blocked.some((t) => t.x === 2 && t.y === 2));
  assert.ok(map.props?.some((p) => p.x === 1 && p.y === 3 && p.kind === "stall"));
  assert.equal(map.props?.filter((p) => p.x === 1 && p.y === 3).length, 1);
});

test("parseMapTxt rejects ragged rows", () => {
  assert.throws(
    () => parseMapTxt("11111\n1111\n11111\n11111\n11111", 5, 5),
    MapGridError,
  );
});

test("compileGrid rejects two spawns", () => {
  const tiles = parseMapTxt("11111\n12021\n10001\n10001\n11111", 5, 5);
  assert.throws(() => compileGrid(baseMap(), tiles), /exactly one spawn/);
});

test("compileGrid rejects zero spawns", () => {
  const tiles = parseMapTxt("11111\n10001\n10001\n10001\n11111", 5, 5);
  assert.throws(() => compileGrid(baseMap(), tiles), /found 0/);
});

test("compileGrid warns when a portal cell is not 3", () => {
  const tiles = parseMapTxt("11111\n12001\n10001\n10001\n11111", 5, 5);
  const { warnings } = compileGrid(baseMap(), tiles, [
    { id: "gate", mapId: "unit_lab", x: 3, y: 1, targetMapId: "x", targetX: 0, targetY: 0, label: "G" },
  ]);
  assert.equal(warnings.length, 1);
  assert.match(warnings[0], /gate/);
});
