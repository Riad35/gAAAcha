import { mkdirSync, readFileSync, writeFileSync, existsSync, unlinkSync, readdirSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import type { GuestSave } from "./persist.js";

export type CharSummary = {
  slotIndex: number;
  characterId: string | null;
  name: string | null;
  classId: string | null;
  level: number;
  mapId: string | null;
  empty: boolean;
};

const saveDir = join(dirname(fileURLToPath(import.meta.url)), "..", ".runtime", "saves");

function ensureDir(): void {
  if (!existsSync(saveDir)) {
    mkdirSync(saveDir, { recursive: true });
  }
}

function safeToken(token: string): string {
  return token.replace(/[^a-zA-Z0-9_-]/g, "").slice(0, 64) || "guest";
}

function slotPath(token: string, slot: number): string {
  return join(saveDir, `${safeToken(token)}_s${slot}.json`);
}

export function loadCharSlot(token: string, slot: number): GuestSave | null {
  if (!token || slot < 0 || slot > 7) {
    return null;
  }
  ensureDir();
  const file = slotPath(token, slot);
  if (!existsSync(file)) {
    // legacy single-file save maps to slot 0
    if (slot === 0) {
      const legacy = join(saveDir, `${safeToken(token)}.json`);
      if (existsSync(legacy)) {
        try {
          const data = JSON.parse(readFileSync(legacy, "utf8")) as GuestSave;
          data.slotIndex = 0;
          return data;
        } catch {
          return null;
        }
      }
    }
    return null;
  }
  try {
    const data = JSON.parse(readFileSync(file, "utf8")) as GuestSave;
    data.slotIndex = slot;
    return data;
  } catch {
    return null;
  }
}

export function saveCharSlot(data: GuestSave, slot: number): void {
  ensureDir();
  data.slotIndex = slot;
  data.updatedAt = Date.now();
  writeFileSync(slotPath(data.guestToken, slot), JSON.stringify(data, null, 2));
}

export function deleteCharSlot(token: string, slot: number): boolean {
  const file = slotPath(token, slot);
  if (existsSync(file)) {
    unlinkSync(file);
    return true;
  }
  return false;
}

export function listCharSlots(token: string): CharSummary[] {
  const out: CharSummary[] = [];
  for (let i = 0; i < 8; i += 1) {
    const save = loadCharSlot(token, i);
    if (!save || !save.charNameSet) {
      out.push({
        slotIndex: i,
        characterId: null,
        name: null,
        classId: null,
        level: 1,
        mapId: null,
        empty: true,
      });
    } else {
      out.push({
        slotIndex: i,
        characterId: save.characterId ?? `file_${i}`,
        name: save.name ?? "Adventurer",
        classId: save.classId,
        level: save.level ?? 1,
        mapId: save.mapId ?? null,
        empty: false,
      });
    }
  }
  return out;
}

export const SERVER_LIST = [
  { id: "local", name: "Local Dev", host: "127.0.0.1", port: 7777, status: "online" as const },
  { id: "ashen-1", name: "Ashen Realm 1", host: "127.0.0.1", port: 7777, status: "online" as const },
];
