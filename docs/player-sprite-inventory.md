# Player sprite inventory

**Live pack is the Art female 180 set**, not this 128 StreamingAssets list. Kit map: `StreamingAssets/Data/animation_kits.json`. Holes: [`sprite-asset-tasks.md`](sprite-asset-tasks.md). Player animations: [`player-animations.md`](player-animations.md).

The tables below describe the **legacy StreamingAssets** 128 pack (still on disk; pending delete). Do not use them as the wiring checklist.

Live pack: `gAAAcha/client/Unity/gatcha1/Assets/_Project/Art/Sprites/player/female`  
Loader: `SpriteCatalog.PlayerAnims.cs` + `SpriteCatalog.AnimKits.cs`  
Audit: `gAAAcha/tools/audit-sprites.ps1`. Play Mode: F8.

All frames below are **128×128** canvas, 8-bit RGBA (PNG color type 6). Pivot in game: bottom-center. Facing is camera-projected 8-dir.

## Pose classes

| Class | Target opaque height | Folders | Align |
|-------|---------------------:|---------|-------|
| **stand** | 118px | idle, run, combat rotations, attacks, hurt, emotes | feet on 2px ground pad |
| **sit** | 84px (~70% stand) | `idle_sitting` | seat/feet on ground |
| **fall** | 88px max, width-capped | `die_death_frame` | body on ground |

Do not scale each frame alone (causes bob). One scale per facing folder.

## Used in game (wired clips)

| When | Clip | Folder | Facings | Pose | Notes |
|------|------|--------|---------|------|-------|
| Peaceful idle | Idle | `idle_rotation_breathing` | 8 | stand | 4-frame breath; fallback `idle_rotation_calm` (1 still each) |
| Peaceful run | Run | `idle_running` | 8 (`east2` = SE) | stand | Unarmed walk/run |
| Combat idle / bow | Idle | `holding_a_longbow_h/rotations` | 8 stills | stand | Haupt contains `bow` |
| Combat run / bow | Run | `…/bow_idle_running` | 8 | stand | |
| Shot / bow AA | Attack | `…/bow_idle_shooting` | 8 | stand | |
| Combat idle / daggers | Idle | `holding_daggers_due/rotations` | 8 stills | stand | Haupt contains `dagger` |
| Combat run / daggers | Run | `…/daggers_idle_running` | 8 | stand | west pack is narrower |
| Dagger AA | Attack | `…/daggers_idle_runstartframe` | 6 | stand | |
| Combat idle / staff | Idle | `holding_staff_long/rotations_staff` | 8 stills | stand | + optional `idle_breathing` |
| Combat run / staff | Run | `…/running_foeward_running_while_holding_thwo_handed` | 8 | stand | |
| Staff AA / skill | Attack | `…/staffskill1` | 3 | stand | |
| Combat idle / sword | Idle | `holding_zweihander/rotations_zweihander` | 8 stills | stand | Haupt contains `sword` |
| Combat run / sword | Run | `…/idle_running_zweihander` | 8 nested | stand | |
| Sword AA | Attack | `…/swordattacks/autoattack_sword1` | NE, SE | stand | others flip/fallback |
| Hurt | Hurt | `idle_dmg_frame` | N, S | stand | |
| Death | Death | `die_death_frame` | 8 | **fall** | not stretched to standing height |
| Pickup | Pickup | `idle_pickup` | N, S | stand | |
| Sit / Rest | Emote | `idle_sitting` | N/E/S/W | **sit** | Rest skill; Shift+4 still plays sit |
| Level-up / Powerup | Emote | `south_powerup` | 1 | stand | |
| Shift+1–3 | Emote | `emotions/emo_*` | 1 | stand | |
| Sword/staff emotes | Emote | `sword_emotion/*`, `staff_emo_victory` | 1 | stand | gated on mainhand |

Sword skills that are wired (shockwave, hook, slash, war cry, iron stance, rally, decoy) live under `holding_zweihander/animations_zweihander/swordattacks/` — see `player-animations.md`. Unused extra swings stay on disk, not in the hotbar.

## Typical content sizes (after pose pass)

Opaque bbox of the **tallest/widest frame in that folder**, then shared scale into 128.

| Folder | Pose | Bbox (max W×H) | Scale |
|--------|------|----------------|------:|
| `idle_rotation_calm` | stand | 40×119 | ~0.99 |
| `idle_running/south` | stand | ~120 tall | ~0.95 |
| `holding_zweihander/rotations_zweihander` | stand | ~120 | ~1.02→0.95 |
| `idle_sitting/*` | sit | ~80×124 (was standing-tall) | **~0.68** → ~84px |
| `die_death_frame/*` | fall | ~75–114 × 99–124 | **~0.71–0.89** → ≤88px |

Full table: `tools/player-sprite-folder-sizes.csv`.

## Restore notes (Phase 1)

GDI `Bitmap.Save(Png)` was used for the first 128 pass. Unity `LoadImage` can treat those as empty while `HasArtSprite` still sees frame counts → **invisible bodies, no shape fallback**. Enemies share `ApplyUnlit` / `BindSpriteTexture`; a tick throw on the player skipped the rest of the entity loop.

Fixes in client: skip fully transparent textures; force `_BaseColor` white on the sprite MPB; catch anim exceptions per entity; rewrite pack with **WPF PngBitmapEncoder**.

## Equipment UI (live)

Inventory (I) paperdoll: Mainhand, Offhand, Body, Helm, Boots, Gloves, Acc, Fairy. Drag matching types; RMB equip from bag / unequip from slot. Class debug dropdown is on the same panel. N no longer swaps two mains.
