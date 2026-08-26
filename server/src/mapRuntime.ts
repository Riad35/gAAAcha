import type { Entity, LiveProjectile, PlayerSession } from "./types.js";

/** Per-map occupancy. Entities still live in the global id maps; this is the locality index. */
export class MapRuntime {
  readonly mapId: string;
  readonly playerIds = new Set<string>();
  readonly monsterIds = new Set<string>();
  readonly npcIds = new Set<string>();
  readonly projectileIds = new Set<string>();

  constructor(mapId: string) {
    this.mapId = mapId;
  }

  get occupied(): boolean {
    return this.playerIds.size > 0;
  }
}

const runtimes = new Map<string, MapRuntime>();

export function getMapRuntime(mapId: string): MapRuntime | undefined {
  return runtimes.get(mapId);
}

export function ensureMapRuntime(mapId: string): MapRuntime {
  let runtime = runtimes.get(mapId);
  if (!runtime) {
    runtime = new MapRuntime(mapId);
    runtimes.set(mapId, runtime);
  }
  return runtime;
}

export function allMapRuntimes(): Iterable<MapRuntime> {
  return runtimes.values();
}

export function occupiedMapIds(): Set<string> {
  const ids = new Set<string>();
  for (const runtime of runtimes.values()) {
    if (runtime.playerIds.size > 0) {
      ids.add(runtime.mapId);
    }
  }
  return ids;
}

export function resetMapRuntimes(): void {
  runtimes.clear();
}

export function rebuildMapRuntimes(input: {
  players: Iterable<PlayerSession>;
  monsters: Iterable<Entity>;
  npcs: Iterable<Entity>;
  projectiles: Iterable<LiveProjectile>;
}): void {
  runtimes.clear();
  for (const session of input.players) {
    if (!session.inWorld) {
      continue;
    }
    ensureMapRuntime(session.entity.mapId).playerIds.add(session.entity.id);
  }
  for (const monster of input.monsters) {
    ensureMapRuntime(monster.mapId).monsterIds.add(monster.id);
  }
  for (const npc of input.npcs) {
    ensureMapRuntime(npc.mapId).npcIds.add(npc.id);
  }
  for (const projectile of input.projectiles) {
    ensureMapRuntime(projectile.mapId).projectileIds.add(projectile.id);
  }
}

export function reindexMonster(entity: Entity, previousMapId?: string): void {
  if (previousMapId && previousMapId !== entity.mapId) {
    getMapRuntime(previousMapId)?.monsterIds.delete(entity.id);
  }
  for (const runtime of runtimes.values()) {
    if (runtime.mapId !== entity.mapId) {
      runtime.monsterIds.delete(entity.id);
    }
  }
  ensureMapRuntime(entity.mapId).monsterIds.add(entity.id);
}
