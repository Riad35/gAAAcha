# Attacks / Skills Reference

Source of truth: `gAAAcha/server/data/skills.json`  
Hotkeys live in `NetworkBootstrap.cs`.

## Adventurer kit (L1–20, 8 skills incl. AA)

| Key | Skill id | Name | Weapon slot |
|-----|----------|------|-------------|
| Space | `auto_attack` | Auto Attack | 1 Hauptwaffe |
| 1 | `shot` | Shot | 2 Sekundärwaffe |
| 2 | `shockwave` | Shockwave | 1 |
| 3 | `dash` | Dash | — |
| 4 | `rally` | Rally | — |
| 5 | `hook_shot` | Hook Shot | 1 (far=pull, near=AoE shove) |
| 6 | `mend` | Mend | — (+MP on cast) |
| 7 | `decoy` | Decoy | — (next hit 80% DR) |

**N** = swap Haupt ↔ Sekundär. HUD chips (AA W1 / N W2) show the drawn pair; Shot greys if slot 2 is empty. Class cards require **level ≥ 20**.

Hotbar shows name, MP, W1/W2, and cooldown sweep. Hover a slot for the full tooltip. Cast bar sits above the bar while a skill plays.

See `gatcha/adventurer_class_prompt.md` for locked design.

## Targeting

| Input | Effect |
|-------|--------|
| **Tab** | Lock closest enemy |
| **Click** enemy | Lock that enemy |
| **Double-click** empty ground | Clear lock |
| Skillshots / ground (1 / 2) | Cast toward lock; aim mode if no target |

## Other class skills

Fighter/Mage/Marksman/Rogue still use their own `skillIds` in `classes.json` (slash, shove, pull, etc.) after class change.
