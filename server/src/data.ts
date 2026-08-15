import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import type { ClassDef, MapDef, MonsterDef, SkillDef } from "./types.js";

const dataDir = join(dirname(fileURLToPath(import.meta.url)), "..", "data");

function loadJson<T>(name: string): T {
  return JSON.parse(readFileSync(join(dataDir, name), "utf8")) as T;
}

export const maps = loadJson<MapDef[]>("maps.json");
export const classes = loadJson<ClassDef[]>("classes.json");
export const skills = loadJson<SkillDef[]>("skills.json");
export const monsters = loadJson<MonsterDef[]>("monsters.json");

export const defaultMap = maps[0];
export const defaultClass = classes[0];

export function skillById(id: string): SkillDef | undefined {
  return skills.find((skill) => skill.id === id);
}
