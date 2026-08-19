/**
 * Combat contract types (docs/combat-rules.md).
 * Evolve path: coexist with legacy types in ../types.ts; adapters come in later steps.
 * Legacy Element "holy"/"dark" map to "light"/"shadow" when bridging.
 */

export type CombatElement = "water" | "fire" | "wind" | "earth" | "light" | "shadow" | "none";

export type CombatDamageType = "physical" | "magic" | "true" | "none";

export type TargetingMode =
  | "self"
  | "single-target"
  | "skillshot-line"
  | "skillshot-cone"
  | "ground-AoE";

export type UnitCombatState =
  | "Idle"
  | "Moving"
  | "Targeting"
  | "AutoAttacking"
  | "CastingSkill"
  | "Transformed"
  | "Stunned"
  | "Dead";

export type StatusEffectType = "buff" | "debuff" | "cc";

export type StatusStacking = "none" | "stacks-refresh" | "stacks-independent";

export type CombatUnitStats = {
  hp: number;
  maxHp: number;
  mp: number;
  maxMp: number;
  atk: number;
  matk: number;
  def: number;
  mdef: number;
  critRate: number;
  critDamage: number;
  hitRate: number;
  dodgeRate: number;
  moveSpeed: number;
  attackSpeed: number;
  /** Per-element resistance 0–1 (or higher; clamped on apply). */
  elementalResist: Partial<Record<CombatElement, number>>;
};

export type CombatUnit = {
  id: string;
  name: string;
  kind: "player" | "monster" | "npc";
  mapId: string;
  x: number;
  y: number;
  hitRadius: number;
  element: CombatElement;
  stats: CombatUnitStats;
  state: UnitCombatState;
  /** Soft/manual lock target id (players). */
  targetId: string | null;
  manualLock: boolean;
  transformGauge: number;
  transformedUntil: number;
  statuses: CombatStatusInstance[];
};

export type CombatSkillDef = {
  id: string;
  displayName: string;
  resourceCost: number;
  cooldown: number;
  castTime: number;
  targetingMode: TargetingMode;
  range: number;
  damageType: CombatDamageType;
  baseDamageMultiplier: number;
  element: CombatElement;
  statusEffectsApplied: string[];
  transformationGaugeGain: number;
  animationTrigger: string;
  isMobileCast: boolean;
};

export type CombatStatusEffectDef = {
  id: string;
  type: StatusEffectType;
  duration: number;
  tickInterval: number;
  stacking: StatusStacking;
  maxStacks: number;
  /** Payload keys interpreted by the resolver (stat mod, DoT, CC flag, etc.). */
  payload: Record<string, number | string | boolean>;
};

export type CombatStatusInstance = {
  defId: string;
  stacks: number;
  until: number;
  nextTickAt: number;
  sourceId: string;
};

/** Queued client intent — never carries resolved damage. */
export type CombatIntent =
  | { type: "move"; unitId: string; x: number; y: number; requestId: string }
  | { type: "set_target"; unitId: string; targetId: string | null; manual: boolean; requestId: string }
  | { type: "cast_skill"; unitId: string; skillId: string; targetId: string | null; aimDx?: number; aimDy?: number; aimX?: number; aimY?: number; requestId: string }
  | { type: "auto_attack"; unitId: string; requestId: string }
  | { type: "activate_transform"; unitId: string; requestId: string };

export type CombatSession = {
  id: string;
  mapId: string;
  createdAt: number;
  updatedAt: number;
  /** Wall-clock ms; Redis TTL mirrors this. */
  expiresAt: number;
  units: Record<string, CombatUnit>;
  intentQueue: CombatIntent[];
  /** Last emitted tick sequence for clients. */
  tickSeq: number;
};

export type CombatConfig = {
  mitigationK: number;
  critDamageDefault: number;
  damageVarianceMin: number;
  damageVarianceMax: number;
  hitChanceMin: number;
  hitChanceMax: number;
  element: {
    advantageDamage: number;
    disadvantageDamage: number;
    advantageResMul: number;
    resClampMax: number;
    cycle: CombatElement[];
    duality: CombatElement[][];
  };
  transform: {
    durationSec: number;
    gaugeFromDamageDealt: number;
    gaugeFromDamageTaken: number;
  };
  tickHz: number;
  sessionTtlSec: number;
};
