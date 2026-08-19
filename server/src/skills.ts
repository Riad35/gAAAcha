import { classById, defaultClass, skillById } from "./data.js";
import type { PlayerSession, ServerMessage } from "./types.js";

/** Core skills granted at character create. */
export function starterSkillsFor(classId: string): string[] {
  const cls = classById(classId) ?? defaultClass;
  // Adventurer is the default test class — unlock full kit so hotkeys work in gray-box.
  if (cls.id === "adventurer") {
    return [...new Set(["auto_attack", ...cls.skillIds])];
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
  return cls.skillIds.filter((id) => !session.unlockedSkillIds.includes(id));
}

export function unlockSkill(session: PlayerSession, skillId: string): { error?: ServerMessage; ok?: true } {
  if (session.unlockedSkillIds.includes(skillId)) {
    return { error: { type: "error", code: "already_unlocked", message: "Already unlocked" } };
  }
  const cls = classById(session.classId) ?? defaultClass;
  if (!cls.skillIds.includes(skillId) || !skillById(skillId)) {
    return { error: { type: "error", code: "bad_skill", message: "Not in your class tree" } };
  }
  if (session.skillPoints < 1) {
    return { error: { type: "error", code: "no_points", message: "No skill points" } };
  }
  session.skillPoints -= 1;
  session.unlockedSkillIds.push(skillId);
  return { ok: true };
}

export function skillTreeSnapshot(session: PlayerSession): ServerMessage {
  return {
    type: "sync_skills",
    skillIds: session.unlockedSkillIds,
    skillPoints: session.skillPoints,
    unlockable: unlockableSkills(session),
  };
}
