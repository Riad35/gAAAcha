/**
 * One-shot: compile maps.json blocked/wallRects/props/hazards + portals/npcs/monsters
 * into data/maps/<id>.map.txt and set map.grid. Does not delete blocked arrays.
 *
 *   npx tsx scripts/blocked-to-grid.ts
 */
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { expandMapWalls, formatMapTxt, TILE } from "../src/mapGrid.js";
import type { MapDef, MonsterDef, PortalDef } from "../src/types.js";

const dataDir = join(dirname(fileURLToPath(import.meta.url)), "..", "data");
const outDir = join(dataDir, "maps");

type NpcRow = { id: string; mapId: string; x: number; y: number };

function loadJson<T>(name: string): T {
  return JSON.parse(readFileSync(join(dataDir, name), "utf8")) as T;
}

function stamp(tiles: number[][], width: number, height: number, x: number, y: number, id: number) {
  if (x < 0 || y < 0 || x >= width || y >= height) {
    return;
  }
  tiles[y][x] = id;
}

const maps = loadJson<MapDef[]>("maps.json");
const portals = loadJson<PortalDef[]>("portals.json");
const npcs = loadJson<NpcRow[]>("npcs.json");
const monsters = loadJson<MonsterDef[]>("monsters.json");

mkdirSync(outDir, { recursive: true });

for (const raw of maps) {
  const expanded = expandMapWalls(raw);
  const tiles: number[][] = [];
  for (let y = 0; y < raw.height; y += 1) {
    tiles.push(Array.from({ length: raw.width }, () => TILE.floor));
  }
  for (const t of expanded.blocked) {
    stamp(tiles, raw.width, raw.height, t.x, t.y, TILE.wall);
  }
  for (const m of monsters.filter((n) => n.mapId === raw.id)) {
    stamp(tiles, raw.width, raw.height, m.x, m.y, TILE.monsterPad);
  }
  for (const n of npcs.filter((n) => n.mapId === raw.id)) {
    stamp(tiles, raw.width, raw.height, n.x, n.y, TILE.npcPad);
  }
  for (const p of raw.props ?? []) {
    stamp(tiles, raw.width, raw.height, p.x, p.y, TILE.prop);
  }
  for (const h of raw.hazards ?? []) {
    stamp(tiles, raw.width, raw.height, h.x, h.y, TILE.hazard);
  }
  for (const p of portals.filter((p) => p.mapId === raw.id)) {
    stamp(tiles, raw.width, raw.height, p.x, p.y, TILE.portal);
  }
  stamp(tiles, raw.width, raw.height, raw.spawn.x, raw.spawn.y, TILE.spawn);

  const rel = `maps/${raw.id}.map.txt`;
  writeFileSync(join(dataDir, rel), formatMapTxt(raw.id, raw.width, raw.height, tiles), "utf8");
  raw.grid = rel;
  console.log(`wrote ${rel}  ${raw.width}x${raw.height}`);
}

writeFileSync(join(dataDir, "maps.json"), `${JSON.stringify(maps, null, 4)}\n`, "utf8");
console.log(`updated maps.json (${maps.length} maps)`);
