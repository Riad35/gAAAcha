import assert from "node:assert/strict";
import { test } from "node:test";
import { clearChatRateLimits, handleChat } from "./chat.js";
import { players, resetWorld, spawnPlayer } from "./world.js";

test("whisper routes to named player; map reaches same map", () => {
  resetWorld();
  clearChatRateLimits();
  const a = spawnPlayer("chat_a");
  const b = spawnPlayer("chat_b");
  a.entity.name = "Alpha";
  b.entity.name = "Beta";
  const now = Date.now();
  const whisper = handleChat(a, "whisper", "hi beta", "Beta", now);
  assert.equal(whisper.error, undefined);
  assert.equal(whisper.messages.length, 2);
  assert.ok(whisper.messages.every((m) => m.msg.type === "sync_chat" && m.msg.channel === "whisper"));

  clearChatRateLimits();
  const map = handleChat(a, "map", "map hello", undefined, now + 1000);
  assert.equal(map.error, undefined);
  assert.equal(map.messages.length, players.size);

  clearChatRateLimits();
  const guild = handleChat(a, "guild", "g?", undefined, now + 2000);
  assert.equal(guild.messages.length, 1);
  assert.ok(guild.messages[0].msg.type === "sync_chat" && guild.messages[0].msg.fromId === "system");
});
