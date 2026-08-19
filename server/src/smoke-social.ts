/**
 * Smoke: two WS clients — sync_state npcs/monsters, guild, party invite/accept/chat/leave.
 * Posts results to debug ingest.
 */
import WebSocket from "ws";

const url = process.env.WS_URL ?? "ws://127.0.0.1:7777";
const INGEST = "http://127.0.0.1:7409/ingest/d50b7111-4a10-4ea1-b7a7-86675c533304";

type Msg = Record<string, unknown> & { type: string };

function log(hypothesisId: string, location: string, message: string, data: Record<string, unknown>) {
  fetch(INGEST, {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-Debug-Session-Id": "88a0a9" },
    body: JSON.stringify({
      sessionId: "88a0a9",
      runId: "post-fix",
      hypothesisId,
      location,
      message,
      data,
      timestamp: Date.now(),
    }),
  }).catch(() => {});
}

function wait(ms: number) {
  return new Promise((r) => setTimeout(r, ms));
}

function connect(): Promise<{ ws: WebSocket; inbox: Msg[] }> {
  const ws = new WebSocket(url);
  const inbox: Msg[] = [];
  return new Promise((resolve, reject) => {
    ws.on("open", () => resolve({ ws, inbox }));
    ws.on("error", reject);
    ws.on("message", (buf) => {
      try {
        inbox.push(JSON.parse(buf.toString()) as Msg);
      } catch {
        /* ignore */
      }
    });
  });
}

function send(ws: WebSocket, payload: unknown) {
  ws.send(JSON.stringify(payload));
}

async function waitFor(
  inbox: Msg[],
  pred: (m: Msg) => boolean,
  timeoutMs = 3000,
): Promise<Msg | null> {
  const start = Date.now();
  while (Date.now() - start < timeoutMs) {
    const hit = inbox.find(pred);
    if (hit) return hit;
    await wait(50);
  }
  return null;
}

async function main() {
  const results: Record<string, boolean | string | number> = {};
  let a: Awaited<ReturnType<typeof connect>>;
  let b: Awaited<ReturnType<typeof connect>>;
  try {
    a = await connect();
    b = await connect();
  } catch (e) {
    log("H-A", "smoke.ts:connect", "connect failed — old/missing server?", {
      error: String(e),
    });
    console.error("CONNECT FAIL", e);
    process.exit(1);
  }

  log("H-A", "smoke.ts:connect", "both clients connected", { url });

  const stateA = await waitFor(a.inbox, (m) => m.type === "sync_state");
  const guildA = await waitFor(a.inbox, (m) => m.type === "sync_guild");
  const stateB = await waitFor(b.inbox, (m) => m.type === "sync_state");

  const monsters = (stateA?.monsters as { id: string }[] | undefined) ?? [];
  const npcs = (stateA?.npcs as { id: string }[] | undefined) ?? [];
  const youA = stateA?.you as { id: string } | undefined;
  const youB = stateB?.you as { id: string } | undefined;

  results.monsterCount = monsters.length;
  results.npcCount = npcs.length;
  results.hasMerchant = npcs.some((n) => n.id === "npc_merchant");
  results.hasShadow = monsters.some((m) => m.id === "monster_shadow_1");
  results.guildName = (guildA?.guildName as string) ?? "";
  results.okState =
    monsters.length >= 8 &&
    npcs.length >= 5 &&
    Boolean(results.hasMerchant) &&
    Boolean(results.hasShadow) &&
    results.guildName === "Ashen Legion";

  log("H-B", "smoke.ts:state", "sync_state checked", {
    monsterCount: monsters.length,
    npcCount: npcs.length,
    npcIds: npcs.map((n) => n.id),
    hasShadow: results.hasShadow,
    guildName: results.guildName,
    okState: results.okState,
  });

  if (!youA?.id || !youB?.id) {
    console.error("missing player ids");
    process.exit(1);
  }

  a.inbox.length = 0;
  b.inbox.length = 0;
  send(a.ws, { type: "request_party_invite", targetId: youB.id });
  const invite = await waitFor(b.inbox, (m) => m.type === "sync_party_invite");
  results.gotInvite = Boolean(invite);
  const inviteId = (invite?.inviteId as string) ?? "";

  log("H-C", "smoke.ts:invite", "invite observed on B", {
    gotInvite: results.gotInvite,
    inviteId,
  });

  b.inbox.length = 0;
  a.inbox.length = 0;
  send(b.ws, { type: "request_party_respond", inviteId, accept: true });
  const partyA = await waitFor(a.inbox, (m) => m.type === "sync_party" && Boolean(m.partyId));
  const partyB = await waitFor(b.inbox, (m) => m.type === "sync_party" && Boolean(m.partyId));
  results.partyJoined = Boolean(partyA && partyB && partyA.partyId === partyB.partyId);
  const members = (partyA?.members as { id: string }[] | undefined) ?? [];
  results.memberCount = members.length;

  log("H-C", "smoke.ts:accept", "party sync after accept", {
    partyJoined: results.partyJoined,
    partyId: partyA?.partyId ?? null,
    memberCount: members.length,
  });

  a.inbox.length = 0;
  b.inbox.length = 0;
  send(a.ws, { type: "request_chat", channel: "party", text: "party-ping" });
  const partyChat = await waitFor(b.inbox, (m) => m.type === "sync_chat" && m.channel === "party");
  results.partyChat = Boolean(partyChat);

  await wait(450);
  a.inbox.length = 0;
  b.inbox.length = 0;
  send(a.ws, { type: "request_chat", channel: "guild", text: "guild-ping" });
  const guildChat = await waitFor(b.inbox, (m) => m.type === "sync_chat" && m.channel === "guild");
  results.guildChat = Boolean(guildChat);

  log("H-D", "smoke.ts:chat", "chat routing", {
    partyChat: results.partyChat,
    guildChat: results.guildChat,
  });

  a.inbox.length = 0;
  b.inbox.length = 0;
  send(a.ws, { type: "request_party_leave" });
  const leftA = await waitFor(a.inbox, (m) => m.type === "sync_party");
  results.leftParty = leftA?.partyId == null || leftA.partyId === null;

  log("H-E", "smoke.ts:leave", "leave party", {
    leftParty: results.leftParty,
    partyId: leftA?.partyId ?? null,
  });

  a.ws.close();
  b.ws.close();

  const ok =
    results.okState === true &&
    results.gotInvite === true &&
    results.partyJoined === true &&
    results.memberCount === 2 &&
    results.partyChat === true &&
    results.guildChat === true &&
    results.leftParty === true;

  log("H-ALL", "smoke.ts:summary", ok ? "SMOKE PASS" : "SMOKE FAIL", { ...results, ok });
  console.log(ok ? "SMOKE PASS" : "SMOKE FAIL", results);
  process.exit(ok ? 0 : 1);
}

main().catch((e) => {
  log("H-A", "smoke.ts:crash", "smoke crashed", { error: String(e) });
  console.error(e);
  process.exit(1);
});
