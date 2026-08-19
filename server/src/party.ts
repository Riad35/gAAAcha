import { createId, players } from "./world.js";
import type { PlayerSession, ServerMessage } from "./types.js";

export type PartyInvite = {
  id: string;
  fromId: string;
  toId: string;
  expiresAt: number;
};

type GuildInvite = {
  id: string;
  fromId: string;
  toId: string;
  guildId: string;
  expiresAt: number;
};

const parties = new Map<string, Set<string>>();
const playerParty = new Map<string, string>();
const pendingInvites = new Map<string, PartyInvite>();

const guilds = new Map<string, { name: string; members: Set<string> }>();
const pendingGuildInvites = new Map<string, GuildInvite>();

export const DEFAULT_GUILD_ID = "guild_ashen";
export const DEFAULT_GUILD_NAME = "Ashen Legion";

guilds.set(DEFAULT_GUILD_ID, { name: DEFAULT_GUILD_NAME, members: new Set() });

export function clearSocial(): void {
  parties.clear();
  playerParty.clear();
  pendingInvites.clear();
  pendingGuildInvites.clear();
  for (const g of guilds.values()) {
    g.members.clear();
  }
  if (!guilds.has(DEFAULT_GUILD_ID)) {
    guilds.set(DEFAULT_GUILD_ID, { name: DEFAULT_GUILD_NAME, members: new Set() });
  }
}

export function ensureGuild(session: PlayerSession): void {
  if (!session.guildId) {
    session.guildId = DEFAULT_GUILD_ID;
  }
  let g = guilds.get(session.guildId);
  if (!g) {
    g = { name: session.guildId === DEFAULT_GUILD_ID ? DEFAULT_GUILD_NAME : "Guild", members: new Set() };
    guilds.set(session.guildId, g);
  }
  g.members.add(session.entity.id);
}

export function guildSnapshot(session: PlayerSession): ServerMessage {
  ensureGuild(session);
  const g = guilds.get(session.guildId!)!;
  return {
    type: "sync_guild",
    guildId: session.guildId!,
    guildName: g.name,
    members: [...g.members].map((id) => ({
      id,
      name: players.get(id)?.entity.name ?? id,
    })),
  };
}

export function getPartyId(playerId: string): string | null {
  return playerParty.get(playerId) ?? null;
}

export function getPartyMembers(partyId: string): string[] {
  return [...(parties.get(partyId) ?? [])];
}

export function partySnapshot(partyId: string | null): ServerMessage | null {
  if (!partyId || !parties.has(partyId)) {
    return { type: "sync_party", partyId: null, members: [] };
  }
  const members = [...parties.get(partyId)!].map((id) => {
    const p = players.get(id);
    return {
      id,
      name: p?.entity.name ?? id,
      hp: p?.entity.hp ?? 0,
      maxHp: p?.entity.maxHp ?? 1,
      mp: p?.entity.mp ?? 0,
      maxMp: p?.entity.maxMp ?? 1,
      level: p?.level ?? 1,
      classId: p?.classId ?? "adventurer",
    };
  });
  return { type: "sync_party", partyId, members };
}

export function inviteToParty(
  from: PlayerSession,
  targetId: string,
  now: number,
): { error?: ServerMessage; invite?: PartyInvite; toMsg?: ServerMessage; fromMsg?: ServerMessage } {
  if (targetId === from.entity.id) {
    return { error: { type: "error", code: "bad_invite", message: "Cannot invite yourself" } };
  }
  const target = players.get(targetId);
  if (!target) {
    return { error: { type: "error", code: "player_not_found", message: "Player not found" } };
  }

  const fromParty = playerParty.get(from.entity.id);
  const toParty = playerParty.get(targetId);
  if (toParty) {
    return { error: { type: "error", code: "already_in_party", message: "Target is already in a party" } };
  }
  if (fromParty && (parties.get(fromParty)?.size ?? 0) >= 4) {
    return { error: { type: "error", code: "party_full", message: "Party is full (max 4)" } };
  }

  for (const [id, inv] of pendingInvites) {
    if (inv.fromId === from.entity.id && inv.toId === targetId) {
      pendingInvites.delete(id);
    }
  }

  const invite: PartyInvite = {
    id: createId("pinv"),
    fromId: from.entity.id,
    toId: targetId,
    expiresAt: now + 60_000,
  };
  pendingInvites.set(invite.id, invite);

  const toMsg: ServerMessage = {
    type: "sync_party_invite",
    inviteId: invite.id,
    fromId: from.entity.id,
    fromName: from.entity.name,
  };
  return { invite, toMsg, fromMsg: toMsg };
}

export function respondPartyInvite(
  session: PlayerSession,
  inviteId: string,
  accept: boolean,
  now: number,
): { error?: ServerMessage; syncs: { playerId: string; msg: ServerMessage }[] } {
  const inv = pendingInvites.get(inviteId);
  pendingInvites.delete(inviteId);
  if (!inv || inv.toId !== session.entity.id) {
    return { error: { type: "error", code: "invite_gone", message: "Invite expired or invalid" }, syncs: [] };
  }
  if (now > inv.expiresAt) {
    return { error: { type: "error", code: "invite_gone", message: "Invite expired" }, syncs: [] };
  }
  if (!accept) {
    return {
      syncs: [
        {
          playerId: inv.fromId,
          msg: {
            type: "sync_chat",
            channel: "server",
            fromId: "system",
            fromName: "System",
            text: `${session.entity.name} declined your party invite.`,
            serverTime: now,
          },
        },
      ],
    };
  }

  if (playerParty.get(session.entity.id)) {
    return { error: { type: "error", code: "already_in_party", message: "You are already in a party" }, syncs: [] };
  }

  let partyId = playerParty.get(inv.fromId);
  if (!partyId) {
    partyId = createId("party");
    parties.set(partyId, new Set([inv.fromId]));
    playerParty.set(inv.fromId, partyId);
    const leader = players.get(inv.fromId);
    if (leader) {
      leader.partyId = partyId;
    }
  }

  const set = parties.get(partyId)!;
  if (set.size >= 4) {
    return { error: { type: "error", code: "party_full", message: "Party is full (max 4)" }, syncs: [] };
  }
  set.add(session.entity.id);
  playerParty.set(session.entity.id, partyId);
  session.partyId = partyId;

  const snap = partySnapshot(partyId)!;
  return {
    syncs: [...set].map((playerId) => ({ playerId, msg: snap })),
  };
}

export function leaveParty(session: PlayerSession): { syncs: { playerId: string; msg: ServerMessage }[] } {
  const partyId = playerParty.get(session.entity.id);
  if (!partyId) {
    return { syncs: [{ playerId: session.entity.id, msg: { type: "sync_party", partyId: null, members: [] } }] };
  }
  const set = parties.get(partyId);
  if (!set) {
    playerParty.delete(session.entity.id);
    session.partyId = null;
    return { syncs: [{ playerId: session.entity.id, msg: { type: "sync_party", partyId: null, members: [] } }] };
  }

  set.delete(session.entity.id);
  playerParty.delete(session.entity.id);
  session.partyId = null;

  const syncs: { playerId: string; msg: ServerMessage }[] = [
    { playerId: session.entity.id, msg: { type: "sync_party", partyId: null, members: [] } },
  ];

  if (set.size <= 1) {
    for (const id of set) {
      playerParty.delete(id);
      const p = players.get(id);
      if (p) {
        p.partyId = null;
      }
      syncs.push({ playerId: id, msg: { type: "sync_party", partyId: null, members: [] } });
    }
    parties.delete(partyId);
  } else {
    const snap = partySnapshot(partyId)!;
    for (const id of set) {
      syncs.push({ playerId: id, msg: snap });
    }
  }
  return { syncs };
}

export function onPlayerDisconnect(playerId: string): { syncs: { playerId: string; msg: ServerMessage }[] } {
  for (const [id, inv] of pendingGuildInvites) {
    if (inv.fromId === playerId || inv.toId === playerId) {
      pendingGuildInvites.delete(id);
    }
  }
  const session = players.get(playerId);
  if (session?.guildId) {
    guilds.get(session.guildId)?.members.delete(playerId);
  }
  if (!session) {
    const partyId = playerParty.get(playerId);
    playerParty.delete(playerId);
    if (partyId) {
      const set = parties.get(partyId);
      set?.delete(playerId);
      if (set && set.size <= 1) {
        for (const id of [...(set ?? [])]) {
          playerParty.delete(id);
        }
        parties.delete(partyId);
      }
    }
    for (const [id, inv] of pendingInvites) {
      if (inv.fromId === playerId || inv.toId === playerId) {
        pendingInvites.delete(id);
      }
    }
    return { syncs: [] };
  }
  return leaveParty(session);
}

export function createGuild(session: PlayerSession, name: string): { error?: ServerMessage; msg?: ServerMessage } {
  const cleaned = name.replace(/[^\w\s\-']/g, "").trim().slice(0, 24);
  if (cleaned.length < 3) {
    return { error: { type: "error", code: "bad_name", message: "Guild name too short" } };
  }
  if (session.guildId && session.guildId !== DEFAULT_GUILD_ID) {
    return { error: { type: "error", code: "in_guild", message: "Leave your guild first" } };
  }
  if (session.guildId === DEFAULT_GUILD_ID) {
    guilds.get(DEFAULT_GUILD_ID)?.members.delete(session.entity.id);
  }
  const id = createId("guild");
  guilds.set(id, { name: cleaned, members: new Set([session.entity.id]) });
  session.guildId = id;
  return { msg: guildSnapshot(session) };
}

export function inviteToGuild(
  from: PlayerSession,
  targetId: string,
  now: number,
): { error?: ServerMessage; toMsg?: ServerMessage } {
  ensureGuild(from);
  if (!from.guildId || from.guildId === DEFAULT_GUILD_ID) {
    return { error: { type: "error", code: "no_guild", message: "Create a guild first (not Ashen Legion stub)" } };
  }
  const target = players.get(targetId);
  if (!target || targetId === from.entity.id) {
    return { error: { type: "error", code: "player_not_found", message: "Player not found" } };
  }
  if (target.guildId && target.guildId !== DEFAULT_GUILD_ID) {
    return { error: { type: "error", code: "in_guild", message: "Target already in a guild" } };
  }
  const invite: GuildInvite = {
    id: createId("ginv"),
    fromId: from.entity.id,
    toId: targetId,
    guildId: from.guildId,
    expiresAt: now + 60_000,
  };
  pendingGuildInvites.set(invite.id, invite);
  const g = guilds.get(from.guildId)!;
  return {
    toMsg: {
      type: "sync_guild_invite",
      inviteId: invite.id,
      fromId: from.entity.id,
      fromName: from.entity.name,
      guildName: g.name,
    },
  };
}

export function respondGuildInvite(
  session: PlayerSession,
  inviteId: string,
  accept: boolean,
  now: number,
): { error?: ServerMessage; syncs: { playerId: string; msg: ServerMessage }[] } {
  const inv = pendingGuildInvites.get(inviteId);
  pendingGuildInvites.delete(inviteId);
  if (!inv || inv.toId !== session.entity.id) {
    return { error: { type: "error", code: "invite_gone", message: "Guild invite gone" }, syncs: [] };
  }
  if (now > inv.expiresAt || !accept) {
    return { syncs: [] };
  }
  const g = guilds.get(inv.guildId);
  if (!g) {
    return { error: { type: "error", code: "invite_gone", message: "Guild gone" }, syncs: [] };
  }
  if (session.guildId && session.guildId !== DEFAULT_GUILD_ID) {
    return { error: { type: "error", code: "in_guild", message: "Already in a guild" }, syncs: [] };
  }
  if (session.guildId === DEFAULT_GUILD_ID) {
    guilds.get(DEFAULT_GUILD_ID)?.members.delete(session.entity.id);
  }
  session.guildId = inv.guildId;
  g.members.add(session.entity.id);
  const snap = guildSnapshot(session);
  return {
    syncs: [...g.members].map((playerId) => ({ playerId, msg: snap })),
  };
}

export function leaveGuild(session: PlayerSession): { msg: ServerMessage } {
  if (session.guildId) {
    guilds.get(session.guildId)?.members.delete(session.entity.id);
  }
  session.guildId = DEFAULT_GUILD_ID;
  ensureGuild(session);
  return { msg: guildSnapshot(session) };
}
