import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import type { CombatConfig } from "./types.js";

const root = join(dirname(fileURLToPath(import.meta.url)), "..", "..");

let cached: CombatConfig | null = null;

export function loadCombatConfig(): CombatConfig {
  if (cached) {
    return cached;
  }
  const raw = JSON.parse(
    readFileSync(join(root, "data", "combat-config.json"), "utf8"),
  ) as CombatConfig;
  cached = raw;
  return raw;
}

/** Test helper — clear cache after mutating config file or injecting fixtures. */
export function clearCombatConfigCache(): void {
  cached = null;
}
