-- PostgreSQL 17 contract. Runtime is still in-memory until a DB is wired.
-- Static catalogs (maps, items, skills, monsters, banners, classes) stay JSON.

CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE accounts (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  guest_token TEXT NOT NULL UNIQUE,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE characters (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  account_id UUID NOT NULL REFERENCES accounts (id) ON DELETE CASCADE,
  class_id TEXT NOT NULL,
  name TEXT NOT NULL,
  level INTEGER NOT NULL DEFAULT 1,
  xp INTEGER NOT NULL DEFAULT 0,
  map_id TEXT NOT NULL,
  x REAL NOT NULL,
  y REAL NOT NULL,
  hp INTEGER NOT NULL,
  mp INTEGER NOT NULL,
  party_id UUID,
  instance_id UUID,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX characters_account_id_idx ON characters (account_id);
CREATE INDEX characters_map_id_idx ON characters (map_id);

CREATE TABLE map_instances (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  map_id TEXT NOT NULL,
  party_id UUID,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE inventory_slots (
  character_id UUID NOT NULL REFERENCES characters (id) ON DELETE CASCADE,
  slot_index SMALLINT NOT NULL,
  item_id TEXT,
  quantity INTEGER NOT NULL DEFAULT 0,
  PRIMARY KEY (character_id, slot_index),
  CONSTRAINT inventory_slots_range CHECK (slot_index >= 0 AND slot_index < 20),
  CONSTRAINT inventory_slots_qty CHECK (quantity >= 0),
  CONSTRAINT inventory_slots_empty CHECK (
    (item_id IS NULL AND quantity = 0)
    OR (item_id IS NOT NULL AND quantity > 0)
  )
);

CREATE TABLE pity_counters (
  account_id UUID NOT NULL REFERENCES accounts (id) ON DELETE CASCADE,
  banner_id TEXT NOT NULL,
  pity INTEGER NOT NULL DEFAULT 0,
  total_pulls INTEGER NOT NULL DEFAULT 0,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (account_id, banner_id),
  CONSTRAINT pity_counters_pity CHECK (pity >= 0),
  CONSTRAINT pity_counters_total CHECK (total_pulls >= 0)
);

CREATE TABLE gacha_history (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  account_id UUID NOT NULL REFERENCES accounts (id) ON DELETE CASCADE,
  character_id UUID REFERENCES characters (id) ON DELETE SET NULL,
  banner_id TEXT NOT NULL,
  item_id TEXT NOT NULL,
  rarity TEXT NOT NULL,
  pity_before INTEGER NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  CONSTRAINT gacha_history_rarity CHECK (rarity IN ('r', 'sr', 'ssr'))
);

CREATE INDEX gacha_history_account_idx ON gacha_history (account_id, created_at DESC);

CREATE TABLE quest_progress (
  character_id UUID NOT NULL REFERENCES characters (id) ON DELETE CASCADE,
  quest_id TEXT NOT NULL,
  step INTEGER NOT NULL DEFAULT 0,
  completed BOOLEAN NOT NULL DEFAULT FALSE,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (character_id, quest_id)
);
