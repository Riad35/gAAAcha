import { defaultClass, defaultMap, monsters } from "./data.js";
import type { Entity, PlayerSession } from "./types.js";

let nextId = 1;

export function createId(prefix: string): string {
  nextId += 1;
  return `${prefix}_${nextId}`;
}

export const players = new Map<string, PlayerSession>();
export const liveMonsters = new Map<string, Entity>();

export function resetWorld(): void {
  liveMonsters.clear();
  const def = monsters[0];
  const id = "monster_slime_1";
  liveMonsters.set(id, {
    id,
    kind: "monster",
    name: def.name,
    x: def.x,
    y: def.y,
    hp: def.hp,
    maxHp: def.hp,
    mp: 0,
    maxMp: 0,
    atk: def.atk,
    def: def.def,
    moveSpeed: 0,
    mapId: defaultMap.id,
  });
}

export function spawnPlayer(): PlayerSession {
  const entity: Entity = {
    id: createId("player"),
    kind: "player",
    name: defaultClass.name,
    x: defaultMap.spawn.x,
    y: defaultMap.spawn.y,
    hp: defaultClass.hp,
    maxHp: defaultClass.hp,
    mp: defaultClass.mp,
    maxMp: defaultClass.mp,
    atk: defaultClass.atk,
    def: defaultClass.def,
    moveSpeed: defaultClass.moveSpeed,
    mapId: defaultMap.id,
  };

  const session: PlayerSession = {
    entity,
    classId: defaultClass.id,
    lastActionAt: 0,
    lastMoveAt: Date.now(),
    actionTimes: [],
    skillReadyAt: {},
  };
  players.set(entity.id, session);
  return session;
}

export function snapshot() {
  return {
    players: [...players.values()].map((session) => session.entity),
    monsters: [...liveMonsters.values()],
  };
}

export function findEntity(id: string): Entity | undefined {
  return players.get(id)?.entity ?? liveMonsters.get(id);
}

resetWorld();
