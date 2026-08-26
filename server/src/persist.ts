import { mkdirSync, readFileSync, writeFileSync, existsSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { isDbReady, loadGuestFromDb, saveGuestToDb } from "./db.js";
import { loadCharSlot, saveCharSlot } from "./chars.js";
import type { InventorySlot, PityCounter, PlayerSession, QuestProgress } from "./types.js";
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
  equippedAmuletId?: string | null;
  equippedRing1Id?: string | null;
  equippedRing2Id?: string | null;
  enhanceLevels?: Partial<Record<string, number>>;
  friends?: { guestToken: string; name: string }[];
  skillPoints?: number;
  unlockedSkillIds?: string[];
  classCardId?: string | null;
  equippedSubclassId?: string | null;
  transformed?: boolean;
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

export const AUTOSAVE_MS = 5 * 60 * 1000;

export function toGuestSave(session: PlayerSession): GuestSave {
  return {
    guestToken: session.guestToken,
    characterId: session.characterId,
    slotIndex: session.slotIndex,
    classId: session.classId,
    name: session.entity.name,
    mapId: session.entity.mapId.includes("#")
      ? session.entity.mapId.slice(0, session.entity.mapId.indexOf("#"))
      : session.entity.mapId,
    x: session.entity.x,
    y: session.entity.y,
    hp: session.entity.hp,
    mp: session.entity.mp,
    inventory: session.inventory,
    pity: session.pity,
    equippedWeaponId: session.equippedWeaponId,
    equippedWeapon2Id: session.equippedWeapon2Id,
    weaponIds: session.weaponIds,
    equippedSpiritId: session.equippedSpiritId,
    spiritIds: session.spiritIds,
    gold: session.gold,
    homeMapId: session.homeMapId,
    homeX: session.homeX,
    homeY: session.homeY,
    quests: session.quests,
    completedQuestIds: session.completedQuestIds,
    charNameSet: session.charNameSet,
    level: session.level,
    xp: session.xp,
    equippedArmorId: session.equippedArmorId,
    equippedHelmId: session.equippedHelmId,
    equippedBootsId: session.equippedBootsId,
    equippedGlovesId: session.equippedGlovesId,
    equippedAccessoryId: session.equippedAmuletId ?? session.equippedAccessoryId,
    equippedAmuletId: session.equippedAmuletId ?? session.equippedAccessoryId,
    equippedRing1Id: session.equippedRing1Id ?? null,
    equippedRing2Id: session.equippedRing2Id ?? null,
    enhanceLevels: session.enhanceLevels ?? {},
    friends: session.friends,
    skillPoints: session.skillPoints,
    unlockedSkillIds: session.unlockedSkillIds,
    classCardId: session.classCardId,
    equippedSubclassId: session.equippedSubclassId ?? null,
    transformed: Boolean(session.transformed),
    equippedSkinId: session.equippedSkinId,
    towerClearedFloor: session.towerClearedFloor,
    switchFlags: session.switchFlags,
    updatedAt: Date.now(),
  };
}

export function markSessionDirty(session: PlayerSession): void {
  session.dirty = true;
}

/** Write RAM session to file/DB. Returns false if skipped (lobby or not dirty). */
export function writeSession(session: PlayerSession, onlyIfDirty = false): boolean {
  if (!session.inWorld || !session.charNameSet) {
    return false;
  }
  if (onlyIfDirty && !session.dirty) {
    return false;
  }
  saveGuest(toGuestSave(session));
  session.dirty = false;
  return true;
}

export function flushDirtySessions(sessions: Iterable<PlayerSession>): number {
  let n = 0;
  for (const session of sessions) {
    if (writeSession(session, true)) {
      n += 1;
    }
  }
  return n;
}
