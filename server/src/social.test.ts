import assert from "node:assert/strict";
import { test } from "node:test";
import { clearChatRateLimits, handleChat } from "./chat.js";
import { clearSocial, ensureGuild, inviteToParty, respondPartyInvite } from "./party.js";
import { liveMonsters, liveNpcs, players, resetWorld, spawnPlayer } from "./world.js";

test("whisper routes to named player; map reaches same map", () => {
  resetWorld();
  clearChatRateLimits();
  clearSocial();
  const a = spawnPlayer("chat_a");
  const b = spawnPlayer("chat_b");
  ensureGuild(a);
  ensureGuild(b);
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
  assert.equal(guild.error, undefined);
  assert.equal(guild.messages.length, 2);
});

test("party invite accept joins both members", () => {
  resetWorld();
  clearSocial();
  const a = spawnPlayer("pa");
  const b = spawnPlayer("pb");
  ensureGuild(a);
  ensureGuild(b);
  const now = Date.now();
  const inv = inviteToParty(a, b.entity.id, now);
  assert.ok(inv.invite);
  const resp = respondPartyInvite(b, inv.invite!.id, true, now + 1);
  assert.equal(resp.error, undefined);
  assert.ok(resp.syncs.some((s) => s.msg.type === "sync_party" && s.msg.partyId && s.msg.members.length === 2));
  assert.equal(a.partyId, b.partyId);
  assert.ok(a.partyId);

  clearChatRateLimits();
  const chat = handleChat(a, "party", "ready?", undefined, now + 2);
  assert.equal(chat.messages.length, 2);
});

test("world has extra monsters and npcs", () => {
  resetWorld();
  assert.ok(liveMonsters.size >= 8);
  assert.ok(liveNpcs.has("npc_weapon"));
  assert.ok(liveNpcs.has("npc_homestone"));
  assert.ok(liveMonsters.has("monster_shadow_1"));
  assert.ok(liveMonsters.has("monster_pest_1"));
});
