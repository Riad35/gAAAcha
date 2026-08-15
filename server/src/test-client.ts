import WebSocket from "ws";

const url = process.env.WS_URL ?? "ws://127.0.0.1:7777";

type Incoming = {
  type: string;
  you?: { id: string; x: number; y: number };
  monsters?: { id: string; x: number; y: number; hp: number }[];
  code?: string;
  message?: string;
  entityId?: string;
  casterId?: string;
  skillId?: string;
  damage?: number;
  hpAfter?: number;
  mpAfter?: number;
};

function send(ws: WebSocket, payload: unknown): void {
  ws.send(JSON.stringify(payload));
}

function wait(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

const ws = new WebSocket(url);
let selfId = "";
let monsterId = "";
let seenState = false;
let seenMove = false;
let seenSkill = false;
let seenMana = false;
let seenError = false;

ws.on("open", async () => {
  console.log("connected", url);
  await wait(400);
  send(ws, { type: "request_move", x: 3, y: 6 });
  await wait(200);
  if (monsterId) {
    send(ws, { type: "cast_skill", skillId: "shot", targetId: monsterId });
  }
  await wait(200);
  send(ws, { type: "cast_skill", skillId: "shot", targetId: monsterId || "missing" });
  await wait(400);
  ws.close();
});

ws.on("message", (buf) => {
  const msg = JSON.parse(buf.toString()) as Incoming;
  console.log("recv", msg);

  if (msg.type === "sync_state" && msg.you) {
    seenState = true;
    selfId = msg.you.id;
    monsterId = msg.monsters?.[0]?.id ?? "";
  }
  if (msg.type === "sync_move" && msg.entityId === selfId) {
    seenMove = true;
  }
  if (msg.type === "sync_skill" && msg.skillId === "shot") {
    seenSkill = true;
    seenMana = msg.mpAfter === 42;
  }
  if (msg.type === "error" && msg.code === "on_cooldown") {
    seenError = true;
  }
});

ws.on("close", () => {
  const ok = seenState && seenMove && seenSkill && seenMana && seenError;
  console.log(
    ok
      ? "test-client ok: sync_state, sync_move, sync_skill, mpAfter, on_cooldown"
      : `test-client incomplete state=${seenState} move=${seenMove} skill=${seenSkill} mana=${seenMana} cooldown=${seenError}`,
  );
  process.exit(ok ? 0 : 1);
});

ws.on("error", (err) => {
  console.error("test-client error", err.message);
  process.exit(1);
});
