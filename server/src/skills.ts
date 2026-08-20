import { classById, defaultClass, skillById } from "./data.js";
import type { PlayerSession, ServerMessage } from "./types.js";

/** Adventurer L1–20 curve (P0-04). */
export const ADVENTURER_UNLOCK_LEVEL: Record<string, number> = {
  auto_attack: 1,
  shot: 1,
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

/** Core skills granted at character create. */
export function starterSkillsFor(classId: string): string[] {
  const cls = classById(classId) ?? defaultClass;
  if (cls.id === "adventurer") {
    return cls.skillIds.filter((id) => unlockLevelOf(id, cls.id) <= 1);
  }
  const core = ["auto_attack"];
  for (const id of cls.skillIds) {
    if (core.includes(id)) {
      continue;
    }
    core.push(id);
    if (core.length >= 4) {
      break;
    }
  }
  return core;
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
