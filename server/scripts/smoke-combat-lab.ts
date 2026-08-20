/**
 * Smoke: combat lab zones present + melee slash deals damage.
 * Run: npx tsx server/scripts/smoke-combat-lab.ts
 */
import WebSocket from "ws";

const url = process.env.WS_URL ?? "ws://127.0.0.1:7777";

type Msg = Record<string, unknown> & { type: string };

function wait(ms: number) {
  return new Promise((r) => setTimeout(r, ms));
}

function send(ws: WebSocket, payload: unknown) {
  ws.send(JSON.stringify(payload));
}

async function waitFor(inbox: Msg[], pred: (m: Msg) => boolean, timeoutMs = 4000): Promise<Msg | null> {
  const start = Date.now();
  while (Date.now() - start < timeoutMs) {
    const hit = inbox.find(pred);
    if (hit) return hit;
    await wait(40);
  }
  return null;
}

async function main() {
  const inbox: Msg[] = [];
  const ws = new WebSocket(url);
  await new Promise<void>((resolve, reject) => {
    ws.on("open", () => resolve());
    ws.on("error", reject);
  });
  ws.on("message", (buf) => {
    try {
      inbox.push(JSON.parse(buf.toString()) as Msg);
    } catch {
      /* ignore */
    }
  });

  const state = await waitFor(inbox, (m) => m.type === "sync_state");
  if (!state) {
    console.error("FAIL: no sync_state");
    process.exit(1);
  }

  const you = state.you as { id: string; mapId: string; x: number; y: number };
  const map = state.map as { id?: string; width?: number; height?: number } | undefined;
  const monsters = (state.monsters as { id: string; x: number; y: number; hp: number }[]) ?? [];

  console.log("map", you.mapId, map?.width, "x", map?.height, "monsters", monsters.length);

  const by = (pred: (m: (typeof monsters)[0]) => boolean) => monsters.find(pred);
  const zones = {
    mapOk: you.mapId === "test_arena" && (map?.width ?? 0) >= 40 && (map?.height ?? 0) >= 28,
    melee: by((m) => m.id.includes("lab_melee") || m.id === "monster_lab_melee_1"),
    // respawnIds become entity ids — check world spawn naming
    ranged: by((m) => m.id.includes("ranged") || m.id.includes("ember") || m.id.includes("gust")),
    dummy: by((m) => m.id.includes("dummy") || m.hp >= 90000),
    ragdoll: by((m) => m.id.includes("ragdoll")),
    chase: by((m) => m.id.includes("chase")),
    cannon: by((m) => m.id.includes("cannon")),
    variety: by((m) => m.id.includes("pest") || m.id.includes("brute") || m.id.includes("shadow")),
  };

  console.log(
    "ids",
    monsters.map((m) => m.id),
  );

  const melee =
    zones.melee ??
    by((m) => m.x >= 10 && m.x <= 16 && m.y >= 4 && m.y <= 8 && m.hp < 100);

  if (!melee) {
    console.error("FAIL: no melee slime");
    process.exit(1);
  }

  const hpBefore = melee.hp;
  inbox.length = 0;

  // Walk in small steps so we don't trip too_fast / walls.
  async function walkTo(tx: number, ty: number) {
    const step = 0.9;
    for (let i = 0; i < 40; i++) {
      const lastYou = [...inbox].reverse().find((m) => m.type === "sync_move" && m.entityId === you.id) as
        | { x: number; y: number }
        | undefined;
      const curX = lastYou?.x ?? (i === 0 ? you.x : tx);
      const curY = lastYou?.y ?? (i === 0 ? you.y : ty);
      const dx = tx - curX;
      const dy = ty - curY;
      const dist = Math.hypot(dx, dy);
      if (dist < 0.35) break;
      const nx = curX + (dx / dist) * Math.min(step, dist);
      const ny = curY + (dy / dist) * Math.min(step, dist);
      send(ws, { type: "request_move", x: nx, y: ny });
      await wait(120);
    }
  }

  // Hub → through gap → melee pad approach
  await walkTo(9, 14);
  await walkTo(12, 14);
  await walkTo(12, 8);
  await walkTo(melee.x - 1.15, melee.y);
  await wait(200);

  inbox.length = 0;
  send(ws, { type: "cast_skill", skillId: "slash", targetId: melee.id });
  await wait(600);

  const skill = await waitFor(
    inbox,
    (m) => m.type === "sync_skill" && (m.skillId === "slash" || Number(m.damage ?? 0) > 0),
    2000,
  );
  const vitals = inbox.filter((m) => m.type === "sync_vitals" && m.entityId === melee.id);
  const errs = inbox.filter((m) => m.type === "error").map((m) => m.code);

  let dmg = Number(skill?.damage ?? 0);
  const results = skill?.results as { damage?: number }[] | undefined;
  if (results) {
    for (const r of results) dmg += Number(r.damage ?? 0);
  }
  for (const v of vitals) {
    if (typeof v.hp === "number" && v.hp < hpBefore) {
      dmg = Math.max(dmg, hpBefore - (v.hp as number));
    }
  }

  const report = {
    ...Object.fromEntries(
      Object.entries(zones).map(([k, v]) => [k, Boolean(v)]),
    ),
    meleeId: melee.id,
    damage: dmg,
    errors: errs,
  };
  console.log("SMOKE", JSON.stringify(report, null, 2));

  const ok =
    report.mapOk &&
    report.melee &&
    report.dummy &&
    report.ragdoll &&
    report.cannon &&
    report.chase &&
    report.variety &&
    report.ranged &&
    dmg > 0 &&
    !errs.some((c) => c === "invalid_target" || c === "unknown_skill");

  console.log(ok ? "PASS" : "FAIL");
  ws.close();
  process.exit(ok ? 0 : 1);
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
