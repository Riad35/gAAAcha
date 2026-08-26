# Map grid drafting

Author maps as a **2D digit grid**. The server compiles the grid into `blocked` / `spawn` / `hazards` / default props. The Unity client already builds a **3D XZ gray-box** (floor quads + wall cubes) from that collision list. Later, the same tile ids swap cubes for prefabs — no second map format.

## Files

| File | Role |
|------|------|
| `server/data/maps/<id>.map.txt` | Digits you draw (git-diffable) |
| `server/data/maps.json` | `id`, `name`, `width`, `height`, `"grid": "maps/<id>.map.txt"`, plus `props` kinds and leftover `blocked` fallback |
| `server/data/portals.json` | Portal destinations. Cell must be **3** on the grid |
| `server/data/npcs.json` / `monsters.json` | Entities. Grid **6** / **7** are markers only |

Regenerate grids from current JSON (does not delete `blocked`):

```bash
cd server
npx tsx scripts/blocked-to-grid.ts
```

## Coordinates

- First row of the file is **y = 0** (smallest y).
- First digit of a row is **x = 0**.
- `width` / `height` in `maps.json` must match row count and row length.

Comments (`# …`) and blank lines are ignored.

## Tile ids (MVP)

| ID | Name | Walk | Now (3D gray-box) | Later (scene prefab key) |
|----|------|------|-------------------|--------------------------|
| 0 | floor | yes | biome floor quad | `Tiles/Floor_<biome>` |
| 1 | wall | no | dark unlit cube (h=2) | `Tiles/Wall` |
| 2 | spawn | yes | floor; unique; sets `spawn` | player start marker |
| 3 | portal | yes | floor; `portals.json` still owns target | `Tiles/Portal` |
| 4 | hazard | yes | orange floor + DoT | `Tiles/Hazard` |
| 5 | prop | no | `props[].kind` if present, else crate | `Props/<kind>` |
| 6 | npc pad | yes | floor (NPC stays in `npcs.json`) | NPC marker |
| 7 | monster pad | yes | floor (spawn stays in `monsters.json`) | spawn marker |
| 8 | water | no | blocked (renders as wall cube until client sends grid) | `Tiles/Water` |
| 9 | reserved | yes | treat as floor | doors / height later |

Exactly **one** `2` per map. Ragged rows or two spawns fail server boot.

`props` in JSON overlay kinds (`stall`, `fountain`, …). A `5` with no JSON prop becomes `crate`. JSON props still block even if the digit is `0`.

The client also draws a **cube ring one tile outside the map** for the out-of-bounds barrier (same collision the server already applies). Interior `blocked` tiles are solid dark cubes, not biome floor textures.

## 3D later

`GrayBoxWorld.SpawnFloorTile` / `SpawnWallCube` are the first two entries of a tile-id → prefab registry. Do not add a Unity scene-per-map until those prefabs exist. Optional later: include the grid in `sync_state` so water/hazard use their own materials instead of wall cubes.

## Tiny example (5×5)

```
# unit_lab  5x5
11111
10201
10301
15041
11111
```

`2` = spawn, `3` = portal, `5` = prop, `4` = hazard, `1` = wall.
