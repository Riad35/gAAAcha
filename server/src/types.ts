export type Element = "wind" | "fire" | "water" | "earth" | "holy" | "dark";
export type DamageType = "direct" | "aoe" | "dot" | "maxHpPercent";
export type Rarity = "r" | "sr" | "ssr";
export type AttrName = "atk" | "magicAtk" | "def" | "magicResist" | "critChance";
export type WeaponCategory = "sword" | "dagger" | "staff" | "bow" | "gun" | "tome" | "charm" | "orb";
export type ArmorWeight = "light" | "medium" | "heavy" | "plate";
export type HandSlot = "mainhand" | "offhand" | "both";
export type WeaponGrip = "1h" | "2h";
export type Scaling = "atk" | "magic";

export type MapDef = {
  id: string;
  name: string;
  width: number;
  height: number;
  spawn: { x: number; y: number };
  blocked: { x: number; y: number }[];
  /** Relative path under server/data to a digit-grid .map.txt. Source of truth when set. */
  grid?: string;
  /** Inclusive rectangles expanded into blocked tiles at load (fallback if no grid). */
  wallRects?: { x: number; y: number; w: number; h: number }[];
  /** Decorative sprites; tiles are also blocked. */
  props?: { x: number; y: number; kind: string }[];
  /** Tiles that deal periodic damage while stood on. */
  hazards?: { x: number; y: number; damage?: number }[];
  /** Compiled tile ids (row = y, col = x). Runtime only; not authored in maps.json. */
  tiles?: number[][];
};

export type ResistMap = Record<Element, number>;

export type ClassDef = {
  id: string;
  name: string;
  hp: number;
  mp: number;
  atk: number;
  magicAtk: number;
  def: number;
  magicResist: number;
  attackSpeed: number;
  hpRegen: number;
  mpRegen: number;
  critChance: number;
  critDamage: number;
  moveSpeed: number;
  hitRadius: number;
  resist: ResistMap;
  skillIds: string[];
  startingWeaponId: string;
  startingWeaponIds: string[];
  startingOffhandId?: string | null;
  startingSpiritId: string | null;
  startingSpiritIds: string[];
};

export type MovementDef = {
  kind: "dash" | "shove" | "pull" | "teleport";
  tiles: number;
};

export type StatusKind =
  | "buff"
  | "debuff"
  | "stun"
  | "blind"
  | "dot"
  | "shove_resist"
  | "attr_up"
  | "speed_mult"
  | "shield_phys"
  | "shield_mag"
  | "elem_dmg_up"
  | "dmg_taken_mult";

export type StatusDef = {
  id: string;
  kind: StatusKind;
  durationMs: number;
  tickMs?: number;
  potency?: number;
  atkBonus?: number;
  attr?: AttrName;
  amount?: number;
  moveSpeedMult?: number;
  attackSpeedMult?: number;
  shieldHp?: number;
  element?: Element;
  elemDmgMult?: number;
  /** Incoming damage multiplier (e.g. decoy 0.2 = take 20%). */
  dmgTakenMult?: number;
};

export type TargetingType =
  | "NO_TARGET"
  | "UNIT_TARGET"
  | "ALLY_TARGET"
  | "SKILLSHOT_LINEAR"
  | "SKILLSHOT_CONE"
  | "GROUND_CIRCLE";

/** Who the skill may hit or buff. Omit → inferred from selfTarget / targetingType. */
export type SkillAffects = "hostile" | "friendly" | "self" | "all";

/** Where aoeRadius is centered. Omit → inferred from targetingType / damageType. */
export type AoeOrigin = "none" | "caster" | "target" | "ground";

export type SkillDef = {
  id: string;
  name: string;
  range: number;
  cooldownMs: number;
  manaCost: number;
  damage: number;
  heal: number;
  healMp?: number;
  selfTarget: boolean;
  targetingType: TargetingType;
  /** Who is a valid hit/buff. Friendly = any living player on the map (incl. self). */
  affects?: SkillAffects;
  /** Splash origin. none = single primary (or self). */
  aoeOrigin?: AoeOrigin;
  element: Element;
  damageType: DamageType;
  scaling: Scaling;
  status: StatusDef | null;
  movement: MovementDef | null;
  /** World units per second; 0/omit = instant */
  projectileSpeed?: number;
  /** Splash radius around aoeOrigin (edge-to-edge). */
  aoeRadius?: number;
  /** Linear skillshot corridor width */
  width?: number;
  /** Cone half-angle in degrees (full cone = 2 * this from center line… use full aperture) */
  coneAngleDeg?: number;
  /** 1 = mainhand, 2 = offhand (legacy shot used 2; bow shot is mainhand). */
  weaponSlot?: 1 | 2;
  /** Adventurer L1–20 gate. Missing = available once the class is unlocked. */
  unlockLevel?: number;
  /** Classes that list this skill. Catalog / docs; runtime tree is ClassDef.skillIds. */
  classIds?: string[];
};

export type WeaponDef = {
  id: string;
  name: string;
  slot: "weapon";
  category: WeaponCategory;
  style: "melee" | "ranged";
  range: number;
  element: Element;
  damageType: DamageType;
  scaling: Scaling;
  atkBonus: number;
  magicAtkBonus: number;
  attackSpeedBonus: number;
  resistBonus?: Partial<ResistMap>;
  hand?: HandSlot;
  grip?: WeaponGrip;
};

export type SpiritDef = {
  id: string;
  name: string;
  element: Element;
  elemDmgBonus: number;
  resistBonus?: Partial<ResistMap>;
};

/** Ordered skill ids from skills.json. Data-only until AI review. */
export type MonsterSkillPriorityList = string[];

/** Live checkBossPhase knobs, stored on the species. Data-only until AI review. */
export type MonsterPhaseStatModifier = {
  atkMul?: number;
  moveSpeedAdd?: number;
  attackSpeedAdd?: number;
};

export type MonsterPhaseDef = {
  hpThresholdPercent: number;
  skillPriorityOverride?: MonsterSkillPriorityList;
  statModifier?: MonsterPhaseStatModifier;
};

/** Combat template. Unique `id`; spawn-cap key is `catalogId ?? id`. */
export type MonsterSpeciesDef = {
  id: string;
  name: string;
  catalogId?: string;
  hp: number;
  atk: number;
  magicAtk?: number;
  def: number;
  magicResist: number;
  element: Element;
  hitRadius: number;
  aggroRange: number;
  leashRange: number;
  attackMs: number;
  prefer: "melee" | "ranged";
  aggroMode?: "hostile" | "neutral";
  monsterType?: string;
  hitRate?: number;
  critChance?: number;
  critDamage?: number;
  dodgeRate?: number;
  elementalResist?: Partial<ResistMap>;
  skillPriorityList?: MonsterSkillPriorityList;
  phases?: MonsterPhaseDef[];
};

/** Placement only. `speciesId` references MonsterSpeciesDef.id. */
export type MonsterSpawnDef = {
  speciesId: string;
  mapId: string;
  x: number;
  y: number;
  respawnId: string;
  name?: string;
};

/**
 * Resolved spawn + species for world.ts.
 * `id` is the catalog/cap key (`catalogId ?? species.id`), not the unique template id.
 */
export type MonsterDef = Omit<MonsterSpeciesDef, "id" | "catalogId"> & {
  id: string;
  speciesId: string;
  catalogId?: string;
  mapId: string;
  x: number;
  y: number;
  respawnId: string;
};

export type ItemDef = {
  id: string;
  name: string;
  rarity: Rarity;
  kind: "character" | "skin" | "portrait" | "material" | "weapon" | "spirit" | "consumable" | "armor" | "class_card" | "subclass";
  use?: "homestone" | "heal" | "buff_food" | "skill_unlock" | "class_card" | "skin" | "portrait";
  healHp?: number;
  healMp?: number;
  durationMs?: number;
  slot?: "armor" | "helm" | "boots" | "gloves" | "accessory" | "amulet" | "ring1" | "ring2" | "subclass";
  levelReq?: number;
  defBonus?: number;
  atkBonus?: number;
  magicAtkBonus?: number;
  moveSpeedBonus?: number;
  classId?: string;
  resistBonus?: Partial<ResistMap>;
  weaponCategory?: string;
  weaponKind?: WeaponCategory;
  hand?: HandSlot;
  grip?: WeaponGrip;
  armorWeight?: ArmorWeight;
  secondaryWeaponId?: string;
  description?: string;
};

export type PortalDef = {
  id: string;
  mapId: string;
  x: number;
  y: number;
  targetMapId: string;
  targetX: number;
  targetY: number;
  label: string;
  minTowerCleared?: number;
  requireSwitch?: string;
};

export type ShopEntry = {
  itemId: string;
  buyPrice: number;
  sellPrice: number;
};

export type ShopDef = {
  id: string;
  npcId: string;
  entries: ShopEntry[];
};

export type QuestStep =
  | { kind: "talk"; npcId: string; count: number }
  | { kind: "kill"; monsterType: string; count: number }
  | { kind: "deliver"; itemId: string; count: number };

export type QuestDef = {
  id: string;
  name: string;
  giverNpcId: string;
  turnInNpcId: string;
  requiresQuestId?: string;
  minLevel?: number;
  chain?: "main" | "side";
  steps: QuestStep[];
  rewards: { gold: number; xp?: number; items: { itemId: string; quantity: number }[] };
  dialogue: string;
};

export type QuestProgress = {
  questId: string;
  stepIndex: number;
  progress: number;
  completed: boolean;
  name?: string;
  hint?: string;
  stepNeed?: number;
};

export type BannerDef = {
  id: string;
  name: string;
  hardPity: number;
  softPityStart: number;
  baseSsrRate: number;
  softStep: number;
  baseSrRate: number;
  pullCostDust?: number;
  tenPullCostDust?: number;
  pool: Record<Rarity, string[]>;
};

export type LootDropDef = {
  itemId: string;
  chance: number;
  minQty?: number;
  maxQty?: number;
};

export type LootTable = {
  goldMin: number;
  goldMax: number;
  xp: number;
  drops: LootDropDef[];
};

export type InventorySlot = {
  slotIndex: number;
  itemId: string | null;
  quantity: number;
};

export type PityCounter = {
  bannerId: string;
  pity: number;
  totalPulls: number;
};

export type PityView = {
  bannerId: string;
  bannerName: string;
  count: number;
  hardPity: number;
  softPityStart: number;
  nextSsrChance: number;
  baseSsrRate: number;
  baseSrRate: number;
  pullCostDust: number;
  tenPullCostDust: number;
};

export type GachaDrop = {
  itemId: string;
  rarity: Rarity;
};

export type StatusInstance = {
  id: string;
  kind: StatusKind;
  until: number;
  nextTick?: number;
  potency?: number;
  atkBonus?: number;
  attr?: AttrName;
  amount?: number;
  moveSpeedMult?: number;
  attackSpeedMult?: number;
  shieldHp?: number;
  element?: Element;
  elemDmgMult?: number;
  dmgTakenMult?: number;
};

export type Entity = {
  id: string;
  kind: "player" | "monster" | "npc";
  name: string;
  x: number;
  y: number;
  hp: number;
  maxHp: number;
  mp: number;
  maxMp: number;
  atk: number;
  magicAtk: number;
  def: number;
  magicResist: number;
  attackSpeed: number;
  hpRegen: number;
  mpRegen: number;
  critChance: number;
  critDamage: number;
  moveSpeed: number;
  hitRadius: number;
  resist: ResistMap;
  element?: Element;
  weaponId?: string;
  spiritId?: string | null;
  mapId: string;
  sprite?: string;
};

export type FriendEntry = {
  guestToken: string;
  name: string;
};

export type PlayerSession = {
  entity: Entity;
  classId: string;
  guestToken: string;
  characterId?: string;
  slotIndex: number;
  lastActionAt: number;
  lastMoveAt: number;
  facingX: number;
  facingY: number;
  actionTimes: number[];
  moveTimes: number[];
  skillReadyAt: Record<string, number>;
  skillCdMs?: Record<string, number>;
  busyUntil?: number;
  inventory: InventorySlot[];
  pity: Record<string, PityCounter>;
  statuses: StatusInstance[];
  weaponIds: string[];
  equippedWeaponId: string;
  equippedWeapon2Id: string | null;
  spiritIds: string[];
  equippedSpiritId: string | null;
  moveLockUntil: number;
  partyId: string | null;
  guildId: string | null;
  gold: number;
  homeMapId: string;
  homeX: number;
  homeY: number;
  quests: QuestProgress[];
  completedQuestIds: string[];
  charNameSet: boolean;
  homestoneReadyAt: number;
  unlockedSkillIds: string[];
  skillPoints: number;
  level: number;
  xp: number;
  equippedArmorId: string | null;
  equippedHelmId: string | null;
  equippedBootsId: string | null;
  equippedGlovesId: string | null;
  equippedAccessoryId: string | null;
  equippedAmuletId?: string | null;
  equippedRing1Id?: string | null;
  equippedRing2Id?: string | null;
  enhanceLevels?: Partial<Record<string, number>>;
  talkingNpcId?: string | null;
  friends: FriendEntry[];
  classCardId: string | null;
  equippedSubclassId?: string | null;
  transformed?: boolean;
  equippedSkinId: string | null;
  towerClearedFloor: number;
  switchFlags: Record<string, boolean>;
  /** Lobby sessions have not entered world yet (login gate). */
  inWorld: boolean;
  /** Needs a persist flush (autosave / logout). */
  dirty: boolean;
  /** Sliding 1s window for non-move/cast RPCs. */
  rpcTimes: number[];
  /** Last websocket payload (any). Used to drop silent sockets. */
  lastHeardAt?: number;
  /** Last `sync_cond` snapshot, to emit only on change. */
  lastCond?: { canMove: boolean; canAct: boolean; resting: boolean };
};

export type ChatChannel = "world" | "server" | "guild" | "map" | "whisper" | "party";

export type ClientMessage =
  | { type: "request_hello"; guestToken: string }
  | { type: "request_char_create"; name: string; classId: string }
  | { type: "request_server_list" }
  | { type: "request_char_list" }
  | { type: "request_char_select"; slotIndex: number }
  | { type: "request_char_create_slot"; slotIndex: number; name: string }
  | { type: "request_char_delete"; slotIndex: number }
  | { type: "request_weapon_swap" }
  | { type: "request_swap_inventory"; fromIndex: number; toIndex: number }
  | { type: "request_debug_set_class"; classId: string }
  | { type: "request_debug_set_level"; level: number }
  | { type: "request_choose_class"; classId: string }
  | { type: "request_transform"; on: boolean }
  | { type: "request_use_class_card"; slotIndex: number }
  | { type: "request_respawn" }
  | { type: "request_move"; x: number; y: number; seq?: number }
  | { type: "request_ping"; clientTime?: number }
  | { type: "cast_skill"; skillId: string; targetId: string; aimDx?: number; aimDy?: number; aimX?: number; aimY?: number }
  | { type: "request_gacha"; bannerId: string; count: 1 | 10 }
  | { type: "request_equip"; weaponId?: string | null; offhandId?: string | null; spiritId?: string | null }
  | { type: "request_inspect"; targetId: string }
  | { type: "request_chat"; channel: ChatChannel; text: string; targetName?: string }
  | { type: "request_party_invite"; targetId: string }
  | { type: "request_party_respond"; inviteId: string; accept: boolean }
  | { type: "request_party_leave" }
  | { type: "request_portal"; portalId: string }
  | { type: "request_interact"; targetId: string }
  | { type: "request_dialog_close" }
  | { type: "request_enhance"; slot: string }
  | { type: "request_shop_buy"; shopId: string; itemId: string; quantity?: number }
  | { type: "request_shop_sell"; shopId: string; itemId: string; quantity?: number }
  | { type: "request_use_item"; slotIndex: number }
  | { type: "request_homestone"; action: "set" | "teleport" }
  | { type: "request_quest_accept"; questId: string }
  | { type: "request_quest_turnin"; questId: string }
  | { type: "request_register"; username: string; password: string }
  | { type: "request_login"; username: string; password: string }
  | {
      type: "request_equip_gear";
      slot: "armor" | "helm" | "boots" | "gloves" | "accessory" | "amulet" | "ring1" | "ring2" | "subclass";
      itemId: string | null;
    }
  | { type: "request_trade_invite"; targetId: string }
  | { type: "request_trade_respond"; inviteId: string; accept: boolean }
  | { type: "request_trade_offer"; gold: number; offers: { slotIndex: number; quantity: number }[] }
  | { type: "request_trade_confirm" }
  | { type: "request_trade_cancel" }
  | { type: "request_friend_add"; targetId: string }
  | { type: "request_friend_remove"; guestToken: string }
  | { type: "request_guild_invite"; targetId: string }
  | { type: "request_guild_respond"; inviteId: string; accept: boolean }
  | { type: "request_guild_leave" }
  | { type: "request_guild_create"; name: string }
  | { type: "request_skill_unlock"; skillId: string }
  | { type: "request_auction_list" }
  | { type: "request_auction_sell"; itemId: string; quantity: number; price: number }
  | { type: "request_auction_buy"; listingId: string }
  | { type: "request_auction_cancel"; listingId: string };

export type CooldownEntry = {
  id: string;
  readyAt: number;
  cooldownMs: number;
};

export type PendingProjectileHit = {
  targetId: string;
  damage: number;
  crit: boolean;
};

export type LiveProjectile = {
  id: string;
  casterId: string;
  /** Homing target, or "" for directional */
  targetId: string;
  skillId: string;
  x: number;
  y: number;
  speed: number;
  /** Directional flight */
  vx?: number;
  vy?: number;
  traveled?: number;
  maxRange?: number;
  width?: number;
  pendingHits: PendingProjectileHit[];
  pendingStatus: StatusInstance | null;
  statusDurationMs: number;
  mpAfter: number;
  mapId: string;
};

export type ServerMessage =
  | {
      type: "sync_state";
      you: Entity;
      players: Entity[];
      monsters: Entity[];
      npcs: Entity[];
      portals: PortalDef[];
      guestToken: string;
      pity: PityView | null;
      equippedWeaponId: string;
      equippedWeapon2Id: string | null;
      weaponIds: string[];
      equippedSpiritId: string | null;
      spiritIds: string[];
      skillIds: string[];
      classSkillIds?: string[];
      cooldowns: CooldownEntry[];
      inventory: InventorySlot[];
      gold: number;
      homeMapId: string;
      homeX: number;
      homeY: number;
      quests: QuestProgress[];
      completedQuestIds: string[];
      charNameSet: boolean;
      classId: string;
      classCardId: string | null;
      equippedSkinId: string | null;
      towerClearedFloor: number;
      switchFlags: Record<string, boolean>;
      slotIndex: number;
      inWorld: boolean;
      level: number;
      xp: number;
      xpToLevel: number;
      equippedArmorId: string | null;
      equippedHelmId: string | null;
      equippedBootsId: string | null;
      equippedGlovesId: string | null;
      equippedAccessoryId: string | null;
      equippedAmuletId?: string | null;
      equippedRing1Id?: string | null;
      equippedRing2Id?: string | null;
      enhanceLevels?: Partial<Record<string, number>>;
      equippedSubclassId?: string | null;
      transformed?: boolean;
      serverTime: number;
      map: MapDef;
    }
  | {
      type: "sync_server_list";
      servers: { id: string; name: string; host: string; port: number; status: string }[];
    }
  | {
      type: "sync_char_list";
      slots: {
        slotIndex: number;
        characterId: string | null;
        name: string | null;
        classId: string | null;
        level: number;
        mapId: string | null;
        empty: boolean;
      }[];
    }
  | { type: "sync_move"; entityId: string; x: number; y: number; speed?: number; seq?: number }
  | { type: "sync_cond"; entityId: string; canMove: boolean; canAct: boolean; resting: boolean; serverTime: number }
  | { type: "sync_pong"; serverTime: number; clientTime?: number }
  | { type: "sync_skill"; casterId: string; targetId: string; skillId: string; damage: number; hpAfter: number; mpAfter: number; crit?: boolean; element?: string; missed?: boolean; advantage?: string; resistHint?: number }
  | { type: "sync_aoe"; casterId: string; skillId: string; centerId: string; aoeRadius: number; aimX?: number; aimY?: number; hits: { targetId: string; damage: number; hpAfter: number; crit: boolean; element?: string; missed?: boolean; advantage?: string }[]; mpAfter: number }
  | { type: "sync_vitals"; entityId: string; hp: number; maxHp: number; mp: number; maxMp: number; gold?: number }
  | { type: "sync_gacha"; results: GachaDrop[]; pity: PityView; inventory: InventorySlot[] }
  | { type: "sync_despawn"; entityId: string; reason: "death" | "leave" }
  | { type: "sync_spawn"; entity: Entity }
  | { type: "sync_loot"; itemId: string; quantity: number; inventory: InventorySlot[]; gold?: number }
  | { type: "sync_equip"; weaponId: string; weapon2Id?: string | null; spiritId: string | null; you: Entity }
  | { type: "sync_status"; entityId: string; statuses: StatusInstance[]; serverTime: number }
  | { type: "sync_fx"; kind: "homestone" | "food" | "telegraph"; entityId?: string; x?: number; y?: number; radius?: number; durationMs?: number }
  | { type: "sync_cooldowns"; cooldowns: CooldownEntry[]; serverTime: number }
  | { type: "sync_inventory"; inventory: InventorySlot[]; gold?: number; equippedSkinId?: string | null }
  | { type: "sync_projectile_spawn"; projectile: { id: string; casterId: string; targetId: string; skillId: string; x: number; y: number; speed: number; vx?: number; vy?: number } }
  | { type: "sync_projectile_move"; id: string; x: number; y: number; mapId?: string }
  | { type: "sync_projectile_despawn"; id: string; mapId?: string }
  | { type: "sync_party_invite"; inviteId: string; fromId: string; fromName: string }
  | {
      type: "sync_party";
      partyId: string | null;
      members: {
        id: string;
        name: string;
        hp: number;
        maxHp: number;
        mp: number;
        maxMp: number;
        level: number;
        classId: string;
      }[];
    }
  | { type: "sync_guild"; guildId: string; guildName: string; members?: { id: string; name: string }[] }
  | { type: "sync_guild_invite"; inviteId: string; fromId: string; fromName: string; guildName: string }
  | { type: "sync_trade_invite"; inviteId: string; fromId: string; fromName: string }
  | {
      type: "sync_trade";
      tradeId: string | null;
      you: { gold: number; slots: { slotIndex: number; itemId: string; quantity: number }[]; confirmed: boolean };
      them: {
        gold: number;
        slots: { slotIndex: number; itemId: string; quantity: number }[];
        confirmed: boolean;
        name: string;
      };
    }
  | {
      type: "sync_friends";
      friends: { guestToken: string; name: string; online: boolean; playerId?: string }[];
    }
  | {
      type: "sync_threat";
      monsterId: string;
      entries: { playerId: string; pct: number }[];
      topId: string | null;
    }
  | {
      type: "sync_inspect";
      targetId: string;
      kind: "player" | "monster" | "npc";
      name: string;
      portraitKey: string;
      hp: number;
      maxHp: number;
      mp: number;
      maxMp: number;
      atk: number;
      magicAtk: number;
      def: number;
      magicResist: number;
      attackSpeed: number;
      moveSpeed: number;
      critChance: number;
      critDamage: number;
      hitRadius: number;
      resist: ResistMap;
      element?: Element;
      weaponId?: string;
      spiritId?: string | null;
      statuses: StatusInstance[];
      monsterType?: string;
      interact?: string;
    }
  | {
      type: "sync_chat";
      channel: ChatChannel;
      fromId: string;
      fromName: string;
      text: string;
      targetId?: string;
      serverTime: number;
      mapId?: string;
    }
  | {
      type: "sync_interact";
      targetId: string;
      interact: string;
      line: string;
      name?: string;
      shop?: ShopDef;
      quests?: { quest: QuestDef; state: "available" | "active" | "ready" | "done" }[];
      home?: { mapId: string; x: number; y: number };
    }
  | { type: "sync_quest"; quests: QuestProgress[]; completedQuestIds: string[] }
  | { type: "sync_gold"; gold: number }
  | { type: "sync_xp"; level: number; xp: number; xpToLevel: number; skillPoints?: number }
  | {
      type: "sync_skills";
      skillIds: string[];
      skillPoints: number;
      unlockable: string[];
      unlockLevels?: Record<string, number>;
      classSkillIds?: string[];
      catalog?: {
        id: string;
        name: string;
        manaCost: number;
        weaponSlot: number;
        cooldownMs: number;
        targetingType?: TargetingType;
        affects?: SkillAffects;
        aoeOrigin?: AoeOrigin;
        range?: number;
        aoeRadius?: number;
      }[];
    }
  | { type: "sync_death"; entityId: string; homeMapId: string; homeX: number; homeY: number }
  | {
      type: "sync_class_change";
      classId: string;
      className: string;
      skillIds: string[];
      resistBonus: Record<string, number>;
    }
  | {
      type: "sync_auction";
      listings: { id: string; sellerName: string; itemId: string; quantity: number; price: number }[];
    }
  | { type: "sync_instance"; instanceId: string | null; mapId: string; expiresAt: number; phase?: number }
  | { type: "sync_auth"; guestToken: string; username: string }
  | { type: "error"; code: string; message: string; seq?: number };
