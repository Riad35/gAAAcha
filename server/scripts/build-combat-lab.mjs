/**
 * Expand test_arena into a zoned combat lab; thin slime_yard.
 * Run: node server/scripts/build-combat-lab.mjs
 */
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), "..");
const dataDir = path.join(root, "data");

function load(name) {
  return JSON.parse(fs.readFileSync(path.join(dataDir, name), "utf8"));
}
function save(name, data) {
  fs.writeFileSync(path.join(dataDir, name), JSON.stringify(data, null, 4) + "\n");
}

/** Light corridor walls: horizontal/vertical segments with gap openings. */
function wallH(y, x0, x1) {
  const tiles = [];
  for (let x = x0; x <= x1; x++) tiles.push({ x, y });
  return tiles;
}
function wallV(x, y0, y1) {
  const tiles = [];
  for (let y = y0; y <= y1; y++) tiles.push({ x, y });
  return tiles;
}

const maps = load("maps.json");
const monsters = load("monsters.json");
const portals = load("portals.json");

// --- maps: expand test_arena ---
const arenaIdx = maps.findIndex((m) => m.id === "test_arena");
if (arenaIdx < 0) throw new Error("test_arena missing");

const blocked = [
  // corners / border anchors
  { x: 0, y: 0 },
  { x: 39, y: 0 },
  { x: 0, y: 27 },
  { x: 39, y: 27 },
  // Hub separator (east of hub) with gap at y=13-15
  ...wallV(8, 2, 11),
  ...wallV(8, 17, 25),
  // Melee / ranged dividers — leave corridor gaps at x=12-13
  ...wallH(10, 9, 11),
  ...wallH(10, 14, 16),
  ...wallH(18, 9, 11),
  ...wallH(18, 14, 16),
  // Mid vertical between center and east — gaps at y=13-15
  ...wallV(17, 2, 8),
  ...wallV(17, 19, 25),
  // Hazard / cannon separators with walk gaps
  ...wallH(10, 23, 25),
  ...wallH(10, 28, 30),
  ...wallH(18, 23, 25),
  ...wallH(18, 28, 30),
  ...wallV(31, 11, 12),
  ...wallV(31, 16, 17),
];

const hazards = [];
for (let y = 13; y <= 15; y++) {
  for (let x = 24; x <= 26; x++) {
    hazards.push({ x, y, damage: 4 });
  }
}

maps[arenaIdx] = {
  id: "test_arena",
  name: "Combat Lab",
  width: 40,
  height: 28,
  spawn: { x: 4, y: 14 },
  blocked,
  hazards,
};

const yardIdx = maps.findIndex((m) => m.id === "slime_yard");
if (yardIdx >= 0) {
  maps[yardIdx] = {
    id: "slime_yard",
    name: "Slime Yard (stub)",
    width: 16,
    height: 12,
    spawn: { x: 3, y: 6 },
    blocked: [
      { x: 0, y: 0 },
      { x: 15, y: 0 },
      { x: 0, y: 11 },
      { x: 15, y: 11 },
    ],
  };
}

// --- monsters: drop arena + yard, add lab set ---
const kept = monsters.filter((m) => m.mapId !== "test_arena" && m.mapId !== "slime_yard");

function mon(partial) {
  return {
    magicResist: 1,
    hitRadius: 0.45,
    attackMs: 1400,
    prefer: "melee",
    aggroMode: "hostile",
    ...partial,
  };
}

const lab = [
  // Melee pad (south-west of hub gap) — low HP, short aggro
  mon({
    id: "slime",
    name: "Lab Slime A",
    mapId: "test_arena",
    hp: 35,
    atk: 5,
    def: 1,
    magicResist: 0,
    element: "water",
    aggroRange: 4,
    leashRange: 8,
    monsterType: "slime",
    x: 11,
    y: 5,
    respawnId: "lab_melee_1",
  }),
  mon({
    id: "slime",
    name: "Lab Slime B",
    mapId: "test_arena",
    hp: 35,
    atk: 5,
    def: 1,
    magicResist: 0,
    element: "water",
    aggroRange: 4,
    leashRange: 8,
    monsterType: "slime",
    x: 13,
    y: 6,
    respawnId: "lab_melee_2",
  }),
  mon({
    id: "slime",
    name: "Lab Slime C",
    mapId: "test_arena",
    hp: 35,
    atk: 5,
    def: 1,
    magicResist: 0,
    element: "water",
    aggroRange: 4,
    leashRange: 8,
    monsterType: "slime",
    x: 15,
    y: 5,
    respawnId: "lab_melee_3",
  }),

  // Ranged pad (north of hub)
  mon({
    id: "ember",
    name: "Lab Ember",
    mapId: "test_arena",
    hp: 40,
    atk: 8,
    def: 1,
    magicResist: 2,
    element: "fire",
    hitRadius: 0.35,
    aggroRange: 5,
    leashRange: 9,
    attackMs: 1800,
    prefer: "ranged",
    monsterType: "ember",
    x: 12,
    y: 22,
    respawnId: "lab_ranged_ember",
  }),
  mon({
    id: "gust",
    name: "Lab Gust",
    mapId: "test_arena",
    hp: 32,
    atk: 5,
    def: 0,
    magicResist: 4,
    element: "wind",
    hitRadius: 0.35,
    aggroRange: 5,
    leashRange: 9,
    attackMs: 1600,
    prefer: "ranged",
    monsterType: "gust",
    x: 15,
    y: 23,
    respawnId: "lab_ranged_gust",
  }),

  // Dummy pad (center)
  mon({
    id: "training_dummy",
    name: "Training Dummy",
    mapId: "test_arena",
    hp: 99999,
    atk: 0,
    def: 0,
    magicResist: 0,
    element: "earth",
    hitRadius: 0.6,
    aggroRange: 0,
    leashRange: 0,
    attackMs: 99999,
    aggroMode: "neutral",
    monsterType: "immortal",
    x: 20,
    y: 14,
    respawnId: "lab_dummy_1",
  }),

  // Force pad — ragdoll for shove/pull
  mon({
    id: "ragdoll",
    name: "Ragdoll Dummy",
    mapId: "test_arena",
    hp: 2500,
    atk: 0,
    def: 2,
    magicResist: 2,
    element: "earth",
    hitRadius: 0.55,
    aggroRange: 0,
    leashRange: 0,
    attackMs: 99999,
    aggroMode: "neutral",
    monsterType: "ragdoll",
    x: 26,
    y: 5,
    respawnId: "lab_ragdoll_1",
  }),

  // Chase lane (north corridor, long leash)
  mon({
    id: "slime",
    name: "Chase Slime A",
    mapId: "test_arena",
    hp: 50,
    atk: 7,
    def: 2,
    magicResist: 0,
    element: "water",
    aggroRange: 7,
    leashRange: 14,
    monsterType: "slime",
    x: 22,
    y: 22,
    respawnId: "lab_chase_1",
  }),
  mon({
    id: "slime",
    name: "Chase Slime B",
    mapId: "test_arena",
    hp: 50,
    atk: 7,
    def: 2,
    magicResist: 0,
    element: "water",
    aggroRange: 7,
    leashRange: 14,
    monsterType: "slime",
    x: 28,
    y: 23,
    respawnId: "lab_chase_2",
  }),

  // Cannon LOS (east of hazard)
  mon({
    id: "cannon",
    name: "Flame Cannon",
    mapId: "test_arena",
    hp: 200,
    atk: 12,
    def: 8,
    magicResist: 4,
    element: "fire",
    hitRadius: 0.55,
    aggroRange: 9,
    leashRange: 1,
    attackMs: 1600,
    prefer: "ranged",
    monsterType: "cannon",
    x: 35,
    y: 14,
    respawnId: "lab_cannon_1",
  }),

  // Variety pad (SE)
  mon({
    id: "pest",
    name: "Lab Pest",
    mapId: "test_arena",
    hp: 28,
    atk: 4,
    def: 0,
    magicResist: 0,
    element: "earth",
    hitRadius: 0.35,
    aggroRange: 3,
    leashRange: 7,
    attackMs: 1600,
    aggroMode: "neutral",
    monsterType: "pest",
    x: 34,
    y: 5,
    respawnId: "lab_pest_1",
  }),
  mon({
    id: "brute",
    name: "Lab Brute",
    mapId: "test_arena",
    hp: 70,
    atk: 10,
    def: 6,
    magicResist: 1,
    element: "earth",
    hitRadius: 0.55,
    aggroRange: 3,
    leashRange: 8,
    attackMs: 2400,
    monsterType: "brute",
    x: 36,
    y: 6,
    respawnId: "lab_brute_1",
  }),
  mon({
    id: "shadow",
    name: "Lab Shadow",
    mapId: "test_arena",
    hp: 32,
    atk: 9,
    def: 1,
    magicResist: 3,
    element: "dark",
    hitRadius: 0.35,
    aggroRange: 4,
    leashRange: 8,
    attackMs: 1700,
    monsterType: "shadow",
    x: 35,
    y: 8,
    respawnId: "lab_shadow_1",
  }),
];

// slime_yard stub: no monsters (exit-only)
save("monsters.json", [...kept, ...lab]);

// --- portals ---
const portalKeep = portals.filter(
  (p) =>
    ![
      "portal_test_a",
      "portal_test_b",
      "portal_test_to_town",
      "portal_town_to_test",
      "portal_test_to_slime",
      "portal_slime_to_test",
    ].includes(p.id),
);

const labPortals = [
  {
    id: "portal_test_a",
    mapId: "test_arena",
    x: 3,
    y: 11,
    targetMapId: "test_arena",
    targetX: 36,
    targetY: 22,
    label: "Hub → Chase",
  },
  {
    id: "portal_test_b",
    mapId: "test_arena",
    x: 36,
    y: 22,
    targetMapId: "test_arena",
    targetX: 3,
    targetY: 11,
    label: "Chase → Hub",
  },
  {
    id: "portal_test_to_town",
    mapId: "test_arena",
    x: 3,
    y: 17,
    targetMapId: "town_ashen",
    targetX: 6,
    targetY: 9,
    label: "Exit to Town",
  },
  {
    id: "portal_town_to_test",
    mapId: "town_ashen",
    x: 12,
    y: 3,
    targetMapId: "test_arena",
    targetX: 4,
    targetY: 14,
    label: "Enter Combat Lab",
  },
  {
    id: "portal_test_to_slime",
    mapId: "test_arena",
    x: 3,
    y: 12,
    targetMapId: "slime_yard",
    targetX: 3,
    targetY: 6,
    label: "Stub → Slime Yard",
  },
  {
    id: "portal_slime_to_test",
    mapId: "slime_yard",
    x: 3,
    y: 4,
    targetMapId: "test_arena",
    targetX: 4,
    targetY: 14,
    label: "Gate → Combat Lab",
  },
];

save("portals.json", [...portalKeep, ...labPortals]);
save("maps.json", maps);

console.log("Combat lab built:");
console.log("  test_arena", maps[arenaIdx].width, "x", maps[arenaIdx].height);
console.log("  lab monsters", lab.length);
console.log("  slime_yard monsters", 0);
console.log("  portals updated", labPortals.length);
