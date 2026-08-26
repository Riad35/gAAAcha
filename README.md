# gAAAcha

Gray-box slice: Node WebSocket server is the source of truth. Unity client comes after.

## Quick start

```bash
cd server
npm install
npm run dev
```

Server listens on `ws://127.0.0.1:7777`. If you see `EADDRINUSE`, kill the old Node/tsx process on 7777 and retry:

```powershell
Get-NetTCPConnection -LocalPort 7777
```

Guest play works without Postgres. `DATABASE_URL` is optional (login/register only).

## Layout

- `server/` — TypeScript game server (`request_*` / `sync_*`)
- `server/db/schema.sql` — PostgreSQL 17 contract
- `server/.runtime/saves/` — guest file persist (pity/inventory/position)
- `client/Unity/gatcha1/` — **active** Unity 6.3 client (only Unity root)
- `client/Stubs/` — canonical C# (edit here, then sync)

## Stubs → Unity sync

```powershell
powershell -NoProfile -File tools/sync-stubs.ps1         # copy Stubs → gatcha1
powershell -NoProfile -File tools/sync-stubs.ps1 -Check  # fail if copies drifted
```

Mapping under `client/Unity/gatcha1/Assets/_Project/Scripts/`:

| Stub | Unity folder |
|------|----------------|
| `NetworkBootstrap.cs`, `GameLog.cs` | `Core/` |
| `GrayBoxWorld.cs`, `WorldCoords.cs`, `SpriteCatalog.cs`, `SpriteCatalog.PlayerAnims.cs`, `SoundCatalog.cs`, `UiChrome.cs` | `World/` |
| `NetClient.cs`, `InputSender.cs`, `JsonUtil.cs`, `PredictionReconciler.cs` | `Network/` |
| `VirtualJoystick.cs` | `UI/` |

Do not push until you ask for it. Rules/memory-bank stay in the `gatcha` repo.

## Maps

Draw with digits in `server/data/maps/<id>.map.txt`. Palette and 3D-later notes: [`docs/map-grid.md`](docs/map-grid.md).

```bash
cd server
npx tsx scripts/blocked-to-grid.ts   # rebuild grids from maps.json blocked/props
```

## Disk / Unity cache

`client/Unity/gatcha1/Library/` is local Unity cache (~GB) and regenerates on open — safe to delete when you need disk. Runtime art is `StreamingAssets/`. Source dumps (PSD, Aseprite, Tiled, `__MACOSX`, `_ToReview`) belong under `Desktop/assets/`, not the Unity tree.

## Tests

```bash
cd server
npm test
npm run test:client
```

