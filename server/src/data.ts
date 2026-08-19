import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import type {
  BannerDef,
  ClassDef,
  ItemDef,
  MapDef,
  MonsterDef,
  PortalDef,
  QuestDef,
  ShopDef,
  SkillDef,
  SpiritDef,
  WeaponDef,
} from "./types.js";

const dataDir = join(dirname(fileURLToPath(import.meta.url)), "..", "data");

export type NpcDef = {
  id: string;
  name: string;
  mapId: string;
  x: number;
  y: number;
  hitRadius: number;
  line: string;
  interact:
    | "shop_weapon"
    | "shop_item"
    | "shop_cook"
    | "shop_skill"
    | "trainer"
    | "quest"
    | "homestone"
    | "flavor"
    | "auction"
    | "switch";
  switchId?: string;
};

const CLASS_ALIASES: Record<string, string> = {
  wanderer: "adventurer",
  warrior: "fighter",
  archer: "marksman",
};

export function migrateClassId(id: string): string {
  return CLASS_ALIASES[id] ?? id;
}

function loadJson<T>(name: string): T {
  return JSON.parse(readFileSync(join(dataDir, name), "utf8")) as T;
}

export const maps = loadJson<MapDef[]>("maps.json");
export const classes = loadJson<ClassDef[]>("classes.json");
export const skills = loadJson<SkillDef[]>("skills.json");
export const monsters = loadJson<MonsterDef[]>("monsters.json");
export const items = loadJson<ItemDef[]>("items.json");
export const banners = loadJson<BannerDef[]>("banners.json");
export const weapons = loadJson<WeaponDef[]>("weapons.json");
export const spirits = loadJson<SpiritDef[]>("spirits.json");
export const npcs = loadJson<NpcDef[]>("npcs.json");
export const portals = loadJson<PortalDef[]>("portals.json");
export const shops = loadJson<ShopDef[]>("shops.json");
export const quests = loadJson<QuestDef[]>("quests.json");

export const defaultMap = maps.find((m) => m.id === "test_arena") ?? maps.find((m) => m.id === "town_ashen") ?? maps[0];
export const defaultClass = classes[0];
export const defaultBanner = banners[0];

export function mapById(id: string): MapDef | undefined {
  return maps.find((m) => m.id === id);
}

export function skillById(id: string): SkillDef | undefined {
  return skills.find((skill) => skill.id === id);
}

export function bannerById(id: string): BannerDef | undefined {
  return banners.find((banner) => banner.id === id);
}

export function weaponById(id: string): WeaponDef | undefined {
  return weapons.find((weapon) => weapon.id === id);
}

export function spiritById(id: string): SpiritDef | undefined {
  return spirits.find((spirit) => spirit.id === id);
}

export function monsterById(id: string): MonsterDef | undefined {
  return monsters.find((monster) => monster.id === id);
}

export function itemById(id: string): ItemDef | undefined {
  return items.find((item) => item.id === id);
}

export function portalById(id: string): PortalDef | undefined {
  return portals.find((p) => p.id === id);
}

export function shopById(id: string): ShopDef | undefined {
  return shops.find((s) => s.id === id);
}

export function shopByNpcId(npcId: string): ShopDef | undefined {
  return shops.find((s) => s.npcId === npcId);
}

export function questById(id: string): QuestDef | undefined {
  return quests.find((q) => q.id === id);
}

export function classById(id: string): ClassDef | undefined {
  const migrated = migrateClassId(id);
  return classes.find((c) => c.id === migrated);
}
