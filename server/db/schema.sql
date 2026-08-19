-- PostgreSQL contract for gAAAcha. Apply on server boot when DATABASE_URL is set.
-- Static catalogs stay JSON.

CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS accounts (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  guest_token TEXT NOT NULL UNIQUE,
  username TEXT UNIQUE,
  password_hash TEXT,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS characters (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  account_id UUID NOT NULL REFERENCES accounts (id) ON DELETE CASCADE,
  slot_index SMALLINT NOT NULL DEFAULT 0,
  class_id TEXT NOT NULL,
  name TEXT NOT NULL,
  level INTEGER NOT NULL DEFAULT 1,
  xp INTEGER NOT NULL DEFAULT 0,
  skill_points INTEGER NOT NULL DEFAULT 0,
  unlocked_skill_ids JSONB NOT NULL DEFAULT '[]'::jsonb,
  map_id TEXT NOT NULL,
  x REAL NOT NULL,
  y REAL NOT NULL,
  hp INTEGER NOT NULL,
  mp INTEGER NOT NULL,
  gold INTEGER NOT NULL DEFAULT 100,
  home_map_id TEXT NOT NULL DEFAULT 'town_ashen',
  home_x REAL NOT NULL DEFAULT 6,
  home_y REAL NOT NULL DEFAULT 9,
  equipped_weapon_id TEXT,
  equipped_weapon2_id TEXT,
  equipped_spirit_id TEXT,
  weapon_ids JSONB NOT NULL DEFAULT '[]'::jsonb,
  spirit_ids JSONB NOT NULL DEFAULT '[]'::jsonb,
  equipped_armor_id TEXT,
  equipped_helm_id TEXT,
  equipped_boots_id TEXT,
  equipped_gloves_id TEXT,
  equipped_accessory_id TEXT,
  class_card_id TEXT,
  tower_cleared_floor INTEGER NOT NULL DEFAULT 0,
  switch_flags JSONB NOT NULL DEFAULT '{}'::jsonb,
  char_name_set BOOLEAN NOT NULL DEFAULT FALSE,
  completed_quest_ids JSONB NOT NULL DEFAULT '[]'::jsonb,
  party_id UUID,
  instance_id UUID,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  CONSTRAINT characters_slot_range CHECK (slot_index >= 0 AND slot_index < 8),
  UNIQUE (account_id, slot_index)
);

CREATE INDEX IF NOT EXISTS characters_account_id_idx ON characters (account_id);
CREATE INDEX IF NOT EXISTS characters_map_id_idx ON characters (map_id);

CREATE TABLE IF NOT EXISTS map_instances (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  map_id TEXT NOT NULL,
  party_id TEXT,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  expires_at TIMESTAMPTZ
);

CREATE TABLE IF NOT EXISTS inventory_slots (
  character_id UUID NOT NULL REFERENCES characters (id) ON DELETE CASCADE,
  slot_index SMALLINT NOT NULL,
  item_id TEXT,
  quantity INTEGER NOT NULL DEFAULT 0,
  PRIMARY KEY (character_id, slot_index),
  CONSTRAINT inventory_slots_range CHECK (slot_index >= 0 AND slot_index < 144),
  CONSTRAINT inventory_slots_qty CHECK (quantity >= 0)
);

-- Migrate existing DBs that still have the old 20-slot check
ALTER TABLE inventory_slots DROP CONSTRAINT IF EXISTS inventory_slots_range;
ALTER TABLE inventory_slots ADD CONSTRAINT inventory_slots_range CHECK (slot_index >= 0 AND slot_index < 144);

CREATE TABLE IF NOT EXISTS pity_counters (
  account_id UUID NOT NULL REFERENCES accounts (id) ON DELETE CASCADE,
  banner_id TEXT NOT NULL,
  pity INTEGER NOT NULL DEFAULT 0,
  total_pulls INTEGER NOT NULL DEFAULT 0,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (account_id, banner_id)
);

CREATE TABLE IF NOT EXISTS gacha_history (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  account_id UUID NOT NULL REFERENCES accounts (id) ON DELETE CASCADE,
  character_id UUID REFERENCES characters (id) ON DELETE SET NULL,
  banner_id TEXT NOT NULL,
  item_id TEXT NOT NULL,
  rarity TEXT NOT NULL,
  pity_before INTEGER NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS quest_progress (
  character_id UUID NOT NULL REFERENCES characters (id) ON DELETE CASCADE,
  quest_id TEXT NOT NULL,
  step_index INTEGER NOT NULL DEFAULT 0,
  progress INTEGER NOT NULL DEFAULT 0,
  completed BOOLEAN NOT NULL DEFAULT FALSE,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (character_id, quest_id)
);

-- Soft-upgrade columns for older DBs
ALTER TABLE characters ADD COLUMN IF NOT EXISTS slot_index SMALLINT NOT NULL DEFAULT 0;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS skill_points INTEGER NOT NULL DEFAULT 0;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS unlocked_skill_ids JSONB NOT NULL DEFAULT '[]'::jsonb;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS equipped_weapon2_id TEXT;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS class_card_id TEXT;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS tower_cleared_floor INTEGER NOT NULL DEFAULT 0;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS switch_flags JSONB NOT NULL DEFAULT '{}'::jsonb;
