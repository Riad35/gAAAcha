import { createHash, randomBytes, scryptSync, timingSafeEqual } from "node:crypto";
import { readFileSync, existsSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import pg from "pg";
import type { GuestSave } from "./persist.js";
import type { CharSummary } from "./chars.js";
import type { InventorySlot, PityCounter, QuestProgress } from "./types.js";
import { INVENTORY_SIZE } from "./gacha.js";
import { log } from "./log.js";

const { Pool } = pg;

let pool: pg.Pool | null = null;
let dbReady = false;

export function isDbReady(): boolean {
  return dbReady;
}

export async function initDb(): Promise<boolean> {
  const url = process.env.DATABASE_URL?.trim();
  if (!url) {
    log.info("PERSIST", "DATABASE_URL unset — using file saves");
    return false;
  }
  try {
    pool = new Pool({ connectionString: url, max: 4 });
    await pool.query("SELECT 1");
    const schemaPath = join(dirname(fileURLToPath(import.meta.url)), "..", "db", "schema.sql");
    if (existsSync(schemaPath)) {
      await pool.query(readFileSync(schemaPath, "utf8"));
    }
    dbReady = true;
    log.info("PERSIST", "Postgres connected — schema applied");
    return true;
  } catch (err) {
    log.warn("PERSIST", "Postgres unavailable, falling back to file saves", {
      err: (err as Error).message,
    });
    pool = null;
    dbReady = false;
    return false;
  }
}

function hashPassword(password: string, salt?: string): { hash: string; salt: string } {
  const s = salt ?? randomBytes(16).toString("hex");
  const hash = scryptSync(password, s, 32).toString("hex");
  return { hash: `${s}:${hash}`, salt: s };
}

export function verifyPassword(password: string, stored: string): boolean {
  const [salt, hash] = stored.split(":");
  if (!salt || !hash) {
    return false;
  }
  const next = scryptSync(password, salt, 32);
  const prev = Buffer.from(hash, "hex");
  return prev.length === next.length && timingSafeEqual(prev, next);
}

export async function registerAccount(
  username: string,
  password: string,
  guestToken: string,
): Promise<{ ok: true; guestToken: string } | { error: string }> {
  if (!pool) {
    return { error: "db_offline" };
  }
  const clean = username.trim().toLowerCase().slice(0, 24);
  if (clean.length < 3 || password.length < 4) {
    return { error: "bad_credentials" };
  }
  const { hash } = hashPassword(password);
  const token = guestToken || `acc_${createHash("sha256").update(clean + Date.now()).digest("hex").slice(0, 16)}`;
  try {
    await pool.query(
      `INSERT INTO accounts (guest_token, username, password_hash) VALUES ($1, $2, $3)`,
      [token, clean, hash],
    );
    return { ok: true, guestToken: token };
  } catch {
    return { error: "username_taken" };
  }
}

export async function loginAccount(
  username: string,
  password: string,
): Promise<{ ok: true; guestToken: string } | { error: string }> {
  if (!pool) {
    return { error: "db_offline" };
  }
  const clean = username.trim().toLowerCase();
  const res = await pool.query<{ guest_token: string; password_hash: string | null }>(
    `SELECT guest_token, password_hash FROM accounts WHERE username = $1`,
    [clean],
  );
  const row = res.rows[0];
  if (!row?.password_hash || !verifyPassword(password, row.password_hash)) {
    return { error: "bad_login" };
  }
  return { ok: true, guestToken: row.guest_token };
}

function rowToSave(token: string, c: Record<string, unknown>, inventory: InventorySlot[], pity: Record<string, PityCounter>, quests: QuestProgress[]): GuestSave {
  return {
    guestToken: token,
    characterId: String(c.id),
    slotIndex: Number(c.slot_index ?? 0),
    classId: String(c.class_id),
    name: String(c.name),
    mapId: String(c.map_id),
    x: Number(c.x),
    y: Number(c.y),
    hp: Number(c.hp),
    mp: Number(c.mp),
    inventory,
    pity,
    equippedWeaponId: (c.equipped_weapon_id as string) ?? undefined,
    equippedWeapon2Id: (c.equipped_weapon2_id as string) ?? null,
    weaponIds: (c.weapon_ids as string[]) ?? [],
    equippedSpiritId: c.equipped_spirit_id as string | null,
    spiritIds: (c.spirit_ids as string[]) ?? [],
    gold: Number(c.gold),
    homeMapId: String(c.home_map_id),
    homeX: Number(c.home_x),
    homeY: Number(c.home_y),
    quests,
    completedQuestIds: (c.completed_quest_ids as string[]) ?? [],
    charNameSet: Boolean(c.char_name_set),
    level: Number(c.level),
    xp: Number(c.xp),
    skillPoints: Number(c.skill_points ?? 0),
    unlockedSkillIds: (c.unlocked_skill_ids as string[]) ?? [],
    equippedArmorId: (c.equipped_armor_id as string) ?? null,
    equippedHelmId: (c.equipped_helm_id as string) ?? null,
    equippedBootsId: (c.equipped_boots_id as string) ?? null,
    equippedGlovesId: (c.equipped_gloves_id as string) ?? null,
    equippedAccessoryId: (c.equipped_amulet_id as string) ?? (c.equipped_accessory_id as string) ?? null,
    equippedAmuletId: (c.equipped_amulet_id as string) ?? (c.equipped_accessory_id as string) ?? null,
    equippedRing1Id: (c.equipped_ring1_id as string) ?? null,
    equippedRing2Id: (c.equipped_ring2_id as string) ?? null,
    enhanceLevels: (c.enhance_levels as Record<string, number>) ?? {},
    classCardId: (c.class_card_id as string) ?? null,
    equippedSubclassId: (c.equipped_subclass_id as string) ?? null,
    transformed: Boolean(c.transformed),
    towerClearedFloor: Number(c.tower_cleared_floor ?? 0),
    switchFlags: (c.switch_flags as Record<string, boolean>) ?? {},
    updatedAt: Date.now(),
  };
}

export async function listCharactersDb(token: string): Promise<CharSummary[] | null> {
  if (!pool || !token) {
    return null;
  }
  const acc = await pool.query<{ id: string }>(`SELECT id FROM accounts WHERE guest_token = $1`, [token]);
  if (!acc.rows[0]) {
    return Array.from({ length: 8 }, (_, i) => ({
      slotIndex: i,
      characterId: null,
      name: null,
      classId: null,
      level: 1,
      mapId: null,
      empty: true,
    }));
  }
  const rows = await pool.query(
    `SELECT id, slot_index, name, class_id, level, map_id, char_name_set FROM characters WHERE account_id = $1`,
    [acc.rows[0].id],
  );
  const bySlot = new Map<number, (typeof rows.rows)[0]>();
  for (const r of rows.rows) {
    bySlot.set(Number(r.slot_index), r);
  }
  return Array.from({ length: 8 }, (_, i) => {
    const r = bySlot.get(i);
    if (!r || !r.char_name_set) {
      return { slotIndex: i, characterId: null, name: null, classId: null, level: 1, mapId: null, empty: true };
    }
    return {
      slotIndex: i,
      characterId: String(r.id),
      name: String(r.name),
      classId: String(r.class_id),
      level: Number(r.level),
      mapId: String(r.map_id),
      empty: false,
    };
  });
}

export async function deleteCharacterDb(token: string, slotIndex: number): Promise<boolean> {
  if (!pool) {
    return false;
  }
  const acc = await pool.query<{ id: string }>(`SELECT id FROM accounts WHERE guest_token = $1`, [token]);
  if (!acc.rows[0]) {
    return false;
  }
  const res = await pool.query(
    `DELETE FROM characters WHERE account_id = $1 AND slot_index = $2`,
    [acc.rows[0].id, slotIndex],
  );
  return (res.rowCount ?? 0) > 0;
}

export async function loadGuestFromDb(token: string, characterId?: string): Promise<GuestSave | null> {
  if (!pool || !token) {
    return null;
  }
  const acc = await pool.query<{ id: string }>(`SELECT id FROM accounts WHERE guest_token = $1`, [token]);
  if (!acc.rows[0]) {
    return null;
  }
  const accountId = acc.rows[0].id;
  const ch = characterId
    ? await pool.query(`SELECT * FROM characters WHERE account_id = $1 AND id = $2`, [accountId, characterId])
    : await pool.query(`SELECT * FROM characters WHERE account_id = $1 ORDER BY slot_index ASC LIMIT 1`, [accountId]);
  const c = ch.rows[0];
  if (!c) {
    return null;
  }
  const inv = await pool.query<{ slot_index: number; item_id: string | null; quantity: number }>(
    `SELECT slot_index, item_id, quantity FROM inventory_slots WHERE character_id = $1 ORDER BY slot_index`,
    [c.id],
  );
  const inventory: InventorySlot[] = Array.from({ length: INVENTORY_SIZE }, (_, i) => ({
    slotIndex: i,
    itemId: null,
    quantity: 0,
  }));
  for (const row of inv.rows) {
    if (row.slot_index < 0 || row.slot_index >= INVENTORY_SIZE) {
      continue;
    }
    inventory[row.slot_index] = {
      slotIndex: row.slot_index,
      itemId: row.item_id,
      quantity: row.quantity,
    };
  }
  const pityRows = await pool.query<{ banner_id: string; pity: number; total_pulls: number }>(
    `SELECT banner_id, pity, total_pulls FROM pity_counters WHERE account_id = $1`,
    [accountId],
  );
  const pity: Record<string, PityCounter> = {};
  for (const p of pityRows.rows) {
    pity[p.banner_id] = { bannerId: p.banner_id, pity: p.pity, totalPulls: p.total_pulls };
  }
  const questsRows = await pool.query<{ quest_id: string; step_index: number; progress: number; completed: boolean }>(
    `SELECT quest_id, step_index, progress, completed FROM quest_progress WHERE character_id = $1`,
    [c.id],
  );
  const completed = (c.completed_quest_ids as string[]) ?? [];
  const quests: QuestProgress[] = questsRows.rows
    .filter((q) => !completed.includes(q.quest_id))
    .map((q) => ({
      questId: q.quest_id,
      stepIndex: q.step_index,
      progress: q.progress,
      completed: q.completed,
    }));

  return rowToSave(token, c as Record<string, unknown>, inventory, pity, quests);
}

export async function loadGuestSlotFromDb(token: string, slotIndex: number): Promise<GuestSave | null> {
  if (!pool || !token) {
    return null;
  }
  const acc = await pool.query<{ id: string }>(`SELECT id FROM accounts WHERE guest_token = $1`, [token]);
  if (!acc.rows[0]) {
    return null;
  }
  const ch = await pool.query(
    `SELECT * FROM characters WHERE account_id = $1 AND slot_index = $2`,
    [acc.rows[0].id, slotIndex],
  );
  if (!ch.rows[0]) {
    return null;
  }
  return loadGuestFromDb(token, String(ch.rows[0].id));
}

export async function saveGuestToDb(data: GuestSave): Promise<void> {
  if (!pool) {
    return;
  }
  const client = await pool.connect();
  try {
    await client.query("BEGIN");
    let acc = await client.query<{ id: string }>(`SELECT id FROM accounts WHERE guest_token = $1`, [data.guestToken]);
    let accountId = acc.rows[0]?.id;
    if (!accountId) {
      const ins = await client.query<{ id: string }>(
        `INSERT INTO accounts (guest_token) VALUES ($1) RETURNING id`,
        [data.guestToken],
      );
      accountId = ins.rows[0].id;
    }
    const slot = data.slotIndex ?? 0;
    let characterId = data.characterId;
    if (!characterId) {
      const existing = await client.query<{ id: string }>(
        `SELECT id FROM characters WHERE account_id = $1 AND slot_index = $2`,
        [accountId, slot],
      );
      characterId = existing.rows[0]?.id;
    }
    const payload = [
      slot,
      data.classId,
      data.name ?? "Adventurer",
      data.level ?? 1,
      data.xp ?? 0,
      data.skillPoints ?? 0,
      JSON.stringify(data.unlockedSkillIds ?? []),
      data.mapId ?? "town_ashen",
      data.x,
      data.y,
      data.hp,
      data.mp,
      data.gold ?? 100,
      data.homeMapId ?? "town_ashen",
      data.homeX ?? 6,
      data.homeY ?? 9,
      data.equippedWeaponId ?? null,
      data.equippedWeapon2Id ?? null,
      data.equippedSpiritId ?? null,
      JSON.stringify(data.weaponIds ?? []),
      JSON.stringify(data.spiritIds ?? []),
      data.equippedArmorId ?? null,
      data.equippedHelmId ?? null,
      data.equippedBootsId ?? null,
      data.equippedGlovesId ?? null,
      data.equippedAmuletId ?? data.equippedAccessoryId ?? null,
      data.classCardId ?? null,
      data.towerClearedFloor ?? 0,
      JSON.stringify(data.switchFlags ?? {}),
      Boolean(data.charNameSet),
      JSON.stringify(data.completedQuestIds ?? []),
      data.equippedSubclassId ?? null,
      Boolean(data.transformed),
      data.equippedRing1Id ?? null,
      data.equippedRing2Id ?? null,
      JSON.stringify(data.enhanceLevels ?? {}),
    ];
    if (!characterId) {
      const ins = await client.query<{ id: string }>(
        `INSERT INTO characters (
          account_id, slot_index, class_id, name, level, xp, skill_points, unlocked_skill_ids,
          map_id, x, y, hp, mp, gold, home_map_id, home_x, home_y,
          equipped_weapon_id, equipped_weapon2_id, equipped_spirit_id, weapon_ids, spirit_ids,
          equipped_armor_id, equipped_helm_id, equipped_boots_id, equipped_gloves_id, equipped_accessory_id,
          equipped_amulet_id,
          class_card_id, tower_cleared_floor, switch_flags, char_name_set, completed_quest_ids,
          equipped_subclass_id, transformed, equipped_ring1_id, equipped_ring2_id, enhance_levels
        ) VALUES ($1,$2,$3,$4,$5,$6,$7,$8::jsonb,$9,$10,$11,$12,$13,$14,$15,$16,$17,$18,$19,$20,$21::jsonb,$22::jsonb,$23,$24,$25,$26,$27,$27,$28,$29,$30::jsonb,$31,$32::jsonb,$33,$34,$35,$36,$37::jsonb)
        RETURNING id`,
        [accountId, ...payload],
      );
      characterId = ins.rows[0].id;
      data.characterId = characterId;
    } else {
      await client.query(
        `UPDATE characters SET
          slot_index=$2, class_id=$3, name=$4, level=$5, xp=$6, skill_points=$7, unlocked_skill_ids=$8::jsonb,
          map_id=$9, x=$10, y=$11, hp=$12, mp=$13, gold=$14, home_map_id=$15, home_x=$16, home_y=$17,
          equipped_weapon_id=$18, equipped_weapon2_id=$19, equipped_spirit_id=$20, weapon_ids=$21::jsonb, spirit_ids=$22::jsonb,
          equipped_armor_id=$23, equipped_helm_id=$24, equipped_boots_id=$25, equipped_gloves_id=$26, equipped_accessory_id=$27,
          equipped_amulet_id=$27,
          class_card_id=$28, tower_cleared_floor=$29, switch_flags=$30::jsonb, char_name_set=$31, completed_quest_ids=$32::jsonb,
          equipped_subclass_id=$33, transformed=$34, equipped_ring1_id=$35, equipped_ring2_id=$36, enhance_levels=$37::jsonb
        WHERE id=$1`,
        [characterId, ...payload],
      );
    }

    for (const slotInv of data.inventory) {
      await client.query(
        `INSERT INTO inventory_slots (character_id, slot_index, item_id, quantity)
         VALUES ($1,$2,$3,$4)
         ON CONFLICT (character_id, slot_index) DO UPDATE SET item_id=$3, quantity=$4`,
        [characterId, slotInv.slotIndex, slotInv.itemId, slotInv.quantity],
      );
    }

    for (const [bannerId, counter] of Object.entries(data.pity ?? {})) {
      await client.query(
        `INSERT INTO pity_counters (account_id, banner_id, pity, total_pulls)
         VALUES ($1,$2,$3,$4)
         ON CONFLICT (account_id, banner_id) DO UPDATE SET pity=$3, total_pulls=$4, updated_at=now()`,
        [accountId, bannerId, counter.pity, counter.totalPulls],
      );
    }

    await client.query(`DELETE FROM quest_progress WHERE character_id = $1`, [characterId]);
    for (const q of data.quests ?? []) {
      await client.query(
        `INSERT INTO quest_progress (character_id, quest_id, step_index, progress, completed)
         VALUES ($1,$2,$3,$4,$5)`,
        [characterId, q.questId, q.stepIndex, q.progress, q.completed],
      );
    }

    await client.query("COMMIT");
  } catch (err) {
    await client.query("ROLLBACK");
    throw err;
  } finally {
    client.release();
  }
}
