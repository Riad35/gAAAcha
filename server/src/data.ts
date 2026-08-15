import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import type { BannerDef, ClassDef, ItemDef, MapDef, MonsterDef, SkillDef, SpiritDef, WeaponDef } from "./types.js";

const dataDir = join(dirname(fileURLToPath(import.meta.url)), "..", "data");

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

export const defaultMap = maps[0];
export const defaultClass = classes[0];
export const defaultBanner = banners[0];

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
