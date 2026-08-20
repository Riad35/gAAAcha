import { mapById } from "./data.js";
import { log } from "./log.js";
import type { Entity, MonsterDef, PlayerSession, ServerMessage } from "./types.js";

export type LiveInstance = {
  id: string;
  mapId: string;
  partyId: string | null;
  createdAt: number;
  expiresAt: number;
  monsterIds: string[];
};

type Spawner = {
  createId: (prefix: string) => string;
  spawnMonster: (def: MonsterDef, id: string, x: number, y: number) => Entity;
  listDungeonDefs: (mapId: string) => MonsterDef[];
  despawnMonster: (id: string) => void;
};

const instances = new Map<string, LiveInstance>();
const INSTANCE_TTL_MS = 30 * 60_000;
let hooks: Spawner | null = null;

export function bindInstanceHooks(s: Spawner): void {
  hooks = s;
}

export function resolveBaseMapId(mapId: string): string {
  const hash = mapId.indexOf("#");
  return hash >= 0 ? mapId.slice(0, hash) : mapId;
}

export function isDungeonMap(mapId: string): boolean {
  const base = resolveBaseMapId(mapId);
  return base.startsWith("dungeon_") || base.startsWith("tower_boss_");
}

export function isInstanceMap(mapId: string): boolean {
  return isDungeonMap(mapId);
}

export function createDungeonInstance(
  dungeonMapId: string,
  partyId: string | null,
  now: number,
): LiveInstance {
  if (!hooks) {
    throw new Error("instance hooks not bound");
  }
  if (!isDungeonMap(dungeonMapId)) {
    throw new Error("not a dungeon map");
  }
  const id = hooks.createId("inst");
  const mapId = resolveBaseMapId(dungeonMapId);
  const inst: LiveInstance = {
    id,
    mapId,
    partyId,
    createdAt: now,
    expiresAt: now + INSTANCE_TTL_MS,
    monsterIds: [],
  };
  for (const def of hooks.listDungeonDefs(mapId)) {
    const mid = `${def.respawnId}_${id}`;
    const ent = hooks.spawnMonster(def, mid, def.x, def.y);
    ent.mapId = `${mapId}#${id}`;
    inst.monsterIds.push(mid);
  }
  instances.set(id, inst);
  log.info("WORLD", "instance open", { map: mapId, inst: id, party: partyId ?? "solo" });
  return inst;
}

export function enterDungeon(
  session: PlayerSession,
  dungeonMapId: string,
  now: number,
): { error?: ServerMessage; instance?: LiveInstance } {
  if (session.entity.hp <= 0) {
    return { error: { type: "error", code: "you_are_dead", message: "You are dead" } };
  }
  const base = resolveBaseMapId(dungeonMapId);
  const dest = mapById(base);
  if (!dest || !isDungeonMap(base)) {
    return { error: { type: "error", code: "bad_map", message: "Dungeon missing" } };
  }
  let inst: LiveInstance | undefined;
  if (session.partyId) {
    for (const existing of instances.values()) {
      if (existing.partyId === session.partyId && existing.mapId === base && now < existing.expiresAt) {
        inst = existing;
        break;
      }
    }
  }
  if (!inst) {
    inst = createDungeonInstance(base, session.partyId, now);
  }
  session.entity.mapId = `${inst.mapId}#${inst.id}`;
  session.entity.x = dest.spawn.x;
  session.entity.y = dest.spawn.y;
  return { instance: inst };
}

export function getInstanceByMapKey(mapKey: string): LiveInstance | undefined {
  const hash = mapKey.indexOf("#");
  if (hash < 0) {
    return undefined;
  }
  return instances.get(mapKey.slice(hash + 1));
}

export function instanceSyncMsg(mapKey: string): ServerMessage | null {
  const inst = getInstanceByMapKey(mapKey);
  if (!inst) {
    return { type: "sync_instance", instanceId: null, mapId: resolveBaseMapId(mapKey), expiresAt: 0 };
  }
  return {
    type: "sync_instance",
    instanceId: inst.id,
    mapId: inst.mapId,
    expiresAt: inst.expiresAt,
  };
}

export function tickInstances(now: number): void {
  if (!hooks) {
    return;
  }
  for (const [id, inst] of instances) {
    if (now < inst.expiresAt) {
      continue;
    }
    for (const mid of inst.monsterIds) {
      hooks.despawnMonster(mid);
    }
    instances.delete(id);
  }
}
