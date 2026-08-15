export type Element = "wind" | "fire" | "water" | "earth" | "holy" | "dark";
export type DamageType = "direct" | "aoe" | "dot" | "maxHpPercent";
export type Rarity = "r" | "sr" | "ssr";
export type AttrName = "atk" | "magicAtk" | "def" | "magicResist" | "critChance";
export type WeaponCategory = "sword" | "dagger" | "staff" | "bow" | "gun";
export type Scaling = "atk" | "magic";

export type MapDef = {
  id: string;
  name: string;
  width: number;
  height: number;
  spawn: { x: number; y: number };
  blocked: { x: number; y: number }[];
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
  | "elem_dmg_up";

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
};

export type TargetingType =
  | "NO_TARGET"
  | "UNIT_TARGET"
  | "SKILLSHOT_LINEAR"
  | "SKILLSHOT_CONE"
  | "GROUND_CIRCLE";

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
  element: Element;
  damageType: DamageType;
  scaling: Scaling;
  status: StatusDef | null;
  movement: MovementDef | null;
  /** World units per second; 0/omit = instant */
  projectileSpeed?: number;
  /** Hostile AoE blast radius (ground circle or around primary) */
  aoeRadius?: number;
  /** Linear skillshot corridor width */
  width?: number;
  /** Cone half-angle in degrees (full cone = 2 * this from center line… use full aperture) */
  coneAngleDeg?: number;
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
};

export type SpiritDef = {
  id: string;
  name: string;
  element: Element;
  elemDmgBonus: number;
  resistBonus?: Partial<ResistMap>;
};

export type MonsterDef = {
  id: string;
  name: string;
  hp: number;
  atk: number;
  def: number;
  magicResist: number;
  element: Element;
  hitRadius: number;
  aggroRange: number;
  leashRange: number;
  attackMs: number;
  prefer: "melee" | "ranged";
  x: number;
  y: number;
  respawnId: string;
};

export type ItemDef = {
  id: string;
  name: string;
  rarity: Rarity;
  kind: "character" | "material" | "weapon" | "spirit";
};

export type BannerDef = {
  id: string;
  name: string;
  hardPity: number;
  softPityStart: number;
  baseSsrRate: number;
  softStep: number;
  baseSrRate: number;
  pool: Record<Rarity, string[]>;
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
  count: number;
  hardPity: number;
  softPityStart: number;
  nextSsrChance: number;
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
};

export type Entity = {
  id: string;
  kind: "player" | "monster";
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
};

export type PlayerSession = {
  entity: Entity;
  classId: string;
  guestToken: string;
  lastActionAt: number;
  lastMoveAt: number;
  facingX: number;
  facingY: number;
  actionTimes: number[];
  moveTimes: number[];
  skillReadyAt: Record<string, number>;
  inventory: InventorySlot[];
  pity: Record<string, PityCounter>;
  statuses: StatusInstance[];
  weaponIds: string[];
  equippedWeaponId: string;
  spiritIds: string[];
  equippedSpiritId: string | null;
  moveLockUntil: number;
};

export type ChatChannel = "world" | "server" | "guild" | "map" | "whisper";

export type ClientMessage =
  | { type: "request_hello"; guestToken: string }
  | { type: "request_move"; x: number; y: number }
  | { type: "cast_skill"; skillId: string; targetId: string; aimDx?: number; aimDy?: number; aimX?: number; aimY?: number }
  | { type: "request_gacha"; bannerId: string; count: 1 | 10 }
  | { type: "request_equip"; weaponId?: string; spiritId?: string | null }
  | { type: "request_inspect"; targetId: string }
  | { type: "request_chat"; channel: ChatChannel; text: string; targetName?: string }
  | { type: "request_party_invite"; targetId: string };

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
};

export type ServerMessage =
  | {
      type: "sync_state";
      you: Entity;
      players: Entity[];
      monsters: Entity[];
      guestToken: string;
      pity: PityView | null;
      equippedWeaponId: string;
      weaponIds: string[];
      equippedSpiritId: string | null;
      spiritIds: string[];
      skillIds: string[];
      cooldowns: CooldownEntry[];
      inventory: InventorySlot[];
      serverTime: number;
      map: MapDef;
    }
  | { type: "sync_move"; entityId: string; x: number; y: number }
  | { type: "sync_skill"; casterId: string; targetId: string; skillId: string; damage: number; hpAfter: number; mpAfter: number; crit?: boolean }
  | { type: "sync_aoe"; casterId: string; skillId: string; centerId: string; aoeRadius: number; aimX?: number; aimY?: number; hits: { targetId: string; damage: number; hpAfter: number; crit: boolean }[]; mpAfter: number }
  | { type: "sync_vitals"; entityId: string; hp: number; maxHp: number; mp: number; maxMp: number }
  | { type: "sync_gacha"; results: GachaDrop[]; pity: PityView; inventory: InventorySlot[] }
  | { type: "sync_despawn"; entityId: string; reason: "death" }
  | { type: "sync_spawn"; entity: Entity }
  | { type: "sync_loot"; itemId: string; quantity: number; inventory: InventorySlot[] }
  | { type: "sync_equip"; weaponId: string; spiritId: string | null; you: Entity }
  | { type: "sync_status"; entityId: string; statuses: StatusInstance[]; serverTime: number }
  | { type: "sync_cooldowns"; cooldowns: CooldownEntry[]; serverTime: number }
  | { type: "sync_inventory"; inventory: InventorySlot[] }
  | { type: "sync_projectile_spawn"; projectile: { id: string; casterId: string; targetId: string; skillId: string; x: number; y: number; speed: number; vx?: number; vy?: number } }
  | { type: "sync_projectile_move"; id: string; x: number; y: number }
  | { type: "sync_projectile_despawn"; id: string }
  | {
      type: "sync_threat";
      monsterId: string;
      entries: { playerId: string; pct: number }[];
      topId: string | null;
    }
  | {
      type: "sync_inspect";
      targetId: string;
      kind: "player" | "monster";
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
    }
  | {
      type: "sync_chat";
      channel: ChatChannel;
      fromId: string;
      fromName: string;
      text: string;
      targetId?: string;
      serverTime: number;
    }
  | { type: "error"; code: string; message: string };
