import { createId, players } from "./world.js";
import { addItem, removeItem } from "./shop.js";
import type { PlayerSession, ServerMessage } from "./types.js";

export type TradeOfferSlot = { slotIndex: number; itemId: string; quantity: number };

export type TradeSide = {
  playerId: string;
  gold: number;
  slots: TradeOfferSlot[];
  confirmed: boolean;
};

export type TradeSession = {
  id: string;
  a: TradeSide;
  b: TradeSide;
};

type TradeInvite = {
  id: string;
  fromId: string;
  toId: string;
  expiresAt: number;
};

const trades = new Map<string, TradeSession>();
const playerTrade = new Map<string, string>();
const pending = new Map<string, TradeInvite>();

export function clearTrades(): void {
  trades.clear();
  playerTrade.clear();
  pending.clear();
}

function sideOf(trade: TradeSession, playerId: string): TradeSide | null {
  if (trade.a.playerId === playerId) {
    return trade.a;
  }
  if (trade.b.playerId === playerId) {
    return trade.b;
  }
  return null;
}

function otherSide(trade: TradeSession, playerId: string): TradeSide | null {
  if (trade.a.playerId === playerId) {
    return trade.b;
  }
  if (trade.b.playerId === playerId) {
    return trade.a;
  }
  return null;
}

function closedTradeMsg(): ServerMessage {
  return {
    type: "sync_trade",
    tradeId: null,
    you: { gold: 0, slots: [], confirmed: false },
    them: { gold: 0, slots: [], confirmed: false, name: "" },
  };
}

export function tradeSnapshotFor(trade: TradeSession, viewerId: string): ServerMessage {
  const you = sideOf(trade, viewerId)!;
  const them = otherSide(trade, viewerId)!;
  return {
    type: "sync_trade",
    tradeId: trade.id,
    you: {
      gold: you.gold,
      slots: you.slots,
      confirmed: you.confirmed,
    },
    them: {
      gold: them.gold,
      slots: them.slots,
      confirmed: them.confirmed,
      name: players.get(them.playerId)?.entity.name ?? them.playerId,
    },
  };
}

export function inviteTrade(
  from: PlayerSession,
  targetId: string,
  now: number,
): { error?: ServerMessage; toMsg?: ServerMessage } {
  if (targetId === from.entity.id) {
    return { error: { type: "error", code: "bad_invite", message: "Cannot trade yourself" } };
  }
  const target = players.get(targetId);
  if (!target) {
    return { error: { type: "error", code: "player_not_found", message: "Player not found" } };
  }
  if (from.entity.mapId !== target.entity.mapId) {
    return { error: { type: "error", code: "wrong_map", message: "Must be on same map" } };
  }
  if (Math.hypot(from.entity.x - target.entity.x, from.entity.y - target.entity.y) > 4) {
    return { error: { type: "error", code: "too_far", message: "Move closer to trade" } };
  }
  if (playerTrade.has(from.entity.id) || playerTrade.has(targetId)) {
    return { error: { type: "error", code: "busy", message: "Already in a trade" } };
  }
  for (const [id, inv] of pending) {
    if (inv.fromId === from.entity.id && inv.toId === targetId) {
      pending.delete(id);
    }
  }
  const invite: TradeInvite = {
    id: createId("tinv"),
    fromId: from.entity.id,
    toId: targetId,
    expiresAt: now + 60_000,
  };
  pending.set(invite.id, invite);
  return {
    toMsg: {
      type: "sync_trade_invite",
      inviteId: invite.id,
      fromId: from.entity.id,
      fromName: from.entity.name,
    },
  };
}

export function respondTradeInvite(
  session: PlayerSession,
  inviteId: string,
  accept: boolean,
  now: number,
): { error?: ServerMessage; syncs: { playerId: string; msg: ServerMessage }[] } {
  const inv = pending.get(inviteId);
  pending.delete(inviteId);
  if (!inv || inv.toId !== session.entity.id) {
    return { error: { type: "error", code: "invite_gone", message: "Trade invite gone" }, syncs: [] };
  }
  if (now > inv.expiresAt) {
    return { error: { type: "error", code: "invite_gone", message: "Trade invite expired" }, syncs: [] };
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
            text: `${session.entity.name} declined trade.`,
            serverTime: now,
          },
        },
      ],
    };
  }
  if (playerTrade.has(session.entity.id) || playerTrade.has(inv.fromId)) {
    return { error: { type: "error", code: "busy", message: "Already in a trade" }, syncs: [] };
  }
  const tradeId = createId("trade");
  const trade: TradeSession = {
    id: tradeId,
    a: { playerId: inv.fromId, gold: 0, slots: [], confirmed: false },
    b: { playerId: session.entity.id, gold: 0, slots: [], confirmed: false },
  };
  trades.set(tradeId, trade);
  playerTrade.set(inv.fromId, tradeId);
  playerTrade.set(session.entity.id, tradeId);
  return {
    syncs: [
      { playerId: inv.fromId, msg: tradeSnapshotFor(trade, inv.fromId) },
      { playerId: session.entity.id, msg: tradeSnapshotFor(trade, session.entity.id) },
    ],
  };
}

export function updateTradeOffer(
  session: PlayerSession,
  gold: number,
  offers: { slotIndex: number; quantity: number }[],
): { error?: ServerMessage; syncs: { playerId: string; msg: ServerMessage }[] } {
  const tradeId = playerTrade.get(session.entity.id);
  const trade = tradeId ? trades.get(tradeId) : undefined;
  if (!trade) {
    return { error: { type: "error", code: "no_trade", message: "Not in a trade" }, syncs: [] };
  }
  const side = sideOf(trade, session.entity.id)!;
  const g = Math.max(0, Math.min(session.gold, Math.floor(gold)));
  const slots: TradeOfferSlot[] = [];
  for (const o of offers.slice(0, 5)) {
    const inv = session.inventory.find((s) => s.slotIndex === o.slotIndex);
    if (!inv?.itemId || inv.quantity < o.quantity || o.quantity <= 0) {
      continue;
    }
    if (inv.itemId === "item_homestone") {
      continue;
    }
    slots.push({ slotIndex: o.slotIndex, itemId: inv.itemId, quantity: Math.floor(o.quantity) });
  }
  side.gold = g;
  side.slots = slots;
  trade.a.confirmed = false;
  trade.b.confirmed = false;
  return {
    syncs: [
      { playerId: trade.a.playerId, msg: tradeSnapshotFor(trade, trade.a.playerId) },
      { playerId: trade.b.playerId, msg: tradeSnapshotFor(trade, trade.b.playerId) },
    ],
  };
}

export function confirmTrade(
  session: PlayerSession,
): { error?: ServerMessage; syncs: { playerId: string; msg: ServerMessage }[]; done?: boolean } {
  const tradeId = playerTrade.get(session.entity.id);
  const trade = tradeId ? trades.get(tradeId) : undefined;
  if (!trade) {
    return { error: { type: "error", code: "no_trade", message: "Not in a trade" }, syncs: [] };
  }
  const side = sideOf(trade, session.entity.id)!;
  side.confirmed = true;
  if (!trade.a.confirmed || !trade.b.confirmed) {
    return {
      syncs: [
        { playerId: trade.a.playerId, msg: tradeSnapshotFor(trade, trade.a.playerId) },
        { playerId: trade.b.playerId, msg: tradeSnapshotFor(trade, trade.b.playerId) },
      ],
    };
  }
  const result = executeTrade(trade);
  if (result.error) {
    trade.a.confirmed = false;
    trade.b.confirmed = false;
    return {
      error: result.error,
      syncs: [
        { playerId: trade.a.playerId, msg: tradeSnapshotFor(trade, trade.a.playerId) },
        { playerId: trade.b.playerId, msg: tradeSnapshotFor(trade, trade.b.playerId) },
      ],
    };
  }
  const aId = trade.a.playerId;
  const bId = trade.b.playerId;
  cancelTradeInternal(trade.id);
  const a = players.get(aId);
  const b = players.get(bId);
  const syncs: { playerId: string; msg: ServerMessage }[] = [
    { playerId: aId, msg: closedTradeMsg() },
    { playerId: bId, msg: closedTradeMsg() },
  ];
  if (a) {
    syncs.push({ playerId: aId, msg: { type: "sync_inventory", inventory: a.inventory, gold: a.gold } });
  }
  if (b) {
    syncs.push({ playerId: bId, msg: { type: "sync_inventory", inventory: b.inventory, gold: b.gold } });
  }
  return { syncs, done: true };
}

function executeTrade(trade: TradeSession): { error?: ServerMessage } {
  const a = players.get(trade.a.playerId);
  const b = players.get(trade.b.playerId);
  if (!a || !b) {
    return { error: { type: "error", code: "player_not_found", message: "Trader offline" } };
  }
  if (a.gold < trade.a.gold || b.gold < trade.b.gold) {
    return { error: { type: "error", code: "not_enough_gold", message: "Not enough gold" } };
  }
  for (const s of trade.a.slots) {
    const inv = a.inventory.find((x) => x.slotIndex === s.slotIndex);
    if (!inv || inv.itemId !== s.itemId || inv.quantity < s.quantity) {
      return { error: { type: "error", code: "missing_item", message: "Offer invalid" } };
    }
  }
  for (const s of trade.b.slots) {
    const inv = b.inventory.find((x) => x.slotIndex === s.slotIndex);
    if (!inv || inv.itemId !== s.itemId || inv.quantity < s.quantity) {
      return { error: { type: "error", code: "missing_item", message: "Offer invalid" } };
    }
  }
  for (const s of trade.a.slots) {
    removeItem(a, s.itemId, s.quantity);
  }
  for (const s of trade.b.slots) {
    removeItem(b, s.itemId, s.quantity);
  }
  a.gold -= trade.a.gold;
  b.gold -= trade.b.gold;
  a.gold += trade.b.gold;
  b.gold += trade.a.gold;
  for (const s of trade.a.slots) {
    addItem(b, s.itemId, s.quantity);
  }
  for (const s of trade.b.slots) {
    addItem(a, s.itemId, s.quantity);
  }
  return {};
}

export function cancelTrade(session: PlayerSession): { syncs: { playerId: string; msg: ServerMessage }[] } {
  const tradeId = playerTrade.get(session.entity.id);
  if (!tradeId) {
    return { syncs: [] };
  }
  const trade = trades.get(tradeId);
  const ids = trade ? [trade.a.playerId, trade.b.playerId] : [session.entity.id];
  cancelTradeInternal(tradeId);
  return { syncs: ids.map((playerId) => ({ playerId, msg: closedTradeMsg() })) };
}

function cancelTradeInternal(tradeId: string): void {
  const trade = trades.get(tradeId);
  if (trade) {
    playerTrade.delete(trade.a.playerId);
    playerTrade.delete(trade.b.playerId);
  }
  trades.delete(tradeId);
}

export function onTradeDisconnect(playerId: string): { syncs: { playerId: string; msg: ServerMessage }[] } {
  for (const [id, inv] of pending) {
    if (inv.fromId === playerId || inv.toId === playerId) {
      pending.delete(id);
    }
  }
  const tradeId = playerTrade.get(playerId);
  if (!tradeId) {
    return { syncs: [] };
  }
  const trade = trades.get(tradeId);
  const ids = trade ? [trade.a.playerId, trade.b.playerId] : [playerId];
  cancelTradeInternal(tradeId);
  return { syncs: ids.map((id) => ({ playerId: id, msg: closedTradeMsg() })) };
}
