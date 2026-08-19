import { mapById, portalById, portals } from "./data.js";
import { enterDungeon, isInstanceMap } from "./instance.js";
import type { PlayerSession, PortalDef, ServerMessage } from "./types.js";

const PORTAL_REACH = 2.25;

export function portalsOnMap(mapId: string): PortalDef[] {
  return portals.filter((p) => p.mapId === mapId);
}

export function findPortalNear(session: PlayerSession): PortalDef | undefined {
  const base = session.entity.mapId.includes("#")
    ? session.entity.mapId.slice(0, session.entity.mapId.indexOf("#"))
    : session.entity.mapId;
  return portalsOnMap(base).find(
    (p) => Math.hypot(session.entity.x - p.x, session.entity.y - p.y) <= PORTAL_REACH,
  );
}

export function usePortal(
  session: PlayerSession,
  portalId: string,
  now = Date.now(),
): { error?: ServerMessage; ok?: true } {
  const portal = portalById(portalId);
  if (!portal) {
    return { error: { type: "error", code: "bad_portal", message: "Unknown portal" } };
  }
  const baseMap = session.entity.mapId.includes("#")
    ? session.entity.mapId.slice(0, session.entity.mapId.indexOf("#"))
    : session.entity.mapId;
  if (portal.mapId !== baseMap && portal.mapId !== session.entity.mapId) {
    return { error: { type: "error", code: "wrong_map", message: "Portal not on this map" } };
  }
  if (Math.hypot(session.entity.x - portal.x, session.entity.y - portal.y) > PORTAL_REACH) {
    return { error: { type: "error", code: "too_far", message: "Move closer to the gate" } };
  }
  if (session.entity.hp <= 0) {
    return { error: { type: "error", code: "you_are_dead", message: "You are dead" } };
  }
  const minCleared = portal.minTowerCleared ?? 0;
  if (session.towerClearedFloor < minCleared) {
    return {
      error: {
        type: "error",
        code: "tower_locked",
        message: `Clear floor ${minCleared} before ascending`,
      },
    };
  }
  if (portal.requireSwitch && !session.switchFlags[portal.requireSwitch]) {
    return {
      error: {
        type: "error",
        code: "switch_locked",
        message: "Activate the rune switch first",
      },
    };
  }
  if (isInstanceMap(portal.targetMapId)) {
    const result = enterDungeon(session, portal.targetMapId, now);
    if (result.error) {
      return { error: result.error };
    }
    return { ok: true };
  }
  const dest = mapById(portal.targetMapId);
  if (!dest) {
    return { error: { type: "error", code: "bad_map", message: "Destination missing" } };
  }
  session.entity.mapId = portal.targetMapId;
  session.entity.x = portal.targetX;
  session.entity.y = portal.targetY;
  return { ok: true };
}

export function teleportHome(session: PlayerSession, now: number, requireCooldown: boolean): { error?: ServerMessage; ok?: true } {
  if (session.entity.hp <= 0) {
    return { error: { type: "error", code: "you_are_dead", message: "You are dead" } };
  }
  if (requireCooldown && now < session.homestoneReadyAt) {
    return { error: { type: "error", code: "on_cooldown", message: "Homestone cooling down" } };
  }
  const dest = mapById(session.homeMapId);
  if (!dest) {
    return { error: { type: "error", code: "bad_map", message: "Home map missing" } };
  }
  session.entity.mapId = session.homeMapId;
  session.entity.x = session.homeX;
  session.entity.y = session.homeY;
  if (requireCooldown) {
    session.homestoneReadyAt = now + 30_000;
  }
  return { ok: true };
}

export function setHomestone(session: PlayerSession): void {
  const base = session.entity.mapId.includes("#")
    ? session.entity.mapId.slice(0, session.entity.mapId.indexOf("#"))
    : session.entity.mapId;
  session.homeMapId = base;
  session.homeX = session.entity.x;
  session.homeY = session.entity.y;
}
