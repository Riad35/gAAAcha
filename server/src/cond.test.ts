import assert from "node:assert/strict";
import { test } from "node:test";
import { condChanged, HEARTBEAT_STALE_MS, isSessionStale, sessionCond, takeCondSync } from "./cond.js";
import type { Entity, PlayerSession } from "./types.js";

function stub(over: Partial<PlayerSession> = {}): PlayerSession {
  return {
    entity: { id: "p1", hp: 100 } as Entity,
    statuses: [],
    moveLockUntil: 0,
    ...over,
  } as PlayerSession;
}

test("healthy session can move and act", () => {
  const now = 1_000;
  const c = sessionCond(stub(), now);
  assert.deepEqual(c, { canMove: true, canAct: true, resting: false });
});

test("stun blocks move and act", () => {
  const now = 1_000;
  const c = sessionCond(
    stub({ statuses: [{ id: "stun", kind: "stun", until: now + 500 }] }),
    now,
  );
  assert.equal(c.canMove, false);
  assert.equal(c.canAct, false);
  assert.equal(c.resting, false);
});

test("shove lock blocks move but not act", () => {
  const now = 1_000;
  const c = sessionCond(stub({ moveLockUntil: now + 250 }), now);
  assert.equal(c.canMove, false);
  assert.equal(c.canAct, true);
});

test("resting is flagged; walk and skills still allowed (rest cancels on act)", () => {
  const now = 1_000;
  const c = sessionCond(
    stub({ statuses: [{ id: "resting", kind: "buff", until: now + 60_000 }] }),
    now,
  );
  assert.equal(c.resting, true);
  assert.equal(c.canMove, true);
  assert.equal(c.canAct, true);
});

test("dead blocks move and act", () => {
  const c = sessionCond(stub({ entity: { id: "p1", hp: 0 } as Entity }), 1_000);
  assert.equal(c.canMove, false);
  assert.equal(c.canAct, false);
});

test("takeCondSync emits once per flag change", () => {
  const s = stub();
  const a = takeCondSync(s, 1_000);
  assert.equal(a?.type, "sync_cond");
  assert.equal(takeCondSync(s, 1_001), null);
  s.moveLockUntil = 2_000;
  const b = takeCondSync(s, 1_500);
  assert.equal(b?.type, "sync_cond");
  if (b?.type === "sync_cond") {
    assert.equal(b.canMove, false);
    assert.equal(b.canAct, true);
  }
});

test("condChanged detects resting flip", () => {
  assert.equal(
    condChanged({ canMove: true, canAct: true, resting: false }, { canMove: true, canAct: true, resting: true }),
    true,
  );
});

test("stale after HEARTBEAT_STALE_MS; missing lastHeard is not stale", () => {
  const now = 50_000;
  assert.equal(isSessionStale(undefined, now), false);
  assert.equal(isSessionStale(now - 1_000, now), false);
  assert.equal(isSessionStale(now - HEARTBEAT_STALE_MS - 1, now), true);
});
