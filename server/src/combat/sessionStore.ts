import { loadCombatConfig } from "./config.js";
import { log } from "../log.js";
import type { CombatSession } from "./types.js";

/**
 * Session persistence for active combat (docs/combat-rules.md §12).
 * Memory adapter is the default. Redis adapter uses the same key/TTL contract
 * and activates only when REDIS_URL is set (scaffolding — no ioredis dep yet).
 */
export interface CombatSessionStore {
  get(sessionId: string): Promise<CombatSession | null>;
  set(session: CombatSession): Promise<void>;
  delete(sessionId: string): Promise<void>;
  /** Touch TTL / expiresAt without rewriting full payload when possible. */
  touch(sessionId: string, expiresAt: number): Promise<void>;
}

export function combatSessionKey(sessionId: string): string {
  return `combat:session:${sessionId}`;
}

export function createEmptyCombatSession(
  id: string,
  mapId: string,
  now = Date.now(),
): CombatSession {
  const ttlMs = loadCombatConfig().sessionTtlSec * 1000;
  return {
    id,
    mapId,
    createdAt: now,
    updatedAt: now,
    expiresAt: now + ttlMs,
    units: {},
    intentQueue: [],
    tickSeq: 0,
  };
}

export class MemoryCombatSessionStore implements CombatSessionStore {
  private readonly map = new Map<string, CombatSession>();

  async get(sessionId: string): Promise<CombatSession | null> {
    const s = this.map.get(sessionId);
    if (!s) {
      return null;
    }
    if (s.expiresAt <= Date.now()) {
      this.map.delete(sessionId);
      return null;
    }
    // Return a structured clone so callers can't mutate the store by accident.
    return structuredClone(s);
  }

  async set(session: CombatSession): Promise<void> {
    const copy = structuredClone(session);
    copy.updatedAt = Date.now();
    this.map.set(session.id, copy);
  }

  async delete(sessionId: string): Promise<void> {
    this.map.delete(sessionId);
  }

  async touch(sessionId: string, expiresAt: number): Promise<void> {
    const s = this.map.get(sessionId);
    if (!s) {
      return;
    }
    s.expiresAt = expiresAt;
    s.updatedAt = Date.now();
  }

  /** Test / debug helper */
  clear(): void {
    this.map.clear();
  }

  size(): number {
    return this.map.size;
  }
}

/**
 * Redis-shaped scaffolding. Without REDIS_URL (or a wired client), falls back to memory.
 * Key: combat:session:{id} — value: JSON CombatSession — TTL: sessionTtlSec.
 * Step 2 does not add an ioredis dependency; swap the body when Redis is introduced.
 */
export class RedisCombatSessionStore implements CombatSessionStore {
  private readonly fallback = new MemoryCombatSessionStore();
  private readonly redisUrl: string | undefined;

  constructor(redisUrl = process.env.REDIS_URL) {
    this.redisUrl = redisUrl;
    if (this.redisUrl) {
      log.warn("PERSIST", "REDIS_URL set but Redis client not wired yet — using memory fallback", {
        key: combatSessionKey("{id}"),
      });
    }
  }

  get(sessionId: string): Promise<CombatSession | null> {
    return this.fallback.get(sessionId);
  }

  set(session: CombatSession): Promise<void> {
    return this.fallback.set(session);
  }

  delete(sessionId: string): Promise<void> {
    return this.fallback.delete(sessionId);
  }

  touch(sessionId: string, expiresAt: number): Promise<void> {
    return this.fallback.touch(sessionId, expiresAt);
  }
}

let defaultStore: CombatSessionStore | null = null;

/** Process-wide store: Redis wrapper if REDIS_URL else pure memory. */
export function getCombatSessionStore(): CombatSessionStore {
  if (!defaultStore) {
    defaultStore = process.env.REDIS_URL
      ? new RedisCombatSessionStore()
      : new MemoryCombatSessionStore();
  }
  return defaultStore;
}

export function setCombatSessionStoreForTests(store: CombatSessionStore | null): void {
  defaultStore = store;
}
