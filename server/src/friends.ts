import { players } from "./world.js";
import type { FriendEntry, PlayerSession, ServerMessage } from "./types.js";

const MAX_FRIENDS = 40;

export function friendsSnapshot(session: PlayerSession): ServerMessage {
  const entries = session.friends.map((f) => {
    const online = [...players.values()].find((p) => p.guestToken === f.guestToken);
    return {
      guestToken: f.guestToken,
      name: online?.entity.name ?? f.name,
      online: Boolean(online),
      playerId: online?.entity.id,
    };
  });
  return { type: "sync_friends", friends: entries };
}

export function addFriend(session: PlayerSession, targetId: string): { error?: ServerMessage; ok?: true } {
  const target = players.get(targetId);
  if (!target || target.entity.id === session.entity.id) {
    return { error: { type: "error", code: "player_not_found", message: "Player not found" } };
  }
  if (session.friends.some((f) => f.guestToken === target.guestToken)) {
    return { error: { type: "error", code: "already_friend", message: "Already on friends list" } };
  }
  if (session.friends.length >= MAX_FRIENDS) {
    return { error: { type: "error", code: "friends_full", message: "Friends list full" } };
  }
  session.friends.push({ guestToken: target.guestToken, name: target.entity.name });
  return { ok: true };
}

export function removeFriend(session: PlayerSession, guestToken: string): { error?: ServerMessage; ok?: true } {
  const before = session.friends.length;
  session.friends = session.friends.filter((f) => f.guestToken !== guestToken);
  if (session.friends.length === before) {
    return { error: { type: "error", code: "not_friend", message: "Not on friends list" } };
  }
  return { ok: true };
}

export function normalizeFriends(list: FriendEntry[] | undefined): FriendEntry[] {
  if (!Array.isArray(list)) {
    return [];
  }
  return list
    .filter((f) => f && typeof f.guestToken === "string" && typeof f.name === "string")
    .slice(0, MAX_FRIENDS);
}
