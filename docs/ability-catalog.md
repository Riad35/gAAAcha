# Ability catalog

Source of truth: [`server/data/skills.json`](../server/data/skills.json)  
Class trees: [`server/data/classes.json`](../server/data/classes.json)  
Runtime push: `sync_skills.catalog`  
Update this file whenever you add or retarget a skill.

Unlock: class cards and debug toggle at **level 10**. Adventurer starts with AA, Shot, Rest, Powerup; the rest of the Adventurer bar unlocks on the old L3–17 curve.

## Pose / clip map

| Situation | Clip folder |
|-----------|-------------|
| Peaceful idle | `idle_rotation_calm` (8 stills: north, north_east, east, …) |
| Peaceful run | `idle_base` `idle_running` |
| Combat + weapon | that weapon’s `rotations_*` stills / run |
| Combat, no mainhand | same calm stills |
| Pickup | `idle_pickingup_animation` |
| Took damage | `idle_taking_dmg` |
| Rest | `idle_sitting` |
| Death | `idle_falling_death` then map spawn |
| Powerup | `emo_zweihand_getting_pumped` |

## Adventurer

| id | name | targeting | radius | clip |
|----|------|-----------|--------|------|
| auto_attack | Auto Attack | UNIT_TARGET | — | weapon AA (`zweih_autoattack` / bow shoot / staff chant / dagger run) |
| shot | Shot | SKILLSHOT_LINEAR | — | bow shoot (mainhand) |
| rest | Rest | NO_TARGET | — | sitting; 2% max HP/s; cancel on move/cast/damage |
| powerup | Powerup | NO_TARGET | — | south_powerup |
| shockwave | Shockwave | GROUND_CIRCLE | 2.5 | `zweih_windcutter` |
| dash | Dash | NO_TARGET | — | `zweih_sworddance` (sword) / else AA |
| rally | Rally | caster AoE friendly | 4 | `zweih_buff_ready_up` |
| hook_shot | Hook Shot | UNIT_TARGET | 1.8 splash | `zweih_blows_of_fury` |
| mend | Mend | ALLY_TARGET | — | staff/heal |
| decoy | Decoy | NO_TARGET | — | sword buff |

## Warrior (`fighter`)

Inherits: auto_attack, dash, shockwave, rally. Kit: slash, war_cry, iron_stance, shove. **AoE:** cleave.

| id | name | targeting | radius | clip |
|----|------|-----------|--------|------|
| slash | Slash | UNIT_TARGET | — | `zweih_focus_slash` |
| cleave | Cleave | GROUND_CIRCLE | 2.4 | `zweih_swordwhirl` |
| war_cry | War Cry | NO_TARGET | — | `zweih_buff_warcry` |
| iron_stance | Iron Stance | NO_TARGET | — | `zweih_block` |
| shove | Shove | UNIT_TARGET | — | `zweih_pike` |
| rest | Rest | NO_TARGET | — | sitting |

## Archer (`marksman`)

Inherits: auto_attack, dash, shot, hook_shot. Kit: haste, pull. **AoE:** arrow_rain.

| id | name | targeting | radius | clip |
|----|------|-----------|--------|------|
| arrow_rain | Arrow Rain | GROUND_CIRCLE | 2.8 | bow shoot |
| haste | Haste | NO_TARGET | — | self |
| pull | Gravity Hook | UNIT_TARGET | — | pull |
| rest | Rest | NO_TARGET | — | sitting |

## Mage

Inherits: auto_attack, mend. Kit: stun_bolt, ember_dot, ward, power_chant. **AoE:** arcane_nova.

| id | name | targeting | radius | clip |
|----|------|-----------|--------|------|
| arcane_nova | Arcane Nova | GROUND_CIRCLE | 2.6 | staff skill |
| stun_bolt | Stun Bolt | SKILLSHOT_LINEAR | — | staff |
| ember_dot | Ember Brand | UNIT_TARGET | — | staff |
| ward | Arcane Ward | NO_TARGET | — | self |
| power_chant | Power Chant | NO_TARGET | — | self |
| rest | Rest | NO_TARGET | — | sitting |

## Rogue

Inherits: auto_attack, dash, decoy. Kit: slash, haste, blind_dust. **AoE:** knife_fan.

| id | name | targeting | radius | clip |
|----|------|-----------|--------|------|
| knife_fan | Knife Fan | SKILLSHOT_CONE | 70° | dagger AA |
| slash | Slash | UNIT_TARGET | — | dagger/sword |
| haste | Haste | NO_TARGET | — | self |
| blind_dust | Blind Dust | SKILLSHOT_CONE | 60° | cone |
| rest | Rest | NO_TARGET | — | sitting |
