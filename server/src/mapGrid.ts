import { readFileSync } from "node:fs";
import { join } from "node:path";
import type { MapDef, PortalDef } from "./types.js";

/** Digit ids for 2D map drafting. Same ids later map to 3D prefabs. */
export const TILE = {
  floor: 0,
  wall: 1,
  spawn: 2,
  portal: 3,
  hazard: 4,
  prop: 5,
  npcPad: 6,
  monsterPad: 7,
  water: 8,
  reserved: 9,
} as const;

export const BLOCKING_TILES = new Set<number>([TILE.wall, TILE.prop, TILE.water]);

const DEFAULT_PROP_KIND = "crate";

export class MapGridError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "MapGridError";
  }
}

export function parseMapTxt(text: string, width: number, height: number, label = "grid"): number[][] {
  const rows: string[] = [];
  for (const raw of text.split(/\r?\n/)) {
    const line = raw.trim();
    if (!line || line.startsWith("#")) {
      continue;
    }
    rows.push(line);
  }
  if (rows.length !== height) {
    throw new MapGridError(`${label}: expected ${height} rows, got ${rows.length}`);
  }
  const tiles: number[][] = [];
  for (let y = 0; y < height; y += 1) {
    const digits = parseRow(rows[y], width, `${label} row ${y}`);
    tiles.push(digits);
  }
  return tiles;
}

function parseRow(line: string, width: number, label: string): number[] {
  const chars = line.includes(" ")
    ? line.split(/\s+/).filter(Boolean)
    : [...line];
  if (chars.length !== width) {
    throw new MapGridError(`${label}: expected ${width} cells, got ${chars.length}`);
  }
  return chars.map((ch, x) => {
    if (!/^[0-9]$/.test(ch)) {
      throw new MapGridError(`${label}: invalid cell '${ch}' at x=${x}`);
    }
    return Number(ch);
  });
}

export function loadMapTxt(absPath: string, width: number, height: number): number[][] {
  const text = readFileSync(absPath, "utf8");
  return parseMapTxt(text, width, height, absPath);
}

export function compileGrid(
  map: MapDef,
  tiles: number[][],
  portals: PortalDef[] = [],
): { map: MapDef; warnings: string[] } {
  if (tiles.length !== map.height) {
    throw new MapGridError(`${map.id}: grid height ${tiles.length} != map.height ${map.height}`);
  }
  const warnings: string[] = [];
  const blocked: { x: number; y: number }[] = [];
  const seenBlocked = new Set<string>();
  const hazards: { x: number; y: number; damage?: number }[] = [];
  const props = [...(map.props ?? [])];
  const propAt = new Set(props.map((p) => `${p.x},${p.y}`));
  const hazardDmg = new Map(
    (map.hazards ?? []).map((h) => [`${h.x},${h.y}`, h.damage] as const),
  );
  const spawns: { x: number; y: number }[] = [];

  const pushBlocked = (x: number, y: number) => {
    const key = `${x},${y}`;
    if (seenBlocked.has(key)) {
      return;
    }
    seenBlocked.add(key);
    blocked.push({ x, y });
  };

  for (let y = 0; y < map.height; y += 1) {
    const row = tiles[y];
    if (!row || row.length !== map.width) {
      throw new MapGridError(`${map.id}: ragged grid at y=${y}`);
    }
    for (let x = 0; x < map.width; x += 1) {
      const id = row[x];
      if (id === TILE.spawn) {
        spawns.push({ x, y });
      }
      if (id === TILE.hazard) {
        hazards.push({ x, y, damage: hazardDmg.get(`${x},${y}`) });
      }
      if (id === TILE.prop && !propAt.has(`${x},${y}`)) {
        props.push({ x, y, kind: DEFAULT_PROP_KIND });
        propAt.add(`${x},${y}`);
      }
      if (BLOCKING_TILES.has(id)) {
        pushBlocked(x, y);
      }
    }
  }

  if (spawns.length === 0) {
    throw new MapGridError(`${map.id}: grid needs exactly one spawn (2), found 0`);
  }
  if (spawns.length > 1) {
    throw new MapGridError(`${map.id}: grid needs exactly one spawn (2), found ${spawns.length}`);
  }

  for (const prop of props) {
    pushBlocked(prop.x, prop.y);
  }

  for (const portal of portals.filter((p) => p.mapId === map.id)) {
    const cell = tiles[portal.y]?.[portal.x];
    if (cell !== TILE.portal) {
      warnings.push(
        `${map.id}: portal ${portal.id} at (${portal.x},${portal.y}) is not tile 3 (got ${cell ?? "oob"})`,
      );
    }
  }

  return {
    map: {
      ...map,
      spawn: spawns[0],
      blocked,
      props,
      hazards: hazards.length ? hazards : map.hazards,
      tiles,
    },
    warnings,
  };
}

/** Expand wallRects + props into blocked (no grid). */
export function expandMapWalls(map: MapDef): MapDef {
  const blocked = [...(map.blocked ?? [])];
  const seen = new Set(blocked.map((t) => `${t.x},${t.y}`));
  for (const rect of map.wallRects ?? []) {
    const w = Math.max(1, Math.floor(rect.w));
    const h = Math.max(1, Math.floor(rect.h));
    for (let x = rect.x; x < rect.x + w; x += 1) {
      for (let y = rect.y; y < rect.y + h; y += 1) {
        const key = `${x},${y}`;
        if (seen.has(key)) {
          continue;
        }
        seen.add(key);
        blocked.push({ x, y });
      }
    }
  }
  for (const prop of map.props ?? []) {
    const key = `${prop.x},${prop.y}`;
    if (seen.has(key)) {
      continue;
    }
    seen.add(key);
    blocked.push({ x: prop.x, y: prop.y });
  }
  return { ...map, blocked };
}

export function expandMapDef(
  map: MapDef,
  dataDir: string,
  portals: PortalDef[] = [],
  warn: (msg: string) => void = console.warn,
): MapDef {
  if (!map.grid) {
    return expandMapWalls(map);
  }
  const tiles = loadMapTxt(join(dataDir, map.grid), map.width, map.height);
  const { map: compiled, warnings } = compileGrid(map, tiles, portals);
  for (const msg of warnings) {
    warn(msg);
  }
  return compiled;
}

export function formatMapTxt(id: string, width: number, height: number, tiles: number[][]): string {
  const lines = [
    `# ${id}  ${width}x${height}`,
    "# y=0 is the first row (smallest y). Digits are tile ids (see docs/map-grid.md).",
  ];
  for (let y = 0; y < height; y += 1) {
    lines.push(tiles[y].join(""));
  }
  lines.push("");
  return lines.join("\n");
}
