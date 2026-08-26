import { classById, defaultClass, skillById } from "./data.js";
import type { PlayerSession, ServerMessage } from "./types.js";

/** Adventurer L1–20 curve (P0-04). */
export const ADVENTURER_UNLOCK_LEVEL: Record<string, number> = {
  auto_attack: 1,
  shot: 1,
  rest: 1,
  powerup: 1,
  shockwave: 3,
  dash: 5,
  rally: 8,
  hook_shot: 11,
  mend: 14,
  decoy: 17,
};

export function unlockLevelOf(skillId: string, classId: string): number {
  const fromData = skillById(skillId)?.unlockLevel;
  if (fromData != null) {
    return fromData;
  }
  if (classId === "adventurer") {
    return ADVENTURER_UNLOCK_LEVEL[skillId] ?? 1;
  }
  return 1;
}

/** Core skills granted at character create / class change. */
export function starterSkillsFor(classId: string): string[] {
  const cls = classById(classId) ?? defaultClass;
  const starters = cls.skillIds.filter((id) => unlockLevelOf(id, cls.id) <= 1);
  if (!starters.includes("auto_attack") && cls.skillIds.includes("auto_attack")) {
    starters.unshift("auto_attack");
  }
  return starters.length ? starters : ["auto_attack"];
}

export function unlockableSkills(session: PlayerSession): string[] {
  const cls = classById(session.classId) ?? defaultClass;
  return cls.skillIds.filter((id) => {
    if (session.unlockedSkillIds.includes(id)) {
      return false;
    }
    return session.level >= unlockLevelOf(id, cls.id);
  });
}

export function unlockSkill(session: PlayerSession, skillId: string): { error?: ServerMessage; ok?: true } {
  if (session.unlockedSkillIds.includes(skillId)) {
    return { error: { type: "error", code: "already_unlocked", message: "Already unlocked" } };
  }
  const cls = classById(session.classId) ?? defaultClass;
  if (!cls.skillIds.includes(skillId) || !skillById(skillId)) {
    return { error: { type: "error", code: "bad_skill", message: "Not in your class tree" } };
  }
  const need = unlockLevelOf(skillId, cls.id);
  if (session.level < need) {
    return {
      error: {
        type: "error",
        code: "level_too_low",
        message: `${skillById(skillId)?.name ?? skillId} unlocks at level ${need}`,
      },
    };
  }
  if (session.skillPoints < 1) {
    return { error: { type: "error", code: "no_points", message: "No skill points" } };
  }
  session.skillPoints -= 1;
  session.unlockedSkillIds.push(skillId);
  return { ok: true };
}

export function skillTreeSnapshot(session: PlayerSession): ServerMessage {
  const cls = classById(session.classId) ?? defaultClass;
  const unlockLevels: Record<string, number> = {};
  const catalog: NonNullable<Extract<ServerMessage, { type: "sync_skills" }>["catalog"]> = [];
  for (const id of cls.skillIds) {
    unlockLevels[id] = unlockLevelOf(id, cls.id);
    const def = skillById(id);
    if (!def) {
      continue;
    }
    catalog.push({
      id: def.id,
      name: def.name,
      manaCost: def.manaCost,
      weaponSlot: def.weaponSlot ?? 0,
      cooldownMs: def.cooldownMs,
      targetingType: def.targetingType,
      affects: def.affects,
      aoeOrigin: def.aoeOrigin,
      range: def.range,
      aoeRadius: def.aoeRadius,
    });
  }
  return {
    type: "sync_skills",
    skillIds: session.unlockedSkillIds,
    skillPoints: session.skillPoints,
    unlockable: unlockableSkills(session),
    unlockLevels,
    classSkillIds: [...cls.skillIds],
    catalog,
  };
}
