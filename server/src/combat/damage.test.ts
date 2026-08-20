import assert from "node:assert/strict";
import { test } from "node:test";
import { loadCombatConfig } from "./config.js";
import { elementRelation, resolveDamage, toCombatElement } from "./damage.js";

function seq(values: number[]): () => number {
  let i = 0;
  return () => values[Math.min(i++, values.length - 1)]!;
}

const attacker = {
  atk: 20,
  matk: 10,
  critRate: 0.05,
  critDamage: 1.5,
  hitRate: 1,
};

const defender = {
  def: 0,
  mdef: 0,
  dodgeRate: 0,
  elementalResist: {} as Record<string, number>,
  element: "fire" as const,
};

test("legacy holy/dark map to light/shadow", () => {
  assert.equal(toCombatElement("holy"), "light");
  assert.equal(toCombatElement("dark"), "shadow");
  assert.equal(toCombatElement("water"), "water");
});

test("water beats fire; fire is weak to water", () => {
  const cfg = loadCombatConfig();
  assert.equal(elementRelation("water", "fire", cfg), "advantage");
  assert.equal(elementRelation("fire", "water", cfg), "disadvantage");
  assert.equal(elementRelation("light", "shadow", cfg), "advantage");
  assert.equal(elementRelation("shadow", "light", cfg), "advantage");
  assert.equal(elementRelation("water", "wind", cfg), "neutral");
});

test("miss deals 0 and skips floor", () => {
  const result = resolveDamage({
    attacker: { ...attacker, hitRate: 0.5 },
    defender: { ...defender, dodgeRate: 0, element: "none" },
    skill: { damageType: "physical", baseDamageMultiplier: 1, flatDamage: 10, element: "none" },
    rng: seq([0.99, 0, 0.5]),
  });
  assert.equal(result.missed, true);
  assert.equal(result.damage, 0);
});

test("landed hit floors at 1 and applies variance midpoint", () => {
  const result = resolveDamage({
    attacker: { ...attacker, critRate: 0 },
    defender: { ...defender, def: 0, element: "none" },
    skill: { damageType: "physical", baseDamageMultiplier: 1, flatDamage: 4, element: "none" },
    rng: seq([0, 0.5]),
  });
  assert.equal(result.missed, false);
  assert.equal(result.crit, false);
  // base 20+4 = 24, no mit, variance 1.0
  assert.equal(result.damage, 24);
});

test("advantage raises damage; resistance is halved", () => {
  const base = resolveDamage({
    attacker: { ...attacker, critRate: 0 },
    defender: { ...defender, element: "earth", elementalResist: { water: 0.4 } },
    skill: { damageType: "physical", baseDamageMultiplier: 0, flatDamage: 100, element: "water" },
    rng: seq([0, 0.5]),
  });
  // water vs earth = disadvantage (-15%), res 0.4 → 100 * 0.85 * 0.6 = 51
  assert.equal(base.advantage, "disadvantage");
  assert.equal(base.damage, 51);

  const adv = resolveDamage({
    attacker: { ...attacker, critRate: 0 },
    defender: { ...defender, element: "fire", elementalResist: { water: 0.4 } },
    skill: { damageType: "physical", baseDamageMultiplier: 0, flatDamage: 100, element: "water" },
    rng: seq([0, 0.5]),
  });
  // +25%, res 0.4 * 0.5 = 0.2 → 100 * 1.25 * 0.8 = 100
  assert.equal(adv.advantage, "advantage");
  assert.equal(adv.damage, 100);
});

test("crit multiplies after element and before mitigation", () => {
  const result = resolveDamage({
    attacker: { ...attacker, critRate: 1, critDamage: 1.5, atk: 0 },
    defender: { ...defender, def: 0, element: "none" },
    skill: { damageType: "physical", baseDamageMultiplier: 1, flatDamage: 10, element: "none" },
    rng: seq([0, 0.5]),
  });
  assert.equal(result.crit, true);
  assert.equal(result.damage, 15);
});

test("true damage skips mitigation", () => {
  const phys = resolveDamage({
    attacker: { ...attacker, critRate: 0, atk: 0 },
    defender: { ...defender, def: 50, element: "none" },
    skill: { damageType: "physical", baseDamageMultiplier: 1, flatDamage: 20, element: "none" },
    rng: seq([0, 0.5]),
  });
  const tr = resolveDamage({
    attacker: { ...attacker, critRate: 0, atk: 0 },
    defender: { ...defender, def: 50, element: "none" },
    skill: { damageType: "true", baseDamageMultiplier: 1, flatDamage: 20, element: "none" },
    rng: seq([0, 0.5]),
  });
  assert.ok(tr.damage > phys.damage);
  assert.equal(tr.damage, 20);
});
