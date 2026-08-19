import { WebSocket, WebSocketServer } from "ws";
import { handleChat } from "./chat.js";
import { bindCombatWorld, validateCast, validateMove } from "./combat.js";
import { pullGacha } from "./gacha.js";
import {
  createGuild,
  ensureGuild,
  guildSnapshot,
  inviteToGuild,
  inviteToParty,
  leaveGuild,
  leaveParty,
  onPlayerDisconnect,
  partySnapshot,
  respondGuildInvite,
  respondPartyInvite,
} from "./party.js";
import { addFriend, friendsSnapshot, removeFriend } from "./friends.js";
import {
  cancelTrade,
  confirmTrade,
  inviteTrade,
  onTradeDisconnect,
  respondTradeInvite,
  updateTradeOffer,
} from "./trade.js";
import { setHomestone, teleportHome, usePortal } from "./portal.js";
import { acceptQuest, noteTalk, questsForNpc, turnInQuest } from "./quest.js";
import { buyFromShop, sellToShop, useInventoryItem } from "./shop.js";
import { saveGuest } from "./persist.js";
import type { ChatChannel, ClientMessage, PlayerSession, ServerMessage } from "./types.js";
import { deleteCharacterDb, isDbReady, listCharactersDb, loadGuestSlotFromDb, loginAccount, registerAccount } from "./db.js";
import { deleteCharSlot, listCharSlots, loadCharSlot, SERVER_LIST } from "./chars.js";
import { xpToNextLevel } from "./xp.js";
import {
  buildInspect,
  bumpTowerFloor,
  changeClass,
  checkBossPhase,
  cooldownSnapshot,
  createCharacter,
  currentPityView,
  applyGearStats,
  equipGear,
  equipSpirit,
  equipWeapon,
  findEntity,
  killMonster,
  liveMonsters,
  liveNpcs,
  clampImmortalHp,
  isImmortalMonster,
  monsterStatuses,
  notePlayerDamageThreat,
  npcInteract,
  npcLines,
  npcSwitchIds,
  onMonsterKilledBy,
  players,
  snapshot,
  spawnPlayer,
  spawnProjectileFromCast,
  statusOf,
  swapWeapons,
  tickProjectiles,
  tickWorld,
} from "./world.js";
import { auctionSnapshot, buyAuction, cancelAuctionListing, listAuctionItem } from "./auction.js";
import { instanceSyncMsg } from "./instance.js";
import { skillTreeSnapshot, unlockSkill } from "./skills.js";
import { classById, defaultClass, shopByNpcId, skillById } from "./data.js";

bindCombatWorld(
  findEntity,
  () => [...players.values()],
  (id, status) => {
    const list = monsterStatuses.get(id) ?? [];
    monsterStatuses.set(id, [...list.filter((s) => s.id !== status.id), status]);
  },
  () => [...liveMonsters.values()].filter((monster) => monster.hp > 0),
  () => [...liveNpcs.values()],
  clampImmortalHp,
);

const sockets = new Map<string, WebSocket>();

function send(ws: WebSocket, message: ServerMessage): void {
  if (ws.readyState === WebSocket.OPEN) {
    ws.send(JSON.stringify(message));
  }
}

function sendTo(playerId: string, message: ServerMessage): void {
  const ws = sockets.get(playerId);
  if (ws) {
    send(ws, message);
  }
}

function broadcast(message: ServerMessage): void {
  const raw = JSON.stringify(message);
  for (const socket of sockets.values()) {
    if (socket.readyState === WebSocket.OPEN) {
      socket.send(raw);
    }
  }
}

function broadcastAll(messages: ServerMessage[]): void {
  for (const message of messages) {
    broadcast(message);
  }
}

function vitalsOf(entityId: string): ServerMessage | null {
  const entity = findEntity(entityId) ?? liveMonsters.get(entityId) ?? players.get(entityId)?.entity;
  if (!entity) {
    return null;
  }
  return {
    type: "sync_vitals",
    entityId: entity.id,
    hp: entity.hp,
    maxHp: entity.maxHp,
    mp: entity.mp,
    maxMp: entity.maxMp,
  };
}

function broadcastStatus(entityId: string, now: number): void {
  const msg = statusOf(entityId, now);
  if (msg) {
    broadcast(msg);
  }
}

function persist(session: PlayerSession): void {
  if (!session.inWorld || !session.charNameSet) {
    return;
  }
  saveGuest({
    guestToken: session.guestToken,
    characterId: session.characterId,
    slotIndex: session.slotIndex,
    classId: session.classId,
    name: session.entity.name,
    mapId: session.entity.mapId.includes("#")
      ? session.entity.mapId.slice(0, session.entity.mapId.indexOf("#"))
      : session.entity.mapId,
    x: session.entity.x,
    y: session.entity.y,
    hp: session.entity.hp,
    mp: session.entity.mp,
    inventory: session.inventory,
    pity: session.pity,
    equippedWeaponId: session.equippedWeaponId,
    equippedWeapon2Id: session.equippedWeapon2Id,
    weaponIds: session.weaponIds,
    equippedSpiritId: session.equippedSpiritId,
    spiritIds: session.spiritIds,
    gold: session.gold,
    homeMapId: session.homeMapId,
    homeX: session.homeX,
    homeY: session.homeY,
    quests: session.quests,
    completedQuestIds: session.completedQuestIds,
    charNameSet: session.charNameSet,
    level: session.level,
    xp: session.xp,
    equippedArmorId: session.equippedArmorId,
    equippedHelmId: session.equippedHelmId,
    equippedBootsId: session.equippedBootsId,
    equippedGlovesId: session.equippedGlovesId,
    equippedAccessoryId: session.equippedAccessoryId,
    friends: session.friends,
    skillPoints: session.skillPoints,
    unlockedSkillIds: session.unlockedSkillIds,
    classCardId: session.classCardId,
    towerClearedFloor: session.towerClearedFloor,
    switchFlags: session.switchFlags,
    updatedAt: Date.now(),
  });
}

async function resolveCharList(token: string) {
  if (isDbReady()) {
    const fromDb = await listCharactersDb(token);
    if (fromDb) {
      return fromDb;
    }
  }
  return listCharSlots(token);
}

function parseMessage(raw: string): ClientMessage | null {
  try {
    const data = JSON.parse(raw) as ClientMessage;
    if (data.type === "request_move" || data.type === "cast_skill") {
      return data;
    }
    if (data.type === "request_gacha" && (data.count === 1 || data.count === 10)) {
      return data;
    }
    if (data.type === "request_hello" && typeof data.guestToken === "string") {
      return data;
    }
    if (data.type === "request_char_create" && typeof data.name === "string" && typeof data.classId === "string") {
      return data;
    }
    if (data.type === "request_server_list" || data.type === "request_char_list" || data.type === "request_weapon_swap") {
      return data;
    }
    if (data.type === "request_char_select" && typeof data.slotIndex === "number") {
      return data;
    }
    if (data.type === "request_char_create_slot" && typeof data.slotIndex === "number" && typeof data.name === "string") {
      return data;
    }
    if (data.type === "request_char_delete" && typeof data.slotIndex === "number") {
      return data;
    }
    if (data.type === "request_use_class_card" && typeof data.slotIndex === "number") {
      return data;
    }
    if (data.type === "request_equip") {
      const hasWeapon = typeof data.weaponId === "string";
      const hasSpirit = data.spiritId === null || typeof data.spiritId === "string";
      if (hasWeapon || hasSpirit) {
        return data;
      }
    }
    if (data.type === "request_inspect" && typeof data.targetId === "string") {
      return data;
    }
    if (data.type === "request_chat" && typeof data.text === "string" && typeof data.channel === "string") {
      const ch = data.channel as ChatChannel;
      if (ch === "world" || ch === "server" || ch === "guild" || ch === "map" || ch === "whisper" || ch === "party") {
        return data;
      }
    }
    if (data.type === "request_party_invite" && typeof data.targetId === "string") {
      return data;
    }
    if (data.type === "request_party_respond" && typeof data.inviteId === "string" && typeof data.accept === "boolean") {
      return data;
    }
    if (data.type === "request_party_leave") {
      return data;
    }
    if (data.type === "request_portal" && typeof data.portalId === "string") {
      return data;
    }
    if (data.type === "request_interact" && typeof data.targetId === "string") {
      return data;
    }
    if (data.type === "request_shop_buy" && typeof data.shopId === "string" && typeof data.itemId === "string") {
      return data;
    }
    if (data.type === "request_shop_sell" && typeof data.shopId === "string" && typeof data.itemId === "string") {
      return data;
    }
    if (data.type === "request_use_item" && typeof data.slotIndex === "number") {
      return data;
    }
    if (data.type === "request_homestone" && (data.action === "set" || data.action === "teleport")) {
      return data;
    }
    if (data.type === "request_quest_accept" && typeof data.questId === "string") {
      return data;
    }
    if (data.type === "request_quest_turnin" && typeof data.questId === "string") {
      return data;
    }
    if (data.type === "request_register" && typeof data.username === "string" && typeof data.password === "string") {
      return data;
    }
    if (data.type === "request_login" && typeof data.username === "string" && typeof data.password === "string") {
      return data;
    }
    if (
      data.type === "request_equip_gear" &&
      (data.slot === "armor" ||
        data.slot === "helm" ||
        data.slot === "boots" ||
        data.slot === "gloves" ||
        data.slot === "accessory") &&
      (data.itemId === null || typeof data.itemId === "string")
    ) {
      return data;
    }
    if (data.type === "request_trade_invite" && typeof data.targetId === "string") {
      return data;
    }
    if (data.type === "request_trade_respond" && typeof data.inviteId === "string" && typeof data.accept === "boolean") {
      return data;
    }
    if (data.type === "request_trade_offer" && typeof data.gold === "number" && Array.isArray(data.offers)) {
      return data;
    }
    if (data.type === "request_trade_confirm" || data.type === "request_trade_cancel") {
      return data;
    }
    if (data.type === "request_friend_add" && typeof data.targetId === "string") {
      return data;
    }
    if (data.type === "request_friend_remove" && typeof data.guestToken === "string") {
      return data;
    }
    if (data.type === "request_guild_invite" && typeof data.targetId === "string") {
      return data;
    }
    if (data.type === "request_guild_respond" && typeof data.inviteId === "string" && typeof data.accept === "boolean") {
      return data;
    }
    if (data.type === "request_guild_leave") {
      return data;
    }
    if (data.type === "request_guild_create" && typeof data.name === "string") {
      return data;
    }
    if (data.type === "request_skill_unlock" && typeof data.skillId === "string") {
      return data;
    }
    if (data.type === "request_auction_list") {
      return data;
    }
    if (
      data.type === "request_auction_sell" &&
      typeof data.itemId === "string" &&
      typeof data.quantity === "number" &&
      typeof data.price === "number"
    ) {
      return data;
    }
    if (data.type === "request_auction_buy" && typeof data.listingId === "string") {
      return data;
    }
    if (data.type === "request_auction_cancel" && typeof data.listingId === "string") {
      return data;
    }
    return null;
  } catch {
    return null;
  }
}

function sendState(ws: WebSocket, session: PlayerSession): void {
  ensureGuild(session);
  const { players: playerList, monsters, npcs, portals, map } = snapshot(session.entity.mapId);
  const now = Date.now();
  const cls = classById(session.classId) ?? defaultClass;
  send(ws, {
    type: "sync_state",
    you: session.entity,
    players: playerList.filter((p) => p.id !== session.entity.id),
    monsters,
    npcs,
    portals,
    guestToken: session.guestToken,
    pity: currentPityView(session),
    equippedWeaponId: session.equippedWeaponId,
    equippedWeapon2Id: session.equippedWeapon2Id,
    weaponIds: session.weaponIds,
    equippedSpiritId: session.equippedSpiritId,
    spiritIds: session.spiritIds,
    skillIds: session.unlockedSkillIds.length ? session.unlockedSkillIds : [...cls.skillIds],
    cooldowns: cooldownSnapshot(session),
    inventory: session.inventory,
    gold: session.gold,
    homeMapId: session.homeMapId,
    homeX: session.homeX,
    homeY: session.homeY,
    quests: session.quests,
    completedQuestIds: session.completedQuestIds,
    charNameSet: session.charNameSet,
    classId: session.classId,
    classCardId: session.classCardId,
    towerClearedFloor: session.towerClearedFloor,
    switchFlags: session.switchFlags,
    slotIndex: session.slotIndex,
    inWorld: session.inWorld,
    level: session.level,
    xp: session.xp,
    xpToLevel: xpToNextLevel(session.level),
    equippedArmorId: session.equippedArmorId,
    equippedHelmId: session.equippedHelmId,
    equippedBootsId: session.equippedBootsId,
    equippedGlovesId: session.equippedGlovesId,
    equippedAccessoryId: session.equippedAccessoryId,
    serverTime: now,
    map,
  });
  send(ws, guildSnapshot(session));
  send(ws, friendsSnapshot(session));
  send(ws, skillTreeSnapshot(session));
  const instMsg = instanceSyncMsg(session.entity.mapId);
  if (instMsg) {
    send(ws, instMsg);
  }
  const partyMsg = partySnapshot(session.partyId);
  if (partyMsg) {
    send(ws, partyMsg);
  }
  broadcast({ type: "sync_status", entityId: session.entity.id, statuses: session.statuses, serverTime: now });
  for (const [mid, statuses] of monsterStatuses) {
    const m = liveMonsters.get(mid);
    if (m && m.mapId === session.entity.mapId) {
      send(ws, { type: "sync_status", entityId: mid, statuses, serverTime: now });
    }
  }
}

export function startServer(port = 7777): WebSocketServer {
  const wss = new WebSocketServer({ port, host: "127.0.0.1" });
  wss.on("error", (err: NodeJS.ErrnoException) => {
    if (err.code === "EADDRINUSE") {
      console.error(
        `Port ${port} already in use. Kill the old gAAAcha server (node/tsx on 7777), then retry:\n` +
          `  Get-NetTCPConnection -LocalPort ${port} | Select OwningProcess\n` +
          `  Stop-Process -Id <pid> -Force`,
      );
      process.exit(1);
    }
    throw err;
  });

  setInterval(() => {
    broadcastAll(tickWorld(Date.now()));
  }, 400);

  setInterval(() => {
    broadcastAll(tickProjectiles(Date.now(), 0.1));
  }, 100);

  wss.on("connection", (ws) => {
    let session = spawnPlayer();
    sockets.set(session.entity.id, ws);
    sendState(ws, session);
    console.log(`player joined ${session.entity.id} guest=${session.guestToken}`);

    ws.on("message", (buf) => {
      const msg = parseMessage(buf.toString());
      if (!msg) {
        send(ws, { type: "error", code: "bad_packet", message: "Unrecognized message" });
        return;
      }

      if (msg.type === "request_hello") {
        onPlayerDisconnect(session.entity.id);
        sockets.delete(session.entity.id);
        players.delete(session.entity.id);
        session = spawnPlayer(msg.guestToken);
        ensureGuild(session);
        sockets.set(session.entity.id, ws);
        sendState(ws, session);
        console.log(`player hello ${session.entity.id} guest=${session.guestToken}`);
        return;
      }

      if (msg.type === "request_register") {
        void (async () => {
          const result = await registerAccount(msg.username, msg.password, session.guestToken);
          if ("error" in result) {
            send(ws, { type: "error", code: result.error, message: result.error });
            return;
          }
          session.guestToken = result.guestToken;
          persist(session);
          send(ws, { type: "sync_auth", guestToken: result.guestToken, username: msg.username.trim().toLowerCase() });
          send(ws, { type: "sync_server_list", servers: SERVER_LIST });
        })();
        return;
      }

      if (msg.type === "request_login") {
        void (async () => {
          const result = await loginAccount(msg.username, msg.password);
          if ("error" in result) {
            send(ws, { type: "error", code: result.error, message: result.error });
            return;
          }
          onPlayerDisconnect(session.entity.id);
          sockets.delete(session.entity.id);
          players.delete(session.entity.id);
          session = spawnPlayer(result.guestToken);
          ensureGuild(session);
          sockets.set(session.entity.id, ws);
          send(ws, { type: "sync_auth", guestToken: result.guestToken, username: msg.username.trim().toLowerCase() });
          sendState(ws, session);
        })();
        return;
      }

      if (msg.type === "request_login") {
        void (async () => {
          const result = await loginAccount(msg.username, msg.password);
          if ("error" in result) {
            send(ws, { type: "error", code: result.error, message: result.error });
            return;
          }
          onPlayerDisconnect(session.entity.id);
          sockets.delete(session.entity.id);
          players.delete(session.entity.id);
          session = spawnPlayer(result.guestToken, { enterWorld: false });
          ensureGuild(session);
          sockets.set(session.entity.id, ws);
          send(ws, { type: "sync_auth", guestToken: result.guestToken, username: msg.username.trim().toLowerCase() });
          send(ws, { type: "sync_server_list", servers: SERVER_LIST });
        })();
        return;
      }

      if (msg.type === "request_server_list") {
        send(ws, { type: "sync_server_list", servers: SERVER_LIST });
        return;
      }

      if (msg.type === "request_char_list") {
        void (async () => {
          send(ws, { type: "sync_char_list", slots: await resolveCharList(session.guestToken) });
        })();
        return;
      }

      if (msg.type === "request_char_select") {
        void (async () => {
          const slot = Math.floor(msg.slotIndex);
          if (slot < 0 || slot > 7) {
            send(ws, { type: "error", code: "bad_slot", message: "Invalid slot" });
            return;
          }
          let save = loadCharSlot(session.guestToken, slot);
          if (isDbReady()) {
            const fromDb = await loadGuestSlotFromDb(session.guestToken, slot);
            if (fromDb) {
              save = fromDb;
            }
          }
          if (!save || !save.charNameSet) {
            send(ws, { type: "error", code: "empty_slot", message: "Empty character slot" });
            return;
          }
          onPlayerDisconnect(session.entity.id);
          sockets.delete(session.entity.id);
          players.delete(session.entity.id);
          session = spawnPlayer(session.guestToken, { slotIndex: slot, enterWorld: true, save });
          session.inWorld = true;
          ensureGuild(session);
          sockets.set(session.entity.id, ws);
          sendState(ws, session);
        })();
        return;
      }

      if (msg.type === "request_char_create_slot") {
        void (async () => {
          const slot = Math.floor(msg.slotIndex);
          if (slot < 0 || slot > 7) {
            send(ws, { type: "error", code: "bad_slot", message: "Invalid slot" });
            return;
          }
          const existing = loadCharSlot(session.guestToken, slot);
          if (existing?.charNameSet) {
            send(ws, { type: "error", code: "slot_taken", message: "Slot already used" });
            return;
          }
          if (isDbReady()) {
            const fromDb = await loadGuestSlotFromDb(session.guestToken, slot);
            if (fromDb?.charNameSet) {
              send(ws, { type: "error", code: "slot_taken", message: "Slot already used" });
              return;
            }
          }
          onPlayerDisconnect(session.entity.id);
          sockets.delete(session.entity.id);
          players.delete(session.entity.id);
          session = spawnPlayer(session.guestToken, { slotIndex: slot, enterWorld: true, save: null });
          const created = createCharacter(session, msg.name, "adventurer");
          if (created.error) {
            send(ws, created.error);
            return;
          }
          session.slotIndex = slot;
          session.inWorld = true;
          ensureGuild(session);
          sockets.set(session.entity.id, ws);
          persist(session);
          sendState(ws, session);
          send(ws, { type: "sync_char_list", slots: await resolveCharList(session.guestToken) });
        })();
        return;
      }

      if (msg.type === "request_char_delete") {
        void (async () => {
          const slot = Math.floor(msg.slotIndex);
          if (slot < 0 || slot > 7) {
            send(ws, { type: "error", code: "bad_slot", message: "Invalid slot" });
            return;
          }
          deleteCharSlot(session.guestToken, slot);
          if (isDbReady()) {
            await deleteCharacterDb(session.guestToken, slot);
          }
          send(ws, { type: "sync_char_list", slots: await resolveCharList(session.guestToken) });
        })();
        return;
      }

      if (msg.type === "request_weapon_swap") {
        const result = swapWeapons(session);
        if (result.error) {
          send(ws, result.error);
          return;
        }
        persist(session);
        send(ws, {
          type: "sync_equip",
          weaponId: session.equippedWeaponId,
          spiritId: session.equippedSpiritId,
          you: session.entity,
        });
        sendState(ws, session);
        return;
      }

      if (msg.type === "request_use_class_card") {
        const result = useInventoryItem(session, msg.slotIndex, Date.now(), {
          teleportHome: (requireCooldown) => teleportHome(session, Date.now(), requireCooldown),
          changeClass: (classId, cardId) => changeClass(session, classId, cardId),
        });
        if (result.error) {
          send(ws, result.error);
          return;
        }
        persist(session);
        for (const m of result.messages) {
          send(ws, m);
        }
        sendState(ws, session);
        return;
      }

      if (msg.type === "request_equip_gear") {
        const result = equipGear(session, msg.slot, msg.itemId);
        if (!("ok" in result)) {
          send(ws, result);
          return;
        }
        persist(session);
        send(ws, {
          type: "sync_equip",
          weaponId: session.equippedWeaponId,
          spiritId: session.equippedSpiritId,
          you: session.entity,
        });
        sendState(ws, session);
        return;
      }

      const now = Date.now();
      if (msg.type === "request_equip") {
        if (typeof msg.weaponId === "string") {
          const result = equipWeapon(session, msg.weaponId);
          if (!("ok" in result)) {
            send(ws, result);
            return;
          }
        }
        if (msg.spiritId !== undefined) {
          const result = equipSpirit(session, msg.spiritId);
          if (!("ok" in result)) {
            send(ws, result);
            return;
          }
        }
        persist(session);
        send(ws, {
          type: "sync_equip",
          weaponId: session.equippedWeaponId,
          spiritId: session.equippedSpiritId,
          you: session.entity,
        });
        return;
      }

      if (msg.type === "request_move") {
        if (session.entity.hp <= 0) {
          send(ws, { type: "error", code: "you_are_dead", message: "You are dead — reconnect to respawn" });
          return;
        }
        const result = validateMove(session, msg.x, msg.y, now);
        if ("ok" in result) {
          const dx = result.x - session.entity.x;
          const dy = result.y - session.entity.y;
          const len = Math.hypot(dx, dy);
          if (len > 0.001) {
            session.facingX = dx / len;
            session.facingY = dy / len;
          }
          session.entity.x = result.x;
          session.entity.y = result.y;
          session.lastMoveAt = now;
          broadcast({ type: "sync_move", entityId: session.entity.id, x: result.x, y: result.y });
          return;
        }
        send(ws, result);
        return;
      }

      if (msg.type === "request_gacha") {
        const gacha = pullGacha(session, msg.bannerId, msg.count, Math.random);
        if ("ok" in gacha) {
          persist(session);
          send(ws, { type: "sync_gacha", results: gacha.results, pity: gacha.pity, inventory: gacha.inventory });
          return;
        }
        send(ws, gacha);
        return;
      }

      if (msg.type === "request_inspect") {
        send(ws, buildInspect(msg.targetId, now));
        return;
      }

      if (msg.type === "request_chat") {
        const result = handleChat(session, msg.channel, msg.text, msg.targetName, now);
        if (result.error) {
          send(ws, result.error);
          return;
        }
        for (const { wsId, msg: chatMsg } of result.messages) {
          sendTo(wsId, chatMsg);
        }
        return;
      }

      if (msg.type === "request_party_invite") {
        const result = inviteToParty(session, msg.targetId, now);
        if (result.error) {
          send(ws, result.error);
          return;
        }
        if (result.toMsg) {
          sendTo(msg.targetId, result.toMsg);
        }
        send(ws, {
          type: "sync_chat",
          channel: "server",
          fromId: "system",
          fromName: "System",
          text: "Party invite sent.",
          serverTime: now,
        });
        return;
      }

      if (msg.type === "request_party_respond") {
        const result = respondPartyInvite(session, msg.inviteId, msg.accept, now);
        if (result.error) {
          send(ws, result.error);
          return;
        }
        for (const { playerId, msg: m } of result.syncs) {
          sendTo(playerId, m);
        }
        return;
      }

      if (msg.type === "request_party_leave") {
        const result = leaveParty(session);
        for (const { playerId, msg: m } of result.syncs) {
          sendTo(playerId, m);
        }
        return;
      }

      if (msg.type === "request_char_create") {
        const result = createCharacter(session, msg.name, msg.classId);
        if (result.error) {
          send(ws, result.error);
          return;
        }
        persist(session);
        sendState(ws, session);
        return;
      }

      if (msg.type === "request_portal") {
        const result = usePortal(session, msg.portalId, now);
        if (result.error) {
          send(ws, result.error);
          return;
        }
        persist(session);
        sendState(ws, session);
        return;
      }

      if (msg.type === "request_interact") {
        const npc = liveNpcs.get(msg.targetId);
        if (!npc || npc.mapId !== session.entity.mapId) {
          send(ws, { type: "error", code: "invalid_target", message: "NPC not found" });
          return;
        }
        if (Math.hypot(session.entity.x - npc.x, session.entity.y - npc.y) > 2.2) {
          send(ws, { type: "error", code: "too_far", message: "Move closer" });
          return;
        }
        const interact = npcInteract.get(msg.targetId) ?? "flavor";
        const talkMsg = noteTalk(session, msg.targetId);
        if (talkMsg) {
          send(ws, talkMsg);
        }
        send(ws, {
          type: "sync_interact",
          targetId: msg.targetId,
          interact,
          line: npcLines.get(msg.targetId) ?? "",
          shop: interact.startsWith("shop_") ? shopByNpcId(msg.targetId) : undefined,
          quests:
            interact === "quest" || interact === "trainer" || interact === "shop_cook"
              ? questsForNpc(session, msg.targetId)
              : undefined,
          home:
            interact === "homestone"
              ? { mapId: session.homeMapId, x: session.homeX, y: session.homeY }
              : undefined,
        });
        if (interact === "auction") {
          send(ws, auctionSnapshot());
        }
        if (interact === "trainer") {
          send(ws, skillTreeSnapshot(session));
        }
        if (interact === "switch") {
          const switchId = npcSwitchIds.get(msg.targetId);
          if (switchId) {
            session.switchFlags[switchId] = true;
            if (switchId === "sw_tower_f1") {
              bumpTowerFloor(session, 1);
            } else if (switchId === "sw_tower_f4") {
              bumpTowerFloor(session, 4);
            }
            persist(session);
            send(ws, {
              type: "sync_chat",
              channel: "server",
              fromId: "system",
              fromName: "System",
              text: `Switch ${switchId} activated.`,
              serverTime: now,
            });
            sendState(ws, session);
          }
        }
        return;
      }

      if (msg.type === "request_shop_buy") {
        const result = buyFromShop(session, msg.shopId, msg.itemId, msg.quantity ?? 1);
        if (result.error) {
          send(ws, result.error);
          return;
        }
        persist(session);
        send(ws, { type: "sync_inventory", inventory: session.inventory, gold: session.gold });
        send(ws, { type: "sync_gold", gold: session.gold });
        return;
      }

      if (msg.type === "request_shop_sell") {
        const result = sellToShop(session, msg.shopId, msg.itemId, msg.quantity ?? 1);
        if (result.error) {
          send(ws, result.error);
          return;
        }
        persist(session);
        send(ws, { type: "sync_inventory", inventory: session.inventory, gold: session.gold });
        send(ws, { type: "sync_gold", gold: session.gold });
        return;
      }

      if (msg.type === "request_use_item") {
        const result = useInventoryItem(session, msg.slotIndex, now, {
          teleportHome: (requireCooldown) => teleportHome(session, now, requireCooldown),
          changeClass: (classId, cardId) => changeClass(session, classId, cardId),
        });
        if (result.error) {
          send(ws, result.error);
          return;
        }
        persist(session);
        applyGearStats(session);
        for (const m of result.messages) {
          send(ws, m);
        }
        sendState(ws, session);
        return;
      }

      if (msg.type === "request_homestone") {
        if (msg.action === "set") {
          setHomestone(session);
          persist(session);
          send(ws, {
            type: "sync_chat",
            channel: "server",
            fromId: "system",
            fromName: "System",
            text: "Homestone bound here.",
            serverTime: now,
          });
          sendState(ws, session);
          return;
        }
        const result = teleportHome(session, now, false);
        if (result.error) {
          send(ws, result.error);
          return;
        }
        persist(session);
        sendState(ws, session);
        return;
      }

      if (msg.type === "request_quest_accept") {
        const result = acceptQuest(session, msg.questId);
        if (result.error) {
          send(ws, result.error);
          return;
        }
        persist(session);
        send(ws, { type: "sync_quest", quests: session.quests, completedQuestIds: session.completedQuestIds });
        return;
      }

      if (msg.type === "request_quest_turnin") {
        const result = turnInQuest(session, msg.questId);
        if (result.error) {
          send(ws, result.error);
          return;
        }
        persist(session);
        for (const m of result.messages) {
          send(ws, m);
        }
        return;
      }

      if (msg.type === "request_trade_invite") {
        const result = inviteTrade(session, msg.targetId, now);
        if (result.error) {
          send(ws, result.error);
          return;
        }
        if (result.toMsg) {
          sendTo(msg.targetId, result.toMsg);
        }
        return;
      }

      if (msg.type === "request_trade_respond") {
        const result = respondTradeInvite(session, msg.inviteId, msg.accept, now);
        if (result.error) {
          send(ws, result.error);
          return;
        }
        for (const { playerId, msg: m } of result.syncs) {
          sendTo(playerId, m);
        }
        return;
      }

      if (msg.type === "request_trade_offer") {
        const result = updateTradeOffer(session, msg.gold, msg.offers);
        if (result.error) {
          send(ws, result.error);
          return;
        }
        for (const { playerId, msg: m } of result.syncs) {
          sendTo(playerId, m);
        }
        return;
      }

      if (msg.type === "request_trade_confirm") {
        const result = confirmTrade(session);
        if (result.error) {
          send(ws, result.error);
        }
        for (const { playerId, msg: m } of result.syncs) {
          sendTo(playerId, m);
        }
        if (result.done) {
          persist(session);
        }
        return;
      }

      if (msg.type === "request_trade_cancel") {
        const result = cancelTrade(session);
        for (const { playerId, msg: m } of result.syncs) {
          sendTo(playerId, m);
        }
        return;
      }

      if (msg.type === "request_friend_add") {
        const result = addFriend(session, msg.targetId);
        if (result.error) {
          send(ws, result.error);
          return;
        }
        persist(session);
        send(ws, friendsSnapshot(session));
        return;
      }

      if (msg.type === "request_friend_remove") {
        const result = removeFriend(session, msg.guestToken);
        if (result.error) {
          send(ws, result.error);
          return;
        }
        persist(session);
        send(ws, friendsSnapshot(session));
        return;
      }

      if (msg.type === "request_guild_create") {
        const result = createGuild(session, msg.name);
        if (result.error) {
          send(ws, result.error);
          return;
        }
        if (result.msg) {
          send(ws, result.msg);
        }
        return;
      }

      if (msg.type === "request_guild_invite") {
        const result = inviteToGuild(session, msg.targetId, now);
        if (result.error) {
          send(ws, result.error);
          return;
        }
        if (result.toMsg) {
          sendTo(msg.targetId, result.toMsg);
        }
        return;
      }

      if (msg.type === "request_guild_respond") {
        const result = respondGuildInvite(session, msg.inviteId, msg.accept, now);
        if (result.error) {
          send(ws, result.error);
          return;
        }
        for (const { playerId, msg: m } of result.syncs) {
          sendTo(playerId, m);
        }
        return;
      }

      if (msg.type === "request_guild_leave") {
        send(ws, leaveGuild(session).msg);
        return;
      }

      if (msg.type === "request_skill_unlock") {
        const result = unlockSkill(session, msg.skillId);
        if (result.error) {
          send(ws, result.error);
          return;
        }
        persist(session);
        send(ws, skillTreeSnapshot(session));
        sendState(ws, session);
        return;
      }

      if (msg.type === "request_auction_list") {
        send(ws, auctionSnapshot());
        return;
      }

      if (msg.type === "request_auction_sell") {
        const result = listAuctionItem(session, msg.itemId, msg.quantity, msg.price);
        if (result.error) {
          send(ws, result.error);
          return;
        }
        persist(session);
        if (result.msg) {
          send(ws, result.msg);
        }
        send(ws, { type: "sync_inventory", inventory: session.inventory, gold: session.gold });
        return;
      }

      if (msg.type === "request_auction_buy") {
        const result = buyAuction(session, msg.listingId);
        if (result.error) {
          send(ws, result.error);
          return;
        }
        persist(session);
        for (const m of result.buyerMsgs) {
          send(ws, m);
        }
        if (result.sellerId != null && result.sellerGold != null) {
          sendTo(result.sellerId, { type: "sync_gold", gold: result.sellerGold });
        }
        return;
      }

      if (msg.type === "request_auction_cancel") {
        const result = cancelAuctionListing(session, msg.listingId);
        if (result.error) {
          send(ws, result.error);
          return;
        }
        persist(session);
        for (const m of result.msgs ?? []) {
          send(ws, m);
        }
        return;
      }

      if (msg.type !== "cast_skill") {
        send(ws, { type: "error", code: "bad_packet", message: "Unrecognized message" });
        return;
      }

      if (session.entity.hp <= 0) {
        send(ws, { type: "error", code: "you_are_dead", message: "You are dead — reconnect to respawn" });
        return;
      }

      const result = validateCast(session, msg.skillId, msg.targetId, now, {
        aimDx: msg.aimDx,
        aimDy: msg.aimDy,
        aimX: msg.aimX,
        aimY: msg.aimY,
      });
      if (!("ok" in result)) {
        send(ws, result);
        return;
      }

      for (const moved of result.movedEntities) {
        broadcast({ type: "sync_move", entityId: moved.id, x: moved.x, y: moved.y });
      }

      if (result.projectile) {
        const spawned = spawnProjectileFromCast(session.entity, result.projectile, result.mpAfter);
        broadcast(spawned.message);
      } else if (result.aoe) {
        broadcast({
          type: "sync_aoe",
          casterId: session.entity.id,
          skillId: msg.skillId,
          centerId: result.primaryTargetId,
          aoeRadius: result.aoeRadius ?? skillById(msg.skillId)?.aoeRadius ?? Math.max(0.5, (skillById(msg.skillId)?.range ?? 2) * 0.5),
          aimX: result.aimX,
          aimY: result.aimY,
          hits: result.hits,
          mpAfter: result.mpAfter,
        });
      } else {
        const hit = result.hits[0];
        broadcast({
          type: "sync_skill",
          casterId: session.entity.id,
          targetId: hit?.targetId ?? result.primaryTargetId,
          skillId: msg.skillId,
          damage: hit?.damage ?? 0,
          hpAfter: hit?.hpAfter ?? 0,
          mpAfter: result.mpAfter,
          crit: hit?.crit,
        });
      }

      let grantedLoot = false;
      const statusIds = new Set<string>([session.entity.id, result.primaryTargetId]);
      for (const hit of result.hits) {
        statusIds.add(hit.targetId);
        const threat = notePlayerDamageThreat(session.entity.id, hit.targetId, hit.damage, now);
        if (threat) {
          broadcast(threat);
        }
        for (const m of checkBossPhase(hit.targetId)) {
          if (m.type === "sync_chat") {
            broadcast(m);
          } else {
            send(ws, m);
          }
        }
        const targetVitals = vitalsOf(hit.targetId);
        if (targetVitals) {
          broadcast(targetVitals);
        }
        if (hit.hpAfter <= 0 && liveMonsters.has(hit.targetId) && !isImmortalMonster(hit.targetId)) {
          const mid = hit.targetId;
          broadcastAll(killMonster(mid, now));
          if (!grantedLoot) {
            for (const m of onMonsterKilledBy(session, mid)) {
              send(ws, m);
            }
            grantedLoot = true;
            persist(session);
          }
        }
      }

      const selfVitals = vitalsOf(session.entity.id);
      if (selfVitals) {
        broadcast(selfVitals);
      }
      send(ws, {
        type: "sync_cooldowns",
        cooldowns: cooldownSnapshot(session),
        serverTime: now,
      });
      for (const id of statusIds) {
        broadcastStatus(id, now);
      }
      send(ws, { type: "sync_inventory", inventory: session.inventory });
    });

    ws.on("close", () => {
      persist(session);
      const tradeLeft = onTradeDisconnect(session.entity.id);
      for (const { playerId, msg: m } of tradeLeft.syncs) {
        if (playerId !== session.entity.id) {
          sendTo(playerId, m);
        }
      }
      const left = onPlayerDisconnect(session.entity.id);
      for (const { playerId, msg: m } of left.syncs) {
        if (playerId !== session.entity.id) {
          sendTo(playerId, m);
        }
      }
      sockets.delete(session.entity.id);
      players.delete(session.entity.id);
      console.log(`player left ${session.entity.id}`);
    });
  });

  console.log(`gAAAcha server ws://127.0.0.1:${port}`);
  return wss;
}
