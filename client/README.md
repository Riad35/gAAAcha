# Unity client

Open: `gAAAcha/client/Unity/gatcha1/`  
Server: `cd server && npm run dev` then **Play**.

Canonical C# lives in `gAAAcha/client/Stubs/`. Sync into `Unity/gatcha1/Assets/_Project/Scripts/` (Core / Network / World / UI) with `tools/sync-stubs.ps1`.

Skill ids and hotkeys: [`docs/ATTACKS.md`](../docs/ATTACKS.md). **F1** in-game help matches the Adventurer kit.

## Controls

| Input | Action |
|-------|--------|
| WASD / click ground | Move |
| Z / C / middle-mouse drag | Camera yaw (pitch locked, dimetric) |
| Tab | Cycle lock-on |
| Click enemy | Lock that enemy |
| Click player / party row | Lock that ally (blue ring) |
| Double-click empty ground | Clear lock |
| Space | Auto Attack (Hauptwaffe) |
| 1 | Shot (Sekundärwaffe) |
| 2 | Shockwave (ground disc, enemies) |
| 3 | Dash |
| 4 | Rally (nearby allies) |
| 5 | Hook Shot |
| 6 | Mend (ally or self) |
| 7 | Decoy |
| N | Swap Haupt ↔ Sekundär |
| I | Inventory |
| **G** | Banner / pity panel |
| T | 10-pull (opens banner) |
| J / K / U | Quests / friends / settings |
| **F1** | Adventurer hotkey help |
| Esc | Close panels / settings |

Hover a hotbar slot for full name, MP cost, cooldown, and weapon slot. **W2!** means Shot wants a secondary weapon and slot 2 is empty.

## HUD

- Cast bar above the hotbar while a skill plays
- Cooldown sweep + remaining seconds on the slot
- MP cost on the slot (blue if you cannot afford it)
- W1 / W2 weapon-slot hint (gold = Haupt/AA, blue = Sekundär)
- **N** swaps Haupt ↔ Sekundär: HUD chips flash, toast, and a weapon mark on the sprite (sword / bow). Bow attack uses a pixel fallback until a sheet exists
- Shot greys if slot 2 is empty

## Expect

- Soft movement + camera only at screen edge
- Starter bag: sword, bow, ration, dust — gear and cards come from shops / banner
- Banner pity is on the **G** panel (counter, soft/hard, rates), not the HUD string
- Class cards from **level 10** (banner or Card Broker)
- Guest is the default at the login gate. Login/register need `DATABASE_URL`
- Skill tomes grant +1 skill point; the Trainer spends points

