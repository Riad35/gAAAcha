import { createId, players } from "./world.js";
import { addItem, removeItem } from "./shop.js";
import type { PlayerSession, ServerMessage } from "./types.js";

export type AuctionListing = {
  id: string;
  sellerToken: string;
  sellerName: string;
  itemId: string;
  quantity: number;
  price: number;
  createdAt: number;
};

const listings = new Map<string, AuctionListing>();
const MAX_LISTINGS = 40;

export function clearAuction(): void {
  listings.clear();
}

export function auctionSnapshot(): ServerMessage {
  return {
    type: "sync_auction",
    listings: [...listings.values()].map((l) => ({
      id: l.id,
      sellerName: l.sellerName,
      itemId: l.itemId,
      quantity: l.quantity,
      price: l.price,
    })),
  };
}

export function listAuctionItem(
  session: PlayerSession,
  itemId: string,
  quantity: number,
  price: number,
): { error?: ServerMessage; msg?: ServerMessage } {
  const qty = Math.max(1, Math.min(20, Math.floor(quantity)));
  const cost = Math.max(1, Math.floor(price));
  if (listings.size >= MAX_LISTINGS) {
    return { error: { type: "error", code: "auction_full", message: "Auction board full" } };
  }
  if (itemId === "item_homestone") {
    return { error: { type: "error", code: "bad_item", message: "Cannot list Homestone" } };
  }
  if (!removeItem(session, itemId, qty)) {
    return { error: { type: "error", code: "missing_item", message: "Not enough items" } };
  }
  const id = createId("auc");
  listings.set(id, {
    id,
    sellerToken: session.guestToken,
    sellerName: session.entity.name,
    itemId,
    quantity: qty,
    price: cost,
    createdAt: Date.now(),
  });
  return { msg: auctionSnapshot() };
}

export function buyAuction(
  session: PlayerSession,
  listingId: string,
): {
  error?: ServerMessage;
  buyerMsgs: ServerMessage[];
  sellerId?: string;
  sellerGold?: number;
} {
  const listing = listings.get(listingId);
  if (!listing) {
    return { error: { type: "error", code: "gone", message: "Listing gone" }, buyerMsgs: [] };
  }
  if (listing.sellerToken === session.guestToken) {
    return { error: { type: "error", code: "own_listing", message: "Cannot buy your own listing" }, buyerMsgs: [] };
  }
  if (session.gold < listing.price) {
    return { error: { type: "error", code: "not_enough_gold", message: "Not enough gold" }, buyerMsgs: [] };
  }
  if (!addItem(session, listing.itemId, listing.quantity)) {
    return { error: { type: "error", code: "inventory_full", message: "Inventory full" }, buyerMsgs: [] };
  }
  session.gold -= listing.price;
  listings.delete(listingId);
  const seller = [...players.values()].find((p) => p.guestToken === listing.sellerToken);
  if (seller) {
    seller.gold += listing.price;
    return {
      buyerMsgs: [
        auctionSnapshot(),
        { type: "sync_inventory", inventory: session.inventory, gold: session.gold },
      ],
      sellerId: seller.entity.id,
      sellerGold: seller.gold,
    };
  }
  return {
    buyerMsgs: [
      auctionSnapshot(),
      { type: "sync_inventory", inventory: session.inventory, gold: session.gold },
    ],
  };
}

export function cancelAuctionListing(
  session: PlayerSession,
  listingId: string,
): { error?: ServerMessage; msgs?: ServerMessage[] } {
  const listing = listings.get(listingId);
  if (!listing || listing.sellerToken !== session.guestToken) {
    return { error: { type: "error", code: "gone", message: "Not your listing" } };
  }
  if (!addItem(session, listing.itemId, listing.quantity)) {
    return { error: { type: "error", code: "inventory_full", message: "Inventory full" } };
  }
  listings.delete(listingId);
  return {
    msgs: [
      auctionSnapshot(),
      { type: "sync_inventory", inventory: session.inventory, gold: session.gold },
    ],
  };
}
