import type { CombatConfig, CombatDamageType, CombatElement } from "./types.js";
import { loadCombatConfig } from "./config.js";

export type DamageRng = () => number;

export type DamageAttacker = {
  atk: number;
  matk: number;
  critRate: number;
  critDamage: number;
  /** 1+ with dodge 0 skips the miss roll (gray-box default). */
  hitRate: number;
};

export type DamageDefender = {
  def: number;
  mdef: number;
  dodgeRate: number;
  /** 0–1 (legacy 0–100 resist should be divided by 100 before passing). */
  elementalResist: Partial<Record<CombatElement, number>>;
  element?: CombatElement;
};

export type DamageSkill = {
  damageType: CombatDamageType;
  baseDamageMultiplier: number;
  flatDamage: number;
  element: CombatElement;
};

export type DamageInput = {
  attacker: DamageAttacker;
  defender: DamageDefender;
  skill: DamageSkill;
  /** Extra multiplier (spirit / status) applied to raw before the pipeline. */
  extraMult?: number;
  config?: CombatConfig;
  rng: DamageRng;
};

export type ElementRelation = "advantage" | "disadvantage" | "neutral";

export type DamageResult = {
  damage: number;
  missed: boolean;
  crit: boolean;
  element: CombatElement;
  advantage: ElementRelation;
  resistHint: number;
};

const LEGACY_TO_COMBAT: Record<string, CombatElement> = {
  wind: "wind",
  fire: "fire",
  water: "water",
  earth: "earth",
  holy: "light",
  dark: "shadow",
  light: "light",
  shadow: "shadow",
  none: "none",
};

export function toCombatElement(raw: string | undefined | null): CombatElement {
  if (!raw) {
    return "none";
  }
  return LEGACY_TO_COMBAT[raw] ?? "none";
}

export function elementRelation(
  attack: CombatElement,
  defender: CombatElement | undefined,
  config: CombatConfig,
): ElementRelation {
  if (!defender || attack === "none" || defender === "none") {
    return "neutral";
  }
  for (const pair of config.element.duality) {
    if (pair[0] === attack && pair[1] === defender) {
      return "advantage";
    }
  }
  const cycle = config.element.cycle;
  const ai = cycle.indexOf(attack);
  const di = cycle.indexOf(defender);
  if (ai < 0 || di < 0) {
    return "neutral";
  }
  const beats = cycle[(ai + 1) % cycle.length];
  const weakTo = cycle[(ai - 1 + cycle.length) % cycle.length];
  if (defender === beats) {
    return "advantage";
  }
  if (defender === weakTo) {
    return "disadvantage";
  }
  return "neutral";
}

function clamp(n: number, min: number, max: number): number {
  return Math.max(min, Math.min(max, n));
}

/**
 * Combat-rules.md §1–4: hit → element → crit → mitigation → variance → floor.
 * Miss/dodge deals 0 and skips the floor.
 */
export function resolveDamage(input: DamageInput): DamageResult {
  const config = input.config ?? loadCombatConfig();
  const { attacker, defender, skill, rng } = input;
  const extraMult = input.extraMult ?? 1;
  const element = skill.element;

  if (skill.damageType === "none") {
    return { damage: 0, missed: false, crit: false, element, advantage: "neutral", resistHint: 0 };
  }

  const skipHitRoll = attacker.hitRate >= 1 && defender.dodgeRate <= 0;
  const pHit = skipHitRoll
    ? 1
    : clamp(attacker.hitRate - defender.dodgeRate, config.hitChanceMin, config.hitChanceMax);
  if (!skipHitRoll && rng() >= pHit) {
    return { damage: 0, missed: true, crit: false, element, advantage: "neutral", resistHint: 0 };
  }

  let base = 0;
  if (skill.damageType === "physical") {
    base = attacker.atk * skill.baseDamageMultiplier;
  } else if (skill.damageType === "magic") {
    base = attacker.matk * skill.baseDamageMultiplier;
  } else if (skill.damageType === "true") {
    base = (attacker.atk + skill.flatDamage) * skill.baseDamageMultiplier;
  }
  let raw = (base + (skill.damageType === "true" ? 0 : skill.flatDamage)) * extraMult;

  const advantage = elementRelation(element, defender.element, config);
  if (advantage === "advantage") {
    raw *= 1 + config.element.advantageDamage;
  } else if (advantage === "disadvantage") {
    raw *= 1 + config.element.disadvantageDamage;
  }

  const rawRes = defender.elementalResist[element] ?? 0;
  const effectiveRes = clamp(
    advantage === "advantage" ? rawRes * config.element.advantageResMul : rawRes,
    0,
    config.element.resClampMax,
  );
  raw *= 1 - effectiveRes;

  const crit = rng() < attacker.critRate;
  if (crit) {
    raw *= attacker.critDamage > 0 ? attacker.critDamage : config.critDamageDefault;
  }

  if (skill.damageType !== "true") {
    const mitStat = skill.damageType === "magic" ? defender.mdef : defender.def;
    const resistFactor = mitStat / (mitStat + config.mitigationK);
    raw *= 1 - resistFactor;
  }

  const span = config.damageVarianceMax - config.damageVarianceMin;
  raw *= config.damageVarianceMin + rng() * span;

  const damage = Math.max(1, Math.floor(raw));
  return { damage, missed: false, crit, element, advantage, resistHint: effectiveRes };
}
