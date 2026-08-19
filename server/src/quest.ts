import { questById, quests } from "./data.js";
import { addItem, removeItem } from "./shop.js";
import type { PlayerSession, QuestDef, QuestProgress, ServerMessage } from "./types.js";

export function questSnapshot(session: PlayerSession): ServerMessage {
  return {
    type: "sync_quest",
    quests: session.quests,
    completedQuestIds: session.completedQuestIds,
  };
}

export function questsForNpc(session: PlayerSession, npcId: string): {
  quest: QuestDef;
  state: "available" | "active" | "ready" | "done";
}[] {
  const out: { quest: QuestDef; state: "available" | "active" | "ready" | "done" }[] = [];
  for (const quest of quests) {
    if (quest.giverNpcId !== npcId && quest.turnInNpcId !== npcId) {
      continue;
    }
    if (session.completedQuestIds.includes(quest.id)) {
      out.push({ quest, state: "done" });
      continue;
    }
    const active = session.quests.find((q) => q.questId === quest.id);
    if (!active) {
      if (quest.giverNpcId === npcId) {
        out.push({ quest, state: "available" });
      }
      continue;
    }
    if (isReadyToTurnIn(session, quest, active) && quest.turnInNpcId === npcId) {
      out.push({ quest, state: "ready" });
    } else {
      out.push({ quest, state: "active" });
    }
  }
  return out;
}

function isReadyToTurnIn(session: PlayerSession, quest: QuestDef, progress: QuestProgress): boolean {
  if (progress.completed) {
    return true;
  }
  const step = quest.steps[progress.stepIndex];
  if (!step) {
    return true;
  }
  if (step.kind === "deliver") {
    const have = session.inventory.find((s) => s.itemId === step.itemId)?.quantity ?? 0;
    return have >= step.count;
  }
  return progress.progress >= step.count && progress.stepIndex >= quest.steps.length - 1;
}

export function acceptQuest(session: PlayerSession, questId: string): { error?: ServerMessage } {
  const quest = questById(questId);
  if (!quest) {
    return { error: { type: "error", code: "bad_quest", message: "Unknown quest" } };
  }
  if (session.completedQuestIds.includes(questId) || session.quests.some((q) => q.questId === questId)) {
    return { error: { type: "error", code: "quest_owned", message: "Already have this quest" } };
  }
  session.quests.push({ questId, stepIndex: 0, progress: 0, completed: false });
  return {};
}

export function noteTalk(session: PlayerSession, npcId: string): ServerMessage | null {
  let changed = false;
  for (const progress of session.quests) {
    if (progress.completed) {
      continue;
    }
    const quest = questById(progress.questId);
    const step = quest?.steps[progress.stepIndex];
    if (!quest || !step || step.kind !== "talk" || step.npcId !== npcId) {
      continue;
    }
    progress.progress = Math.min(step.count, progress.progress + 1);
    if (progress.progress >= step.count) {
      if (progress.stepIndex < quest.steps.length - 1) {
        progress.stepIndex += 1;
        progress.progress = 0;
      } else {
        progress.completed = true;
      }
    }
    changed = true;
  }
  return changed ? questSnapshot(session) : null;
}

export function noteKill(session: PlayerSession, monsterType: string): ServerMessage | null {
  let changed = false;
  for (const progress of session.quests) {
    if (progress.completed) {
      continue;
    }
    const quest = questById(progress.questId);
    const step = quest?.steps[progress.stepIndex];
    if (!quest || !step || step.kind !== "kill" || step.monsterType !== monsterType) {
      continue;
    }
    progress.progress = Math.min(step.count, progress.progress + 1);
    if (progress.progress >= step.count) {
      if (progress.stepIndex < quest.steps.length - 1) {
        progress.stepIndex += 1;
        progress.progress = 0;
      } else {
        progress.completed = true;
      }
    }
    changed = true;
  }
  return changed ? questSnapshot(session) : null;
}

export function turnInQuest(session: PlayerSession, questId: string): {
  error?: ServerMessage;
  messages: ServerMessage[];
} {
  const quest = questById(questId);
  if (!quest) {
    return { error: { type: "error", code: "bad_quest", message: "Unknown quest" }, messages: [] };
  }
  const progress = session.quests.find((q) => q.questId === questId);
  if (!progress) {
    return { error: { type: "error", code: "no_quest", message: "Quest not active" }, messages: [] };
  }
  if (!isReadyToTurnIn(session, quest, progress) && !progress.completed) {
    return { error: { type: "error", code: "quest_incomplete", message: "Objectives incomplete" }, messages: [] };
  }

  for (const step of quest.steps) {
    if (step.kind === "deliver") {
      if (!removeItem(session, step.itemId, step.count)) {
        return { error: { type: "error", code: "missing_item", message: "Missing delivery item" }, messages: [] };
      }
    }
  }

  session.gold += quest.rewards.gold;
  for (const reward of quest.rewards.items) {
    addItem(session, reward.itemId, reward.quantity);
  }
  session.quests = session.quests.filter((q) => q.questId !== questId);
  session.completedQuestIds.push(questId);

  const towerRewards: Record<string, number> = {
    q_tower_f1: 1,
    q_tower_f2: 2,
    q_tower_f3: 3,
    q_tower_f4: 4,
    q_tower_f5: 5,
  };
  const floor = towerRewards[questId];
  if (floor && floor > session.towerClearedFloor) {
    session.towerClearedFloor = floor;
  }

  return {
    messages: [
      questSnapshot(session),
      { type: "sync_inventory", inventory: session.inventory, gold: session.gold },
      { type: "sync_gold", gold: session.gold },
    ],
  };
}
