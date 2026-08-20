import { mkdirSync, readFileSync, writeFileSync, existsSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { isDbReady, loadGuestFromDb, saveGuestToDb } from "./db.js";
import { loadCharSlot, saveCharSlot } from "./chars.js";
import type { InventorySlot, PityCounter, QuestProgress } from "./types.js";
import { log } from "./log.js";

export type GuestSave = {
  guestToken: string;
  characterId?: string;
  slotIndex?: number;
  classId: string;
  name?: string;
  mapId?: string;
  x: number;
  y: number;
  hp: number;
  mp: number;
  inventory: InventorySlot[];
  pity: Record<string, PityCounter>;
  equippedWeaponId?: string;
  equippedWeapon2Id?: string | null;
  weaponIds?: string[];
  equippedSpiritId?: string | null;
  spiritIds?: string[];
  gold?: number;
  homeMapId?: string;
  homeX?: number;
  homeY?: number;
  quests?: QuestProgress[];
  completedQuestIds?: string[];
  charNameSet?: boolean;
  level?: number;
  xp?: number;
  equippedArmorId?: string | null;
  equippedHelmId?: string | null;
  equippedBootsId?: string | null;
  equippedGlovesId?: string | null;
  equippedAccessoryId?: string | null;
  friends?: { guestToken: string; name: string }[];
  skillPoints?: number;
  unlockedSkillIds?: string[];
  classCardId?: string | null;
  equippedSkinId?: string | null;
  towerClearedFloor?: number;
  switchFlags?: Record<string, boolean>;
  updatedAt: number;
};

const saveDir = join(dirname(fileURLToPath(import.meta.url)), "..", ".runtime", "saves");

function ensureDir(): void {
  if (!existsSync(saveDir)) {
    mkdirSync(saveDir, { recursive: true });
  }
}

function pathFor(token: string): string {
  const safe = token.replace(/[^a-zA-Z0-9_-]/g, "").slice(0, 64) || "guest";
  return join(saveDir, `${safe}.json`);
}

/** Sync file load — used by tests and spawnPlayer (slot 0 / legacy). */
export function loadGuest(token: string): GuestSave | null {
  if (!token) {
    return null;
  }
  const slotted = loadCharSlot(token, 0);
  if (slotted) {
    return slotted;
  }
  ensureDir();
  const file = pathFor(token);
  if (!existsSync(file)) {
    return null;
  }
  try {
    return JSON.parse(readFileSync(file, "utf8")) as GuestSave;
  } catch {
    return null;
  }
}

export async function loadGuestPreferDb(token: string, characterId?: string): Promise<GuestSave | null> {
  if (isDbReady()) {
    try {
      const fromDb = await loadGuestFromDb(token, characterId);
      if (fromDb) {
        return fromDb;
      }
    } catch (err) {
      log.warn("PERSIST", "DB load failed (file kept)", { err: (err as Error).message });
    }
  }
  return loadGuest(token);
}

export function saveGuest(data: GuestSave): void {
  data.updatedAt = Date.now();
  const slot = data.slotIndex ?? 0;
  saveCharSlot(data, slot);
  // keep legacy path in sync for slot 0
  if (slot === 0) {
    ensureDir();
    writeFileSync(pathFor(data.guestToken), JSON.stringify(data, null, 2));
  }
  if (isDbReady()) {
    void saveGuestToDb(data).catch((err) => {
      log.warn("PERSIST", "DB save failed (file kept)", { err: (err as Error).message });
    });
  }
}
