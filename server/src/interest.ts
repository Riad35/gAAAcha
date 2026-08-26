import type { PlayerSession, ServerMessage } from "./types.js";

/** Player-private syncs must not fan out to a map (inventory/xp would clobber other clients). */
export const PRIVATE_SYNC_TYPES = new Set<ServerMessage["type"]>([
  "sync_loot",
  "sync_xp",
  "sync_inventory",
  "sync_gold",
  "sync_quest",
  "sync_gacha",
  "sync_equip",
  "sync_cooldowns",
  "sync_skills",
  "sync_auction",
  "sync_class_change",
  "sync_cond",
  "sync_pong",
]);

export function playerIdsOnMap(mapId: string, sessions: Iterable<PlayerSession>): string[] {
  const ids: string[] = [];
  for (const session of sessions) {
    if (session.inWorld && session.entity.mapId === mapId) {
      ids.push(session.entity.id);
    }
  }
  return ids;
}

export function isPrivateSync(message: ServerMessage): boolean {
  return PRIVATE_SYNC_TYPES.has(message.type);
}

function entityIdFromSync(message: ServerMessage): string | undefined {
  switch (message.type) {
    case "sync_move":
    case "sync_vitals":
    case "sync_status":
    case "sync_cond":
    case "sync_despawn":
    case "sync_death":
      return message.entityId;
    case "sync_inspect":
      return message.targetId;
    case "sync_fx":
      return message.entityId;
    case "sync_spawn":
      return message.entity.id;
    case "sync_skill":
    case "sync_aoe":
      return message.casterId;
    case "sync_projectile_spawn":
      return message.projectile.casterId;
    case "sync_threat":
      return message.monsterId;
    case "sync_chat":
      return message.fromId !== "system" ? message.fromId : undefined;
    default:
      return undefined;
  }
}

/** Resolve which map should receive a world sync. Undefined → do not fan out. */
export function mapIdFromSync(
  message: ServerMessage,
  lookupMap: (entityId: string) => string | undefined,
): string | undefined {
  if ("mapId" in message && typeof message.mapId === "string" && message.mapId.length > 0) {
    if (message.type === "sync_instance" && message.instanceId) {
      return `${message.mapId}#${message.instanceId}`;
    }
    if (message.type !== "sync_state") {
      return message.mapId;
    }
  }
  if (message.type === "sync_spawn") {
    return message.entity.mapId;
  }
  const id = entityIdFromSync(message);
  return id ? lookupMap(id) : undefined;
}

export function casterIdFromSync(message: ServerMessage): string | undefined {
  if (message.type === "sync_skill" || message.type === "sync_aoe") {
    return message.casterId;
  }
  if (message.type === "sync_projectile_spawn") {
    return message.projectile.casterId;
  }
  return undefined;
}
