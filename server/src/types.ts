export type MapDef = {
  id: string;
  name: string;
  width: number;
  height: number;
  spawn: { x: number; y: number };
  blocked: { x: number; y: number }[];
};

export type ClassDef = {
  id: string;
  name: string;
  hp: number;
  mp: number;
  atk: number;
  def: number;
  moveSpeed: number;
  skillIds: string[];
};

export type SkillDef = {
  id: string;
  name: string;
  range: number;
  cooldownMs: number;
  manaCost: number;
  damage: number;
  heal: number;
  selfTarget: boolean;
};

export type MonsterDef = {
  id: string;
  name: string;
  hp: number;
  atk: number;
  def: number;
  x: number;
  y: number;
};

export type Entity = {
  id: string;
  kind: "player" | "monster";
  name: string;
  x: number;
  y: number;
  hp: number;
  maxHp: number;
  mp: number;
  maxMp: number;
  atk: number;
  def: number;
  moveSpeed: number;
  mapId: string;
};

export type PlayerSession = {
  entity: Entity;
  classId: string;
  lastActionAt: number;
  lastMoveAt: number;
  actionTimes: number[];
  skillReadyAt: Record<string, number>;
};

export type ClientMessage =
  | { type: "request_move"; x: number; y: number }
  | { type: "cast_skill"; skillId: string; targetId: string };

export type ServerMessage =
  | { type: "sync_state"; you: Entity; players: Entity[]; monsters: Entity[] }
  | { type: "sync_move"; entityId: string; x: number; y: number }
  | { type: "sync_skill"; casterId: string; targetId: string; skillId: string; damage: number; hpAfter: number; mpAfter: number }
  | { type: "error"; code: string; message: string };
