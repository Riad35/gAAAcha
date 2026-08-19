# Attacks / Skills Reference

Source of truth: `gAAAcha/server/data/skills.json`  
Hotkeys live in `NetworkBootstrap.cs`.

## Targeting

| Input | Effect |
|-------|--------|
| **Tab** | Lock closest enemy (map-wide, ~64 tiles) |
| **Click** enemy | Lock that enemy |
| **Double-click** empty ground | Clear lock |
| Skillshots (2 / E / 6 / 8) | Cast **immediately** toward lock (or closest). Aim mode only if no target. |

## Hotkeys

| Key | Skill id | Name |
|-----|----------|------|
| Space | `auto_attack` | Auto Attack |
| 1 | `slash` | Slash |
| 2 | `shot` | Shot (skillshot) |
| 3 | `mend` | Mend (self heal) |
| 4 | `shove` | Shove |
| 5 | `pull` | Gravity Hook |
| 6 | `blind_dust` | Blind Dust (cone) |
| 7 | `iron_stance` | Iron Stance (self) |
| 8 | `shockwave` | Shockwave (ground AoE) |
| Q | `dash` | Dash (self) |
| E | `stun_bolt` | Stun Bolt (skillshot) |
| R | `ember_dot` | Ember Brand |
| T | `war_cry` | War Cry (self) |
| U | `power_chant` | Power Chant (self) |
| B | `haste` | Haste (self) |
| O | `barrier` | Barrier (self) |
| P | `ward` | Arcane Ward (self) |
| Y | `elemental_focus` | Elemental Focus (self) |

Also on the on-screen skill bar (same ids).

## Full skill stats

| id | name | dmg | heal | range | CD ms | MP | damageType | targeting |
|----|------|-----|------|-------|-------|----|------------|-----------|
| `auto_attack` | Auto Attack | 4 | 0 | weapon | 800 | 0 | direct | UNIT_TARGET |
| `slash` | Slash | 8 | 0 | 1.5 | 1000 | 0 | direct | UNIT_TARGET |
| `shot` | Shot | 10 | 0 | 5 | 2000 | 8 | direct | SKILLSHOT_LINEAR |
| `mend` | Mend | 0 | 20 | 0 | 4000 | 12 | direct | NO_TARGET (self) |
| `dash` | Dash | 0 | 0 | 3 | 3000 | 10 | direct | NO_TARGET (self move) |
| `stun_bolt` | Stun Bolt | 6 | 0 | 4 | 5000 | — | direct | SKILLSHOT_LINEAR |
| `ember_dot` | Ember Brand | 4 | 0 | 3.5 | 4500 | — | dot | UNIT_TARGET |
| `war_cry` | War Cry | 0 | 0 | 0 | 8000 | — | direct | NO_TARGET (self) |
| `shove` | Shove | 5 | 0 | 1.5 | 3500 | — | direct | UNIT_TARGET |
| `pull` | Gravity Hook | 4 | 0 | 4.5 | 4000 | — | direct | UNIT_TARGET |
| `blind_dust` | Blind Dust | 2 | 0 | 3.5 | 6000 | — | direct | SKILLSHOT_CONE |
| `iron_stance` | Iron Stance | 0 | 0 | 0 | 10000 | — | direct | NO_TARGET (self) |
| `shockwave` | Shockwave | 9 | 0 | 3.5 | 7000 | — | aoe | GROUND_CIRCLE |
| `power_chant` | Power Chant | 0 | 0 | 0 | 9000 | — | direct | NO_TARGET (self) |
| `haste` | Haste | 0 | 0 | 0 | 12000 | — | direct | NO_TARGET (self) |
| `barrier` | Barrier | 0 | 0 | 0 | 14000 | — | direct | NO_TARGET (self) |
| `ward` | Arcane Ward | 0 | 0 | 0 | 14000 | — | direct | NO_TARGET (self) |
| `elemental_focus` | Elemental Focus | 0 | 0 | 0 | 10000 | — | direct | NO_TARGET (self) |

Exact mana costs: see `skills.json`.

## Unlock rules

- **Adventurer** (default): all class skills unlocked on create / login (gray-box).
- Other classes: short starter set + skill-tree unlocks.

## If a skill “does nothing”

Status bar now shows server errors, e.g.:

- `locked_skill` — not unlocked
- `out_of_range` — move closer (melee ~1.5, AA uses weapon range)
- `not_enough_mana` / `on_cooldown`
- `need target` — press **Tab** or click an enemy first
