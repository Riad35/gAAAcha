import { WebSocket, WebSocketServer } from "ws";
import { handleChat } from "./chat.js";
import { bindCombatWorld, validateCast, validateMove } from "./combat.js";
import { pullGacha } from "./gacha.js";
import { saveGuest } from "./persist.js";
import type { ChatChannel, ClientMessage, PlayerSession, ServerMessage } from "./types.js";
import {
  buildInspect,
  cooldownSnapshot,
  currentPityView,
  equipSpirit,
  equipWeapon,
  findEntity,
  grantKillLoot,
  killMonster,
  liveMonsters,
  monsterStatuses,
  notePlayerDamageThreat,
  players,
  snapshot,
  spawnPlayer,
  spawnProjectileFromCast,
  statusOf,
  tickProjectiles,
  tickWorld,
} from "./world.js";
import { defaultClass, defaultMap, skillById } from "./data.js";

bindCombatWorld(
  findEntity,
  () => [...players.values()],
  (id, status) => {
    const list = monsterStatuses.get(id) ?? [];
    monsterStatuses.set(id, [...list.filter((s) => s.id !== status.id), status]);
  },
  () => [...liveMonsters.values()].filter((monster) => monster.hp > 0),
);

const sockets = new Map<string, WebSocket>();

function send(ws: WebSocket, message: ServerMessage): void {
  if (ws.readyState === WebSocket.OPEN) {
    ws.send(JSON.stringify(message));
  }
}

function sendTo(playerId: string, message: ServerMessage): void {
  const ws = sockets.get(playerId);
  if (ws) {
    send(ws, message);
  }
}

function broadcast(message: ServerMessage): void {
  const raw = JSON.stringify(message);
  for (const socket of sockets.values()) {
    if (socket.readyState === WebSocket.OPEN) {
      socket.send(raw);
    }
  }
}

function broadcastAll(messages: ServerMessage[]): void {
  for (const message of messages) {
    broadcast(message);
  }
}

function vitalsOf(entityId: string): ServerMessage | null {
  const entity = findEntity(entityId) ?? liveMonsters.get(entityId) ?? players.get(entityId)?.entity;
  if (!entity) {
    return null;
  }
  return {
    type: "sync_vitals",
    entityId: entity.id,
    hp: entity.hp,
    maxHp: entity.maxHp,
    mp: entity.mp,
    maxMp: entity.maxMp,
  };
}

function broadcastStatus(entityId: string, now: number): void {
  const msg = statusOf(entityId, now);
  if (msg) {
    broadcast(msg);
  }
}

function persist(session: PlayerSession): void {
  saveGuest({
    guestToken: session.guestToken,
    classId: session.classId,
    x: session.entity.x,
    y: session.entity.y,
    hp: session.entity.hp,
    mp: session.entity.mp,
    inventory: session.inventory,
    pity: session.pity,
    equippedWeaponId: session.equippedWeaponId,
    weaponIds: session.weaponIds,
    equippedSpiritId: session.equippedSpiritId,
    spiritIds: session.spiritIds,
    updatedAt: Date.now(),
  });
}

function parseMessage(raw: string): ClientMessage | null {
  try {
    const data = JSON.parse(raw) as ClientMessage;
    if (data.type === "request_move" || data.type === "cast_skill") {
      return data;
    }
    if (data.type === "request_gacha" && (data.count === 1 || data.count === 10)) {
      return data;
    }
    if (data.type === "request_hello" && typeof data.guestToken === "string") {
      return data;
    }
    if (data.type === "request_equip") {
      const hasWeapon = typeof data.weaponId === "string";
      const hasSpirit = data.spiritId === null || typeof data.spiritId === "string";
      if (hasWeapon || hasSpirit) {
        return data;
      }
    }
    if (data.type === "request_inspect" && typeof data.targetId === "string") {
      return data;
    }
    if (data.type === "request_chat" && typeof data.text === "string" && typeof data.channel === "string") {
      const ch = data.channel as ChatChannel;
      if (ch === "world" || ch === "server" || ch === "guild" || ch === "map" || ch === "whisper") {
        return data;
      }
    }
    if (data.type === "request_party_invite" && typeof data.targetId === "string") {
      return data;
    }
    return null;
  } catch {
    return null;
  }
}

function sendState(ws: WebSocket, session: PlayerSession): void {
  const { players: playerList, monsters } = snapshot();
  const now = Date.now();
  send(ws, {
    type: "sync_state",
    you: session.entity,
    players: playerList,
    monsters,
    guestToken: session.guestToken,
    pity: currentPityView(session),
    equippedWeaponId: session.equippedWeaponId,
    weaponIds: session.weaponIds,
    equippedSpiritId: session.equippedSpiritId,
    spiritIds: session.spiritIds,
    skillIds: [...defaultClass.skillIds],
    cooldowns: cooldownSnapshot(session),
    inventory: session.inventory,
    serverTime: now,
    map: defaultMap,
  });
  broadcast({ type: "sync_status", entityId: session.entity.id, statuses: session.statuses, serverTime: now });
  for (const [mid, statuses] of monsterStatuses) {
    send(ws, { type: "sync_status", entityId: mid, statuses, serverTime: now });
  }
}

export function startServer(port = 7777): WebSocketServer {
  const wss = new WebSocketServer({ port, host: "127.0.0.1" });

  setInterval(() => {
    broadcastAll(tickWorld(Date.now()));
  }, 400);

  setInterval(() => {
    broadcastAll(tickProjectiles(Date.now(), 0.1));
  }, 100);

  wss.on("connection", (ws) => {
    let session = spawnPlayer();
    sockets.set(session.entity.id, ws);
    sendState(ws, session);
    console.log(`player joined ${session.entity.id} guest=${session.guestToken}`);

    ws.on("message", (buf) => {
      const msg = parseMessage(buf.toString());
      if (!msg) {
        send(ws, { type: "error", code: "bad_packet", message: "Unrecognized message" });
        return;
      }

      if (msg.type === "request_hello") {
        sockets.delete(session.entity.id);
        players.delete(session.entity.id);
        session = spawnPlayer(msg.guestToken);
        sockets.set(session.entity.id, ws);
        sendState(ws, session);
        console.log(`player hello ${session.entity.id} guest=${session.guestToken}`);
        return;
      }

      const now = Date.now();
      if (msg.type === "request_equip") {
        if (typeof msg.weaponId === "string") {
          const result = equipWeapon(session, msg.weaponId);
          if (!("ok" in result)) {
            send(ws, result);
            return;
          }
        }
        if (msg.spiritId !== undefined) {
          const result = equipSpirit(session, msg.spiritId);
          if (!("ok" in result)) {
            send(ws, result);
            return;
          }
        }
        persist(session);
        send(ws, {
          type: "sync_equip",
          weaponId: session.equippedWeaponId,
          spiritId: session.equippedSpiritId,
          you: session.entity,
        });
        return;
      }

      if (msg.type === "request_move") {
        if (session.entity.hp <= 0) {
          send(ws, { type: "error", code: "you_are_dead", message: "You are dead — reconnect to respawn" });
          return;
        }
        const result = validateMove(session, msg.x, msg.y, now);
        if ("ok" in result) {
          const dx = result.x - session.entity.x;
          const dy = result.y - session.entity.y;
          const len = Math.hypot(dx, dy);
          if (len > 0.001) {
            session.facingX = dx / len;
            session.facingY = dy / len;
          }
          session.entity.x = result.x;
          session.entity.y = result.y;
          session.lastMoveAt = now;
          broadcast({ type: "sync_move", entityId: session.entity.id, x: result.x, y: result.y });
          return;
        }
        send(ws, result);
        return;
      }

      if (msg.type === "request_gacha") {
        const gacha = pullGacha(session, msg.bannerId, msg.count, Math.random);
        if ("ok" in gacha) {
          persist(session);
          send(ws, { type: "sync_gacha", results: gacha.results, pity: gacha.pity, inventory: gacha.inventory });
          return;
        }
        send(ws, gacha);
        return;
      }

      if (msg.type === "request_inspect") {
        send(ws, buildInspect(msg.targetId, now));
        return;
      }

      if (msg.type === "request_chat") {
        const result = handleChat(session, msg.channel, msg.text, msg.targetName, now);
        if (result.error) {
          send(ws, result.error);
          return;
        }
        for (const { wsId, msg: chatMsg } of result.messages) {
          sendTo(wsId, chatMsg);
        }
        return;
      }

      if (msg.type === "request_party_invite") {
        send(ws, { type: "error", code: "coming_soon", message: "Party invites coming soon" });
        return;
      }

      if (msg.type !== "cast_skill") {
        send(ws, { type: "error", code: "bad_packet", message: "Unrecognized message" });
        return;
      }

      if (session.entity.hp <= 0) {
        send(ws, { type: "error", code: "you_are_dead", message: "You are dead — reconnect to respawn" });
        return;
      }

      const result = validateCast(session, msg.skillId, msg.targetId, now, {
        aimDx: msg.aimDx,
        aimDy: msg.aimDy,
        aimX: msg.aimX,
        aimY: msg.aimY,
      });
      if (!("ok" in result)) {
        send(ws, result);
        return;
      }

      for (const moved of result.movedEntities) {
        broadcast({ type: "sync_move", entityId: moved.id, x: moved.x, y: moved.y });
      }

      if (result.projectile) {
        const spawned = spawnProjectileFromCast(session.entity, result.projectile, result.mpAfter);
        broadcast(spawned.message);
      } else if (result.aoe) {
        broadcast({
          type: "sync_aoe",
          casterId: session.entity.id,
          skillId: msg.skillId,
          centerId: result.primaryTargetId,
          aoeRadius: result.aoeRadius ?? skillById(msg.skillId)?.aoeRadius ?? Math.max(0.5, (skillById(msg.skillId)?.range ?? 2) * 0.5),
          aimX: result.aimX,
          aimY: result.aimY,
          hits: result.hits,
          mpAfter: result.mpAfter,
        });
      } else {
        const hit = result.hits[0];
        broadcast({
          type: "sync_skill",
          casterId: session.entity.id,
          targetId: hit?.targetId ?? result.primaryTargetId,
          skillId: msg.skillId,
          damage: hit?.damage ?? 0,
          hpAfter: hit?.hpAfter ?? 0,
          mpAfter: result.mpAfter,
          crit: hit?.crit,
        });
      }

      let grantedLoot = false;
      const statusIds = new Set<string>([session.entity.id, result.primaryTargetId]);
      for (const hit of result.hits) {
        statusIds.add(hit.targetId);
        const threat = notePlayerDamageThreat(session.entity.id, hit.targetId, hit.damage, now);
        if (threat) {
          broadcast(threat);
        }
        const targetVitals = vitalsOf(hit.targetId);
        if (targetVitals) {
          broadcast(targetVitals);
        }
        if (hit.hpAfter <= 0 && liveMonsters.has(hit.targetId)) {
          broadcastAll(killMonster(hit.targetId, now));
          if (!grantedLoot) {
            send(ws, grantKillLoot(session));
            grantedLoot = true;
            persist(session);
          }
        }
      }

      const selfVitals = vitalsOf(session.entity.id);
      if (selfVitals) {
        broadcast(selfVitals);
      }
      send(ws, {
        type: "sync_cooldowns",
        cooldowns: cooldownSnapshot(session),
        serverTime: now,
      });
      for (const id of statusIds) {
        broadcastStatus(id, now);
      }
      send(ws, { type: "sync_inventory", inventory: session.inventory });
    });

    ws.on("close", () => {
      persist(session);
      sockets.delete(session.entity.id);
      players.delete(session.entity.id);
      console.log(`player left ${session.entity.id}`);
    });
  });

  console.log(`gAAAcha server ws://127.0.0.1:${port}`);
  return wss;
}
