# Combat Rules Contract

Shared rules for **server (TypeScript)** and **client prediction (C#)**.  
Not shared code — both sides implement this document independently.  
If prediction and authority disagree, **this file is wrong or one side drifted**; fix the drift, don’t paper over it in netcode.

**Status:** Step 1 approved. Decisions locked (see §15). Step 2 = types + session store scaffolding only.

**Authority:** Server resolves all combat outcomes. Client may predict presentation only; it never finalizes damage, death, gauge fill, or RNG.

---

## 1. Hit resolution order

For every damaging action, resolve in this exact order:

1. **Hit / dodge** — if miss or dodge, deal **0** and stop (no floor).
2. **Elemental modifier** — apply advantage / disadvantage / resistance (see §3).
3. **Crit check** — on crit, multiply by Crit Damage (default **150%** = ×1.5).
4. **Mitigation** — physical uses DEF vs ATK path; magic uses MDEF vs MATK path; true damage skips mitigation.
5. **Final variance** — uniform random in **[0.95, 1.05]** (tunable in config).
6. **Floor** — if the attack landed (not miss/dodge), final damage **≥ 1**.

Heals use a separate path (no hit/dodge/crit unless a skill explicitly says otherwise); floor heal at **1** when a heal is applied.

---

## 2. Stats (per unit)

| Stat | Role |
|------|------|
| HP / Max HP | Vitality |
| MP / Max MP | Skill resource; also feeds transformation gauge indirectly via flagged skills / combat |
| ATK | Physical damage scalar |
| MATK | Magic damage scalar |
| DEF | Physical mitigation |
| MDEF | Magic mitigation |
| Crit Rate | Chance to crit (0–1) |
| Crit Damage | Crit multiplier (default **1.5**) |
| Hit Rate | Chance attack lands |
| Dodge Rate | Chance target evades |
| Move Speed | World units / second |
| Attack Speed | Auto-attack interval scaler (not skill cast time) |
| Elemental Resistance | Per-element flat % reduction (see §3) |

### Mitigation (placeholder, tunable)

```
mitigated = raw * (1 - resistFactor)
resistFactor = mitigationStat / (mitigationStat + K)
K = 50   // config: combat.mitigationK
```

Physical: `mitigationStat = DEF`. Magic: `mitigationStat = MDEF`. True: skip.

### Hit / dodge (placeholder)

```
pHit = clamp(attacker.hitRate - defender.dodgeRate, 0.05, 0.95)  // config bounds
roll U[0,1); if roll >= pHit → miss (0 damage)
```

Crit: `roll U[0,1) < critRate` → apply crit damage multiplier.

---

## 3. Elemental system

### Elements

Primary cycle (each beats the next, weak to the previous):

`Water → Fire → Wind → Earth → Water`

Duality (separate from the cycle):

`Light → Shadow` and `Shadow → Light` (mutual advantage when attacking the opposite).

**Open decision:** ~~verification cycle~~ — **locked:** Water→Fire→Wind→Earth + Light↔Shadow (see §15).

### Modifiers (config keys — do not hardcode in logic)

| Case | Damage | Resistance handling |
|------|--------|---------------------|
| Advantage | **+25%** (`element.advantageDamage`) | Defender’s resistance to that element treated as **half** for this hit |
| Disadvantage | **−15%** (`element.disadvantageDamage`) | Normal resistance |
| Neutral / none | **0%** | Normal resistance |

Resistance after elemental step:

```
effectiveRes = advantage ? resistance * 0.5 : resistance
damage *= (1 - clamp(effectiveRes, 0, 0.75))
```

Config table path (planned): `server/data/combat-config.json` (and mirrored client prediction constants).

---

## 4. Damage from a skill

```
base =
  damageType == physical → ATK * baseDamageMultiplier
  damageType == magic    → MATK * baseDamageMultiplier
  damageType == true     → (ATK or flat skill value per skill data) * baseDamageMultiplier
  damageType == none     → 0

raw = base + skill.flatDamage   // if present; else base only
→ hit/dodge → elemental → crit → mitigation → variance → floor
```

Auto-attack is a skill with `id = auto_attack` (or per-character auto def), `castTime = 0`, resource cost 0, cooldown driven by Attack Speed.

---

## 5. Combat state machine (per unit)

States:

`Idle → Moving → Targeting → AutoAttacking → CastingSkill → Transformed → Stunned/CC'd → Dead`

Rules:

| Rule | Detail |
|------|--------|
| Stun/CC | Interrupts any state except `Dead`; blocks all input except queued movement-cancel |
| CastingSkill | Cast time may be 0; movement locked unless `isMobileCast` |
| Dead | Not targetable; all status timers on that unit **expire/clear** (do not persist) |
| Transitions | Server-validated. Client may predict locally; on reject, roll back cleanly |

`Transformed` is orthogonal to action states in presentation, but server tracks transform buff + Skill 4 availability as combat state flags while the unit is otherwise Idle/Moving/AutoAttacking/CastingSkill.

---

## 6. Targeting

- Soft-lock: click/tap valid enemy → current target + highlight ring.
- Auto-attacks fire at current target when in range, on attack-speed timer, no per-swing click.
- On target death or leave range: auto-retarget nearest valid enemy **unless** manual lock is set (manual lock persists until retarget or invalid).
- Skills use current target by default; modes may override: self / single / skillshot-line / skillshot-cone / ground-AoE.
- Taunt: forces **auto-attacks** onto taunter; skills stay player-directed unless skill flagged to respect taunt.
- **Range is always checked on the server** using positions at process time — client range is UX only.

---

## 7. Skills

### Active skill fields (schema contract)

`id`, `displayName`, `resourceCost` (MP), `cooldown`, `castTime`, `targetingMode` (`self` \| `single-target` \| `skillshot-line` \| `skillshot-cone` \| `ground-AoE`), `range`, `damageType` (`physical` \| `magic` \| `true` \| `none`), `baseDamageMultiplier`, `element`, `statusEffectsApplied[]`, `transformationGaugeGain`, `animationTrigger`, `isMobileCast`.

### Kit shape

Per character: **1 auto-attack**, **3 active skills**, **1 passive**.  
Passives are a **separate lightweight effect type** (stat bonus / on-hit / aura), not active skills with cast/CD.

### Validation

Server on intent: resource, cooldown, valid target/range, not silenced (and not stunned for actions).  
Client pre-validates for UI (grey icons) only.

---

## 8. Transformation

| Rule | Value / behavior |
|------|------------------|
| Gauge | 0–100, separate from MP |
| Fill | Damage dealt (small % of damage), damage taken (smaller %), skills with `transformationGaugeGain` |
| Activate | Explicit player action at **100** only; consumes full gauge |
| Duration | **8–12s** (config: `transform.durationSec`, default **10**) |
| While active | Per-character stat buff; **Skill 4** available; optional AA/skill alterations via **data**, not hardcoded branches |
| End | Duration expiry only → gauge **0**; no separate transform CD |
| CC | Does **not** cancel transform; still blocks actions normally |
| Authority | Client requests; server validates gauge == 100 and confirms |

### Placeholder fill rates (config)

- Dealt: `transform.gaugeFromDamageDealt = 0.02` → `min(100, gauge + damage * rate)`
- Taken: `transform.gaugeFromDamageTaken = 0.01`
- Skill flag: add `transformationGaugeGain` flat points on successful cast

---

## 9. Status effects (data-driven)

Definition fields: `id`, `type` (`buff` \| `debuff` \| `cc`), `duration`, `tickInterval` (0 = non-ticking), `stacking` (`none` \| `stacks-refresh` \| `stacks-independent`), `maxStacks`, payload (stat mod / DoT / HoT / CC flag).

### Minimum set

| Id (example) | Behavior |
|--------------|----------|
| stun | Blocks all actions |
| root | Blocks movement only |
| silence | Blocks skills; AA + move OK |
| slow | % move speed reduction |
| poison / burn | DoT ticks |
| regen | HoT ticks |
| atk_up / def_down | Stat buff/debuff |

Ticks and durations: **server tick only**. Client renders icons/VFX from confirmed state — **never predicts DoT ticks**.

---

## 10. Enemy AI (PvE)

Server-side FSM (AI intents should eventually share the player intent pipeline):

`Idle/Patrol → Aggro → Chase → Attack → Leash/Return`

- Aggro: player in aggro radius or damage dealt.
- Target: nearest player; bosses may **fixate** first aggro.
- Skills: per-type **priority list** (e.g. Skill A if ready + in range, else auto).
- Leash/Return: exceed leash → return home; **HP resets on return**.

Threat table may exist as an enhancement; nearest/fixate is enough for MVP.

---

## 11. Client prediction & reconciliation

- On input: play anim + optional muted provisional float; buffer prediction with **request ID**.
- On server delta: match request ID → confirm or correct.
- Small divergence: short interpolation. Large (e.g. predicted live, server dead): hard correct.
- Logic lives in an isolated module (e.g. `PredictionReconciler`) — not scattered through gameplay.

---

## 12. Session persistence (architecture target)

| Layer | Role |
|-------|------|
| **Redis** | Active combat session state (ephemeral, TTL on abandon) — **target** |
| **PostgreSQL** | Battle end: rewards, stats, currency |

**Gray-box path:** in-process `MemoryCombatSessionStore` implements the same interface as Redis. Real Redis is optional (`REDIS_URL`); not required for local play until multi-process / TTL sessions matter.

Tick loop target: **10–15 Hz** (consume intents → resolve → tick statuses → win/loss → emit deltas).

---

## 13. Config defaults (single place)

Planned file: `server/data/combat-config.json`

```json
{
  "mitigationK": 50,
  "critDamageDefault": 1.5,
  "damageVarianceMin": 0.95,
  "damageVarianceMax": 1.05,
  "hitChanceMin": 0.05,
  "hitChanceMax": 0.95,
  "element": {
    "advantageDamage": 0.25,
    "disadvantageDamage": -0.15,
    "advantageResMul": 0.5,
    "resClampMax": 0.75
  },
  "transform": {
    "durationSec": 10,
    "gaugeFromDamageDealt": 0.02,
    "gaugeFromDamageTaken": 0.01
  },
  "tickHz": 12
}
```

---

## 14. Implementation order (do not skip)

1. **This file** — approved  
2. Server types + session store scaffolding (memory now, Redis-shaped API) ← current  
3. Pure damage resolution function + unit tests  
4. Tick loop + intent queue + deltas  
5. Client ScriptableObject mirrors  
6. Intent send + prediction + reconciliation  
7. Status / CD / transform UI  
8. Enemy AI + minimal 1v1 test scene  

---

## 15. Locked decisions

1. **Element cycle:** Water→Fire→Wind→Earth, plus Light↔Shadow.  
2. **Redis:** Defer live Redis dependency. Step 2 uses a `CombatSessionStore` interface + **in-memory** adapter; Redis adapter is scaffolding (same keys/TTL contract) enabled later via `REDIS_URL`.  
3. **Evolve** existing gray-box (`combat.ts`, `skills.json`, world maps) toward this contract — no greenfield rewrite beside it.