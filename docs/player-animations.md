# Player animations

Source pack: `gatcha1/Assets/_Project/Art/Sprites/player/female` (180×180 frames)  
Loader: `SpriteCatalog.PlayerAnims.cs` (Art path first, StreamingAssets fallback)  
**Kit map:** `StreamingAssets/Data/animation_kits.json` + `sprite_infos.json` (loaded at boot)  
**Holes to fill later:** [`sprite-asset-tasks.md`](sprite-asset-tasks.md)  
**Ability catalog:** [`ability-catalog.md`](ability-catalog.md)

## Sprite audit

- Offline: `powershell -NoProfile -File tools/audit-sprites.ps1`  
- Play Mode: **F8** → `SpriteCatalog.AuditWiredClips()`

Contract: `tools/sprite-audit-clips.json`. Facings use mixed names (`south-east`, `south_east`, `west2` = east on zwei run). Missing dirs flip or pick a neighbor.

Walk/run facing is an **8-dir clip from the camera view**. Left/right are swapped so they match the viewpoint.

| Key | Moves on screen | Typical clip (home yaw) |
|-----|-----------------|-------------------------|
| A / Left | left | `east` (pack vs camera) |
| D / Right | right | `west` |
| W / Up | up | `north` |
| S / Down | down | `south` |

## Combat vs peaceful

| State | When | Idle | Run |
|-------|------|------|-----|
| **Peaceful** | No living lock and no monster has you as `ThreatTopId` | `idle_rotation_calm` 8-dir **stills** (Art `rotations` / `rotations_base` if present) | `idle_base/.../idle_running` (8-dir) |
| **Combat** | Living lock, or a living monster’s threat top is you | Equipped weapon **rotation stills** (one pose per facing) | Equipped weapon **run** |
| **Combat, no mainhand** | Fists / empty | Same calm stills | `idle_running` |

Attacks, skills, hurt, death, pickup, and emotes ignore peaceful/combat for clip choice; weapon **gates** still apply.

## Weapon ids → pack

| `HeldWeaponId` contains | Stance | Combat idle | Combat run | Attack |
|-------------------------|--------|-------------|------------|--------|
| `bow` | bow | `rotations_longbow` | `running_forward_running_switfly_elegant_stride` | `loading_bow_with_arrow_shooting_arrow_stable_uprig` |
| `dagger` | daggers | `rotations_daggers` (no south still) | `forward_leaning_very_fast_steps_jumping_running_ru` | same run (no AA clip) |
| `staff` | staff | `rotations_staff` | `running_foeward_running_while_holding_thwo_handed` | `chanting` |
| `sword` | sword | `rotations_zweih` (no SE still) | `running_zweihander` (`west2` = east) | `zweih_autoattack` |
| else | unarmed | `idle_rotation_calm` stills | `idle_running` | `idle_running` (no punch pack) |

Overlay weapon mark is hidden while peaceful, while a drawn-weapon pack is on the body, and during emote/pickup/death.

## Shared clips (`idle_base`)

| Clip | Folder | Facings |
|------|--------|---------|
| Hurt | `idle_taking_dmg` | `facing_north` / `facing_south` / `facing_side` (west flips side) |
| Death | `idle_falling_death` | 7 dirs (no north — NE fallback) |
| Pickup | `idle_pickingup_animation` | `pickingup_north`, `pickingup_south` |
| Peaceful idle | `rotations` / `idle_rotation_calm` | 8 stills (north … south-west), not a clip |
| Peaceful run | `idle_running` | 8 |
| Rest / sit | `idle_sitting` | 7 (no north) |

## Sword skills (`zweihand_skills`, 1-dir except idle/run)

| Skill | Folder |
|-------|--------|
| auto_attack | `zweih_autoattack` |
| slash | `zweih_focus_slash` |
| cleave | `zweih_swordwhirl` |
| shockwave | `zweih_windcutter` |
| hook_shot | `zweih_blows_of_fury` |
| war_cry | `zweih_buff_warcry` |
| iron_stance | `zweih_block` |
| rally | `zweih_buff_ready_up` |
| decoy | `zweih_taunt` |
| shove | `zweih_pike` |
| dash | `zweih_sworddance` |

Unused in the bar: `zweih_strike`, `zweih_electric_slash`, `zweih_combo`, `zweih_blance`.

## Emotes — Shift+1 … Shift+0

Number keys **without** Shift still cast. Rest is the sit skill, not Shift+4.

| Key | Id | Folder | Gate |
|-----|----|--------|------|
| Shift+1 | `emo_scared` | `emotions/emo_scared` | always |
| Shift+2 | `emo_zweih_wink` or scared | `emo_zweihand` | sword wink, else scared |
| Shift+3 | `emo_zweih_bet` or scared | `emo_zweihand` | sword |
| Shift+4 | `idle_sitting` | `idle_sitting` | always |
| Shift+5 | `south_powerup` | `emo_zweihand_getting_pumped` | pumped / staff turnaround / scared |
| Shift+6 | `emo_zwei_readying` or staff turnaround | | sword **or** staff |
| Shift+7 | `emo_zweihand_taunt` | | sword |
| Shift+8 | `emo_zweih_come_get_me` | `emotes_zweih` | sword or staff |
| Shift+9 | `emo_zweih_victory` | | sword |
| Shift+0 | `emo_zweih_hellyeah` | | sword |

Level-up also plays `south_powerup` (pumped clip).

## Direction aliases

Canonical order: south, south-east, east, north-east, north, north-west, west, south-west (0–7).

Also: `south_east`, `west2` (east), `pickingup_north` / `pickingup_south`, `facing_north` / `facing_south` / `facing_side`.

## Files of record

- Pack: `Assets/_Project/Art/Sprites/player/female/**`
- Kits: `Assets/StreamingAssets/Data/animation_kits.json`, poses: `sprite_infos.json`
- Holes: [`sprite-asset-tasks.md`](sprite-asset-tasks.md)
- Resolver: `Assets/_Project/Scripts/World/SpriteCatalog.PlayerAnims.cs` + `SpriteCatalog.AnimKits.cs`
- Combat / emote: `GrayBoxWorld.cs` (`IsInCombat`, `PlayEmoteSlot`)
- Input: `NetworkBootstrap.cs` (`TryPlayEmote`, Shift + 0–9)
