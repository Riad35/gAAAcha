import assert from "node:assert/strict";
import { test } from "node:test";
import {
  channelForError,
  formatLogLine,
  getRingSnapshot,
  log,
  packetRejectReason,
  redactSecret,
  resetLogForTests,
  setMinLevel,
  shouldLog,
} from "./log.js";

test("formatLogLine is grep-friendly and includes ctx", () => {
  const line = formatLogLine(
    "2026-08-20T01:18:00.412Z",
    "WARN",
    "GFX",
    "slice failed",
    { sheet: "swordsman_idle", reason: "width_not_divisible", tex: "256x256" },
  );
  assert.equal(
    line,
    "2026-08-20T01:18:00.412Z  WARN   GFX      slice failed  sheet=swordsman_idle  reason=width_not_divisible  tex=256x256",
  );
});

test("INFO min level skips DEBUG", () => {
  resetLogForTests();
  setMinLevel("INFO");
  assert.equal(shouldLog("INFO"), true);
  assert.equal(shouldLog("WARN"), true);
  assert.equal(shouldLog("DEBUG"), false);
  assert.equal(shouldLog("TRACE"), false);
});

test("INFO line never embeds a full packet JSON blob", () => {
  const line = formatLogLine("2026-08-20T01:18:00.412Z", "INFO", "NET", "recv", {
    type: "sync_state",
  });
  assert.match(line, /type=sync_state/);
  assert.doesNotMatch(line, /\{/);
  assert.doesNotMatch(line, /"you"/);
});

test("guest tokens are redacted in ctx", () => {
  const line = formatLogLine("2026-08-20T01:18:00.412Z", "INFO", "SYS", "player joined", {
    entity: "player_1",
    guest: "guest_abcdefghijklmnopqrstuvwxyz",
  });
  assert.match(line, /guest=guest_ab…/);
  assert.doesNotMatch(line, /abcdefghijklmnopqrstuvwxyz/);
});

test("redactSecret keeps a short prefix", () => {
  assert.equal(redactSecret("abcdefghijkl"), "abcdefgh…");
  assert.equal(redactSecret("ab"), "ab…");
});

test("channelForError maps combat vs world vs net", () => {
  assert.equal(channelForError("out_of_range"), "COMBAT");
  assert.equal(channelForError("on_cooldown"), "COMBAT");
  assert.equal(channelForError("bad_portal"), "WORLD");
  assert.equal(channelForError("bad_packet"), "NET");
  assert.equal(channelForError("inventory_full"), "UI");
  assert.equal(channelForError("party_full"), "SOCIAL");
});

test("packetRejectReason explains comma decimals vs unknown type", () => {
  const comma = packetRejectReason('{"type":"request_move","x":6,5,"y":10}');
  assert.match(comma.why, /comma/);
  assert.equal(comma.type, "(unparseable)");
  const unknown = packetRejectReason('{"type":"request_teleport_moon"}');
  assert.equal(unknown.type, "request_teleport_moon");
  assert.match(unknown.why, /unknown or incomplete/);
});

test("log.info writes to the ring at INFO and skips DEBUG", () => {
  resetLogForTests();
  setMinLevel("INFO");
  log.info("COMBAT", "cast", { skill: "slash", target: "lab_melee_1", dmg: 8 });
  log.debug("NET", "recv", { type: "sync_move" });
  const ring = getRingSnapshot();
  assert.equal(ring.length, 1);
  assert.match(ring[0], /COMBAT/);
  assert.match(ring[0], /skill=slash/);
  assert.doesNotMatch(ring[0], /sync_move/);
});

test("craftpix slice rule: square cells from height/4, width must divide", () => {
  const texW = 256;
  const texH = 256;
  const frameH = texH / 4;
  const frameW = frameH;
  assert.equal(frameW, 64);
  assert.equal(texH % 4, 0);
  assert.equal(texW % frameW, 0);
  const badW = 250;
  assert.notEqual(badW % frameW, 0);
});
