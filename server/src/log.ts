/**
 * Tagged MMO logger. Files under .runtime/logs (gitignored).
 * Tests: NODE_TEST_CONTEXT disables file + console unless configureLog() says otherwise.
 */
import { appendFileSync, existsSync, mkdirSync, renameSync, statSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

export const LOG_LEVELS = ["ERROR", "WARN", "INFO", "DEBUG", "TRACE"] as const;
export type LogLevel = (typeof LOG_LEVELS)[number];
export type LogChannel = "NET" | "COMBAT" | "WORLD" | "GFX" | "UI" | "PERSIST" | "SOCIAL" | "SYS";
export type LogCtx = Record<string, string | number | boolean | null | undefined>;

const LEVEL_RANK: Record<LogLevel, number> = {
  ERROR: 0,
  WARN: 1,
  INFO: 2,
  DEBUG: 3,
  TRACE: 4,
};

const COMBAT_CODES = new Set([
  "stunned",
  "move_locked",
  "invalid_move",
  "blocked",
  "blocked_entity",
  "too_fast",
  "bad_aim",
  "unknown_skill",
  "locked_skill",
  "blinded",
  "on_cooldown",
  "not_enough_mana",
  "invalid_target",
  "target_dead",
  "out_of_range",
  "no_targets",
  "you_are_dead",
]);

const WORLD_CODES = new Set([
  "bad_portal",
  "wrong_map",
  "too_far",
  "tower_locked",
  "switch_locked",
  "bad_map",
]);

const SOCIAL_CODES = new Set([
  "bad_invite",
  "player_not_found",
  "already_in_party",
  "party_full",
  "invite_gone",
  "empty_chat",
  "no_whisper_target",
  "busy",
  "no_trade",
  "already_friend",
  "friends_full",
  "not_friend",
  "in_guild",
  "no_guild",
  "own_listing",
]);

const UI_CODES = new Set([
  "inventory_full",
  "not_enough_gold",
  "missing_item",
  "bad_shop",
  "bad_item",
  "not_usable",
  "unknown_banner",
  "invalid_pull",
  "already_unlocked",
  "bad_skill",
  "no_points",
  "auction_full",
  "gone",
]);

const RING_CAP = 200;
const MAX_BYTES = 5 * 1024 * 1024;
const MAX_FILES = 5;

let minLevel: LogLevel = "INFO";
let fileEnabled = true;
let consoleEnabled = true;
let logDir = "";
let inited = false;
const ring: string[] = [];
let lastDumpPath = "";

function isTestRuntime(): boolean {
  return Boolean(process.env.NODE_TEST_CONTEXT);
}

function defaultDir(): string {
  return join(dirname(fileURLToPath(import.meta.url)), "..", ".runtime", "logs");
}

function parseLevel(raw: string | undefined, fallback: LogLevel): LogLevel {
  const u = (raw ?? "").trim().toUpperCase();
  return (LOG_LEVELS as readonly string[]).includes(u) ? (u as LogLevel) : fallback;
}

function ensureInit(): void {
  if (inited) {
    return;
  }
  inited = true;
  minLevel = parseLevel(process.env.GAAACHA_LOG_LEVEL, "INFO");
  if (isTestRuntime() && process.env.GAAACHA_LOG !== "on") {
    fileEnabled = false;
    consoleEnabled = false;
    return;
  }
  if (process.env.GAAACHA_LOG === "off") {
    fileEnabled = false;
  }
  logDir = defaultDir();
  mkdirSync(logDir, { recursive: true });
}

export function configureLog(opts: {
  minLevel?: LogLevel;
  file?: boolean;
  console?: boolean;
  dir?: string;
}): void {
  inited = true;
  if (opts.minLevel) {
    minLevel = opts.minLevel;
  }
  if (opts.file !== undefined) {
    fileEnabled = opts.file;
  }
  if (opts.console !== undefined) {
    consoleEnabled = opts.console;
  }
  if (opts.dir) {
    logDir = opts.dir;
    mkdirSync(logDir, { recursive: true });
  }
}

export function resetLogForTests(): void {
  inited = true;
  minLevel = "INFO";
  fileEnabled = false;
  consoleEnabled = false;
  logDir = "";
  ring.length = 0;
  lastDumpPath = "";
}

export function setMinLevel(level: LogLevel): void {
  minLevel = level;
}

export function getMinLevel(): LogLevel {
  return minLevel;
}

export function shouldLog(level: LogLevel): boolean {
  return LEVEL_RANK[level] <= LEVEL_RANK[minLevel];
}

export function redactSecret(value: string, keep = 8): string {
  if (!value) {
    return "";
  }
  if (value.length <= keep) {
    return `${value.slice(0, Math.min(2, value.length))}…`;
  }
  return `${value.slice(0, keep)}…`;
}

const SENSITIVE = /pass|token|guest|secret|text|password/i;

function sanitizeCtx(ctx?: LogCtx): Record<string, string | number | boolean> {
  const out: Record<string, string | number | boolean> = {};
  if (!ctx) {
    return out;
  }
  for (const [key, raw] of Object.entries(ctx)) {
    if (raw === null || raw === undefined) {
      continue;
    }
    if (SENSITIVE.test(key) && typeof raw === "string") {
      out[key] = redactSecret(raw);
    } else {
      out[key] = raw;
    }
  }
  return out;
}

export function packetRejectReason(raw: string): { why: string; type: string } {
  const text = (raw ?? "").trim();
  if (!text) {
    return { why: "empty packet", type: "(empty)" };
  }
  try {
    const data = JSON.parse(text) as { type?: unknown };
    const type = typeof data?.type === "string" && data.type.length > 0 ? data.type : "(no type field)";
    return { why: "unknown or incomplete packet", type };
  } catch {
    if (/:\s*-?\d+,\d+/.test(text)) {
      return {
        why: "invalid JSON — numbers used a comma (3,5) instead of a dot (3.5)",
        type: "(unparseable)",
      };
    }
    return { why: "invalid JSON", type: "(unparseable)" };
  }
}

export function formatLogLine(
  iso: string,
  level: LogLevel,
  channel: LogChannel,
  msg: string,
  ctx?: LogCtx,
): string {
  const bits: string[] = [
    iso,
    level.padEnd(5),
    channel.padEnd(7),
    msg.replace(/\s+/g, " ").trim(),
  ];
  const clean = sanitizeCtx(ctx);
  for (const [k, v] of Object.entries(clean)) {
    const val = typeof v === "string" ? v.replace(/\s+/g, "_") : String(v);
    bits.push(`${k}=${val}`);
  }
  return bits.join("  ");
}

export function channelForError(code: string | undefined): LogChannel {
  if (!code) {
    return "NET";
  }
  if (COMBAT_CODES.has(code)) {
    return "COMBAT";
  }
  if (WORLD_CODES.has(code)) {
    return "WORLD";
  }
  if (SOCIAL_CODES.has(code)) {
    return "SOCIAL";
  }
  if (UI_CODES.has(code)) {
    return "UI";
  }
  if (code === "rate_limited" || code === "bad_packet" || code === "bad_slot" || code === "empty_slot" || code === "slot_taken") {
    return "NET";
  }
  return "NET";
}

function currentFile(): string {
  return join(logDir || defaultDir(), "server.log");
}

function rotateIfNeeded(path: string): void {
  if (!existsSync(path)) {
    return;
  }
  let size = 0;
  try {
    size = statSync(path).size;
  } catch {
    return;
  }
  if (size < MAX_BYTES) {
    return;
  }
  const oldest = join(logDir, `server.${MAX_FILES - 1}.log`);
  if (existsSync(oldest)) {
    try {
      renameSync(oldest, join(logDir, `server.${MAX_FILES - 1}.bak`));
    } catch {
      /* ignore */
    }
  }
  for (let i = MAX_FILES - 2; i >= 1; i--) {
    const from = join(logDir, `server.${i}.log`);
    const to = join(logDir, `server.${i + 1}.log`);
    if (existsSync(from)) {
      try {
        renameSync(from, to);
      } catch {
        /* ignore */
      }
    }
  }
  try {
    renameSync(path, join(logDir, "server.1.log"));
  } catch {
    /* ignore */
  }
}

function pushRing(line: string): void {
  ring.push(line);
  if (ring.length > RING_CAP) {
    ring.shift();
  }
}

function write(level: LogLevel, channel: LogChannel, msg: string, ctx?: LogCtx): string | null {
  ensureInit();
  if (!shouldLog(level)) {
    return null;
  }
  const line = formatLogLine(new Date().toISOString(), level, channel, msg, ctx);
  pushRing(line);
  if (consoleEnabled) {
    if (level === "ERROR") {
      console.error(line);
    } else if (level === "WARN") {
      console.warn(line);
    } else {
      console.log(line);
    }
  }
  if (fileEnabled) {
    try {
      if (!logDir) {
        logDir = defaultDir();
        mkdirSync(logDir, { recursive: true });
      }
      const path = currentFile();
      rotateIfNeeded(path);
      appendFileSync(path, `${line}\n`, "utf8");
    } catch (err) {
      if (consoleEnabled) {
        console.error("log file write failed", (err as Error).message);
      }
    }
  }
  if (level === "ERROR") {
    dumpRing();
  }
  return line;
}

export function dumpRing(label?: string): string {
  ensureInit();
  const stamp = new Date().toISOString().replace(/[-:]/g, "").replace(/\.\d+Z$/, "Z").replace("T", "-");
  const name = `crash-${stamp}${label ? `-${label}` : ""}.log`;
  const body = ring.length ? `${ring.join("\n")}\n` : "(empty ring)\n";
  if (!fileEnabled && isTestRuntime()) {
    lastDumpPath = name;
    return lastDumpPath;
  }
  try {
    if (!logDir) {
      logDir = defaultDir();
    }
    mkdirSync(logDir, { recursive: true });
    const path = join(logDir, name);
    writeFileSync(path, body, "utf8");
    lastDumpPath = path;
    return path;
  } catch {
    lastDumpPath = name;
    return lastDumpPath;
  }
}

export function getRingSnapshot(): string[] {
  return [...ring];
}

export function getLastDumpPath(): string {
  return lastDumpPath;
}

export const log = {
  error: (channel: LogChannel, msg: string, ctx?: LogCtx) => write("ERROR", channel, msg, ctx),
  warn: (channel: LogChannel, msg: string, ctx?: LogCtx) => write("WARN", channel, msg, ctx),
  info: (channel: LogChannel, msg: string, ctx?: LogCtx) => write("INFO", channel, msg, ctx),
  debug: (channel: LogChannel, msg: string, ctx?: LogCtx) => write("DEBUG", channel, msg, ctx),
  trace: (channel: LogChannel, msg: string, ctx?: LogCtx) => write("TRACE", channel, msg, ctx),
};
