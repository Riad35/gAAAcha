import { mkdirSync, readFileSync, writeFileSync, existsSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import type { InventorySlot, PityCounter } from "./types.js";

export type GuestSave = {
  guestToken: string;
  classId: string;
  x: number;
  y: number;
  hp: number;
  mp: number;
  inventory: InventorySlot[];
  pity: Record<string, PityCounter>;
  equippedWeaponId?: string;
  weaponIds?: string[];
  equippedSpiritId?: string | null;
  spiritIds?: string[];
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

export function loadGuest(token: string): GuestSave | null {
  if (!token) {
    return null;
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

export function saveGuest(data: GuestSave): void {
  ensureDir();
  data.updatedAt = Date.now();
  writeFileSync(pathFor(data.guestToken), JSON.stringify(data, null, 2));
}
