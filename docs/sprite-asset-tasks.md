# Sprite asset tasks

Fill these when new frames land. Runtime already maps every **state × kit** we have in `StreamingAssets/Data/animation_kits.json`. Missing clips stay missing — do not invent art. Drop new PNGs in the listed folder (same names the loader already understands), then re-run `tools/audit-sprites.ps1` and F8.

Live pack: `Assets/_Project/Art/Sprites/player/female` (180×180).  
Calm stills (temporary): `StreamingAssets/Sprites/player/idle_rotation_calm`.

| # | State | Kit | Have now | Need |
|---|-------|-----|----------|------|
| 1 | IDLE | unarmed | 8 calm stills in StreamingAssets | 8 stills in `idle_base/Idle_base/rotations` (and/or `rotations_base`). Then we can delete the leftover StreamingAssets pack. |
| 2 | ATTACK | unarmed | none (uses run) | Fist / punch AA, 8-dir preferred (1-dir OK if it faces the lock). |
| 3 | ATTACK | daggers | none (uses run) | Dagger AA clip, 8-dir preferred. |
| 4 | IDLE | daggers | 7 stills | `rotations_daggers/south.png`. `south-west_0005.png` is leftover, not south. |
| 5 | IDLE | sword | 7 stills | `rotations_zweih/south_east.png` (or `south-east.png`). |
| 6 | DIE | unarmed | 7 dirs | `idle_falling_death/north` (same frame count as the other dirs). |
| 7 | SIT | unarmed | 7 dirs | `idle_sitting/north`. |
| 8 | WALK | staff | 7 dirs | `running_foeward_…/north-west` (or `north_west`). |
| 9 | CAST | staff | 6 dirs | `chanting/east` and `chanting/south-west`. Nested `chanting1` leftover. |
| 10 | PICKUP | unarmed | N + S | Optional east/west/diagonals. Cardinal fallback is fine. |
| 11 | HIT | unarmed | N / S / side | Optional true 8-dir hurt. Extra folder `getting_hit_taking_dmg_crouching_pushback_flinchin` is not wired. |
| 12 | ATTACK | sword | 1-dir `zweih_autoattack` | Optional 8-dir AA. Face-the-lock is already OK. |
| 13 | WALK | all kits | run only | Optional distinct walk (shorter stride). Walk and Run both use the run pack. |
| 14 | — | gun / orb | none | New kits when those weapons exist. Offhand charm stays unarmed/combat of mainhand. |
| 15 | pose | sit / fall | feet-scan pivot | Per-frame origin Y in `sprite_infos.json` so sit/death do not change world height. |

Unused clips already on disk (not tasks — wire later if a skill wants them): `zweih_strike`, `zweih_electric_slash`, `zweih_combo`, `zweih_blance`.

Generator `metadata.json` under each Art stance still points at old 128 paths (`Idle/rotations/…`). Runtime does not read those files. Refresh them only if the exporter needs them.

After a drop: copy into the Art folder above, keep 180×180, facing names `south` … `south-west` (aliases `south_east`, `west2` = east). Then `powershell -NoProfile -File tools/audit-sprites.ps1`.
