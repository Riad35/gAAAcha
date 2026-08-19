import type { ChatChannel, PlayerSession, ServerMessage } from "./types.js";
import { DEFAULT_GUILD_ID, getPartyId, getPartyMembers } from "./party.js";
import { players } from "./world.js";

const lastChatAt = new Map<string, number>();
const CHAT_COOLDOWN_MS = 400;
const MAX_LEN = 120;

export function handleChat(
  session: PlayerSession,
  channel: ChatChannel,
  text: string,
  targetName: string | undefined,
  now: number,
): { messages: { wsId: string; msg: ServerMessage }[]; error?: ServerMessage } {
  const cleaned = text.replace(/[\u0000-\u001f]/g, "").trim().slice(0, MAX_LEN);
  if (!cleaned) {
    return { messages: [], error: { type: "error", code: "empty_chat", message: "Empty message" } };
  }

  const last = lastChatAt.get(session.entity.id) ?? 0;
  if (now - last < CHAT_COOLDOWN_MS) {
    return { messages: [], error: { type: "error", code: "rate_limited", message: "Chat too fast" } };
  }
  lastChatAt.set(session.entity.id, now);

  const payload: ServerMessage = {
    type: "sync_chat",
    channel,
    fromId: session.entity.id,
    fromName: session.entity.name,
    text: cleaned,
    serverTime: now,
  };

  if (channel === "whisper") {
    const name = (targetName ?? "").trim().toLowerCase();
    if (!name) {
      return { messages: [], error: { type: "error", code: "no_whisper_target", message: "Whisper needs a name" } };
    }
    const target = [...players.values()].find((p) => p.entity.name.toLowerCase() === name);
    if (!target) {
      return { messages: [], error: { type: "error", code: "player_not_found", message: "Player not found" } };
    }
    const whisper: ServerMessage = {
      ...payload,
      channel: "whisper",
      targetId: target.entity.id,
    };
    return {
      messages: [
        { wsId: session.entity.id, msg: whisper },
        { wsId: target.entity.id, msg: whisper },
      ],
    };
  }

  if (channel === "party") {
    const partyId = getPartyId(session.entity.id);
    if (!partyId) {
      return {
        messages: [
          {
            wsId: session.entity.id,
            msg: {
              type: "sync_chat",
              channel: "party",
              fromId: "system",
              fromName: "System",
              text: "You are not in a party.",
              serverTime: now,
            },
          },
        ],
      };
    }
    return {
      messages: getPartyMembers(partyId).map((wsId) => ({ wsId, msg: payload })),
    };
  }

  if (channel === "guild") {
    const guildId = session.guildId ?? DEFAULT_GUILD_ID;
    const recipients = [...players.values()]
      .filter((p) => (p.guildId ?? DEFAULT_GUILD_ID) === guildId)
      .map((p) => p.entity.id);
    return {
      messages: recipients.map((wsId) => ({ wsId, msg: payload })),
    };
  }

  const recipients: string[] = [];
  for (const other of players.values()) {
    if (channel === "map" && other.entity.mapId !== session.entity.mapId) {
      continue;
    }
    recipients.push(other.entity.id);
  }

  return {
    messages: recipients.map((wsId) => ({ wsId, msg: payload })),
  };
}

export function clearChatRateLimits(): void {
  lastChatAt.clear();
}
