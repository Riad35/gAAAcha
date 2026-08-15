import { WebSocket, WebSocketServer } from "ws";
import { validateCast, validateMove } from "./combat.js";
import type { ClientMessage, ServerMessage } from "./types.js";
import { players, snapshot, spawnPlayer } from "./world.js";

const sockets = new Map<string, WebSocket>();

function send(ws: WebSocket, message: ServerMessage): void {
  if (ws.readyState === WebSocket.OPEN) {
    ws.send(JSON.stringify(message));
  }
}

function broadcast(message: ServerMessage): void {
  const raw = JSON.stringify(message);
  for (const ws of sockets.values()) {
    if (ws.readyState === WebSocket.OPEN) {
      ws.send(raw);
    }
  }
}

function parseMessage(raw: string): ClientMessage | null {
  try {
    const data = JSON.parse(raw) as ClientMessage;
    if (data.type === "request_move" || data.type === "cast_skill") {
      return data;
    }
    return null;
  } catch {
    return null;
  }
}

export function startServer(port = 7777): WebSocketServer {
  const wss = new WebSocketServer({ port, host: "127.0.0.1" });

  wss.on("connection", (ws) => {
    const session = spawnPlayer();
    sockets.set(session.entity.id, ws);
    const { players: playerList, monsters } = snapshot();
    send(ws, { type: "sync_state", you: session.entity, players: playerList, monsters });
    console.log(`player joined ${session.entity.id}`);

    ws.on("message", (buf) => {
      const msg = parseMessage(buf.toString());
      if (!msg) {
        send(ws, { type: "error", code: "bad_packet", message: "Unrecognized message" });
        return;
      }

      const now = Date.now();
      if (msg.type === "request_move") {
        const result = validateMove(session, msg.x, msg.y, now);
        if ("ok" in result) {
          session.entity.x = result.x;
          session.entity.y = result.y;
          session.lastMoveAt = now;
          broadcast({ type: "sync_move", entityId: session.entity.id, x: result.x, y: result.y });
          return;
        }
        send(ws, result);
        return;
      }

      const result = validateCast(session, msg.skillId, msg.targetId, now);
      if ("ok" in result) {
        broadcast({
          type: "sync_skill",
          casterId: session.entity.id,
          targetId: result.targetId,
          skillId: msg.skillId,
          damage: result.damage,
          hpAfter: result.hpAfter,
          mpAfter: result.mpAfter,
        });
        return;
      }
      send(ws, result);
    });

    ws.on("close", () => {
      sockets.delete(session.entity.id);
      players.delete(session.entity.id);
      console.log(`player left ${session.entity.id}`);
    });
  });

  console.log(`gAAAcha server ws://127.0.0.1:${port}`);
  return wss;
}
