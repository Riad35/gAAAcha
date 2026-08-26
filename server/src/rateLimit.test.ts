import assert from "node:assert/strict";
import { test } from "node:test";
import { META_RPC_PER_SEC, metaRpcLimited } from "./rateLimit.js";

test("metaRpcLimited ignores move and cast", () => {
  const session = { rpcTimes: [] as number[] };
  const now = 1_000_000;
  assert.equal(metaRpcLimited(session, "request_move", now), false);
  assert.equal(metaRpcLimited(session, "cast_skill", now), false);
  assert.equal(metaRpcLimited(session, "request_ping", now), false);
  assert.equal(session.rpcTimes.length, 0);
});

test("metaRpcLimited caps other request_* at META_RPC_PER_SEC", () => {
  const session = { rpcTimes: [] as number[] };
  const now = 2_000_000;
  for (let i = 0; i < META_RPC_PER_SEC; i += 1) {
    assert.equal(metaRpcLimited(session, "request_gacha", now), false);
  }
  assert.equal(metaRpcLimited(session, "request_gacha", now), true);
  assert.equal(metaRpcLimited(session, "request_shop_buy", now), true);
  assert.equal(metaRpcLimited(session, "request_gacha", now + 1001), false);
});
