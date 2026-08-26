using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// MMO client log: tagged console + rotating files + flight-recorder ring.
/// Format: 2026-08-20T01:18:00.412Z  WARN   GFX      reason=width_not_divisible
/// F9 dump · F10 verbosity (INFO → DEBUG → TRACE).
/// </summary>
public static class GameLog
{
    public enum Channel
    {
        Net,
        Combat,
        World,
        Gfx,
        Ui,
        Persist,
        Social,
        Sys,
    }

    public enum Level
    {
        Error = 0,
        Warn = 1,
        Info = 2,
        Debug = 3,
        Trace = 4,
    }

    private const int RingCapacity = 200;
    private const long MaxBytes = 5L * 1024 * 1024;
    private const int MaxFiles = 5;

    public static Level MinLevel = Level.Info;

    private static readonly string[] Ring = new string[RingCapacity];
    private static int _write;
    private static int _count;
    private static readonly HashSet<string> OnceKeys = new HashSet<string>();
    private static string _dir;
    private static string _lastDumpPath = "";
    private static string _lastMapId = "";

    public static string LastDumpPath => _lastDumpPath;

    public static string LevelName => MinLevel.ToString().ToUpperInvariant();

    public static void CycleLevel()
    {
        MinLevel = MinLevel switch
        {
            Level.Info => Level.Debug,
            Level.Debug => Level.Trace,
            _ => Level.Info,
        };
        Info(Channel.Sys, "log level " + LevelName + "  (F10 cycles, F9 dumps ring)");
    }

    public static void Info(Channel channel, string message) => Write(Level.Info, channel, message);

    public static void Warn(Channel channel, string message) => Write(Level.Warn, channel, message);

    public static void Error(Channel channel, string message) => Write(Level.Error, channel, message);

    public static void DebugLine(Channel channel, string message) => Write(Level.Debug, channel, message);

    public static void Trace(Channel channel, string message) => Write(Level.Trace, channel, message);

    public static void WarnOnce(Channel channel, string key, string message)
    {
        if (string.IsNullOrEmpty(key) || !OnceKeys.Add(key))
        {
            return;
        }

        Warn(channel, message);
    }

    public static void Packet(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return;
        }

        var type = JsonUtil.ExtractString(json, "type");
        if (string.IsNullOrEmpty(type))
        {
            type = "?";
        }

        if (MinLevel >= Level.Trace)
        {
            var trim = json.Length > 400 ? json.Substring(0, 400) + "…" : json;
            Trace(Channel.Net, "raw type=" + type + "  " + trim);
        }
        else
        {
            DebugLine(Channel.Net, "recv type=" + type);
        }

        switch (type)
        {
            case "error":
            {
                var code = JsonUtil.ExtractString(json, "code");
                var msg = JsonUtil.ExtractString(json, "message");
                var line = FormatServerError(code, msg);
                if (code == "blocked" || code == "blocked_entity" || code == "too_fast"
                    || code == "rate_limited" || code == "move_locked")
                {
                    WarnOnce(ChannelForError(code), "err:" + code, line);
                }
                else
                {
                    Warn(ChannelForError(code), line);
                }

                break;
            }
            case "sync_state":
            {
                var map = JsonUtil.ExtractObject(json, "map");
                var mapId = JsonUtil.ExtractString(map, "id");
                var you = JsonUtil.ExtractObject(json, "you");
                if (you.Length == 0)
                {
                    you = JsonUtil.SliceAround(json, "\"you\"", 0, 500);
                }

                var youMap = JsonUtil.ExtractString(you, "mapId");
                var key = string.IsNullOrEmpty(youMap) ? mapId : youMap;
                if (key != _lastMapId)
                {
                    _lastMapId = key;
                    JsonUtil.TryInt(map, "width", out var w);
                    JsonUtil.TryInt(map, "height", out var h);
                    Info(Channel.World, "map=" + key + "  size=" + w + "x" + h);
                }
                else
                {
                    DebugLine(Channel.World, "sync_state map=" + key);
                }

                break;
            }
            case "sync_skill":
            case "sync_aoe":
            {
                var skill = JsonUtil.ExtractString(json, "skillId");
                var caster = JsonUtil.ExtractString(json, "casterId");
                var target = JsonUtil.ExtractString(json, "targetId");
                JsonUtil.TryNumber(json, "damage", out var dmg);
                Info(Channel.Combat, "skill=" + skill + "  caster=" + ShortId(caster) +
                                     "  target=" + ShortId(target) + "  dmg=" + dmg.ToString("0"));
                break;
            }
            case "sync_vitals":
            {
                var id = JsonUtil.ExtractString(json, "entityId");
                if (string.IsNullOrEmpty(id))
                {
                    id = JsonUtil.ExtractString(json, "id");
                }

                JsonUtil.TryInt(json, "hp", out var hp);
                JsonUtil.TryInt(json, "maxHp", out var maxHp);
                DebugLine(Channel.Combat, "vitals  entity=" + ShortId(id) + "  hp=" + hp + "/" + maxHp);
                break;
            }
            case "sync_despawn":
                Info(Channel.Combat, "despawn  entity=" + ShortId(JsonUtil.ExtractString(json, "entityId")) +
                                     "  reason=" + JsonUtil.ExtractString(json, "reason"));
                break;
            case "sync_spawn":
                Info(Channel.World, "spawn  entity=" + ShortId(JsonUtil.ExtractString(json, "id")) +
                                    "  name=" + JsonUtil.ExtractString(json, "name"));
                break;
            case "sync_status":
            case "sync_cond":
            case "sync_pong":
                if (MinLevel >= Level.Debug)
                {
                    DebugLine(Channel.Net, "msg type=" + type);
                }

                break;
            case "sync_move":
                if (MinLevel >= Level.Trace)
                {
                    JsonUtil.TryNumber(json, "x", out var mx);
                    JsonUtil.TryNumber(json, "y", out var my);
                    Trace(Channel.World, "move  entity=" + ShortId(JsonUtil.ExtractString(json, "entityId")) +
                                         "  @" + mx.ToString("0.0") + "," + my.ToString("0.0"));
                }

                break;
            case "sync_auth":
                Info(Channel.Sys, "auth ok  user=" + JsonUtil.ExtractString(json, "username"));
                break;
            case "sync_chat":
                Info(Channel.Social, "chat  channel=" + JsonUtil.ExtractString(json, "channel") +
                                     "  from=" + JsonUtil.ExtractString(json, "from"));
                break;
            case "sync_gacha":
                Info(Channel.Ui, "gacha results");
                break;
            case "sync_loot":
                Info(Channel.Combat, "loot  item=" + JsonUtil.ExtractString(json, "itemId"));
                break;
            default:
                DebugLine(Channel.Net, "msg type=" + type);
                break;
        }
    }

    public static void CastLocal(string skillId, string targetId, string note = null)
    {
        var line = "local skill=" + skillId;
        if (!string.IsNullOrEmpty(targetId))
        {
            line += "  target=" + ShortId(targetId);
        }

        if (!string.IsNullOrEmpty(note))
        {
            line += "  note=" + note.Replace(' ', '_');
        }

        DebugLine(Channel.Combat, line);
    }

    public static string DumpRing()
    {
        EnsureDir();
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var path = Path.Combine(_dir, "crash-" + stamp + ".log");
        var n = _count;
        var sb = new StringBuilder(n * 80);
        for (var i = 0; i < n; i++)
        {
            var idx = (_write - n + i + RingCapacity * 4) % RingCapacity;
            sb.Append(Ring[idx]);
            sb.Append('\n');
        }

        try
        {
            File.WriteAllText(path, n == 0 ? "(empty ring)\n" : sb.ToString());
            _lastDumpPath = path;
            Info(Channel.Sys, "dump  path=" + path);
        }
        catch (Exception ex)
        {
            Error(Channel.Sys, "dump failed  err=" + ex.Message);
        }

        return _lastDumpPath;
    }

    public static string RecentText(int maxLines = 8)
    {
        maxLines = Mathf.Clamp(maxLines, 1, RingCapacity);
        var n = Mathf.Min(maxLines, _count);
        if (n <= 0)
        {
            return "";
        }

        var sb = new StringBuilder(n * 48);
        for (var i = n - 1; i >= 0; i--)
        {
            var idx = (_write - 1 - i + RingCapacity * 4) % RingCapacity;
            if (i < n - 1)
            {
                sb.Append('\n');
            }

            var line = Ring[idx] ?? "";
            sb.Append(line.Length > 26 ? line.Substring(26) : line);
        }

        return sb.ToString();
    }

    private static bool ShouldWrite(Level level) => (int)level <= (int)MinLevel;

    private static void Write(Level level, Channel channel, string message)
    {
        if (!ShouldWrite(level))
        {
            return;
        }

        var iso = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fff") + "Z";
        var line = iso + "  " + LevelTag(level).PadRight(5) + "  " + ChannelTag(channel).PadRight(7) + "  " + message;
        PushRing(line);
        AppendFile(line);
        switch (level)
        {
            case Level.Warn:
                Debug.LogWarning(line);
                break;
            case Level.Error:
                Debug.LogError(line);
                break;
            default:
                Debug.Log(line);
                break;
        }

        if (level == Level.Error)
        {
            DumpRing();
        }
    }

    private static string LevelTag(Level level) => level switch
    {
        Level.Error => "ERROR",
        Level.Warn => "WARN",
        Level.Debug => "DEBUG",
        Level.Trace => "TRACE",
        _ => "INFO",
    };

    private static string ChannelTag(Channel channel) => channel switch
    {
        Channel.Net => "NET",
        Channel.Combat => "COMBAT",
        Channel.World => "WORLD",
        Channel.Gfx => "GFX",
        Channel.Ui => "UI",
        Channel.Persist => "PERSIST",
        Channel.Social => "SOCIAL",
        _ => "SYS",
    };

    private static void PushRing(string line)
    {
        Ring[_write] = line;
        _write = (_write + 1) % RingCapacity;
        if (_count < RingCapacity)
        {
            _count += 1;
        }
    }

    private static void EnsureDir()
    {
        if (!string.IsNullOrEmpty(_dir))
        {
            return;
        }

        _dir = Path.Combine(Application.persistentDataPath, "gAAAcha", "logs");
        try
        {
            Directory.CreateDirectory(_dir);
        }
        catch
        {
            _dir = Path.Combine(Application.temporaryCachePath, "gAAAcha-logs");
            Directory.CreateDirectory(_dir);
        }
    }

    private static void AppendFile(string line)
    {
        try
        {
            EnsureDir();
            var path = Path.Combine(_dir, "game.log");
            RotateIfNeeded(path);
            File.AppendAllText(path, line + "\n");
        }
        catch
        {
            // never throw from logging
        }
    }

    private static void RotateIfNeeded(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return;
            }

            var info = new FileInfo(path);
            if (info.Length < MaxBytes)
            {
                return;
            }

            var oldest = Path.Combine(_dir, "game." + (MaxFiles - 1) + ".log");
            if (File.Exists(oldest))
            {
                File.Delete(oldest);
            }

            for (var i = MaxFiles - 2; i >= 1; i--)
            {
                var from = Path.Combine(_dir, "game." + i + ".log");
                var to = Path.Combine(_dir, "game." + (i + 1) + ".log");
                if (File.Exists(from))
                {
                    if (File.Exists(to))
                    {
                        File.Delete(to);
                    }

                    File.Move(from, to);
                }
            }

            var first = Path.Combine(_dir, "game.1.log");
            if (File.Exists(first))
            {
                File.Delete(first);
            }

            File.Move(path, first);
        }
        catch
        {
            // ignore rotate failures
        }
    }

    private static Channel ChannelForError(string code)
    {
        switch (code)
        {
            case "bad_packet":
            case "rate_limited":
                return Channel.Net;
            case "bad_portal":
            case "wrong_map":
            case "too_far":
            case "tower_locked":
            case "switch_locked":
            case "bad_map":
                return Channel.World;
            case "inventory_full":
            case "not_enough_gold":
            case "missing_item":
                return Channel.Ui;
            default:
                return Channel.Combat;
        }
    }

    private static string FormatServerError(string code, string msg)
    {
        if (!string.IsNullOrEmpty(msg))
        {
            return string.IsNullOrEmpty(code) ? msg : msg + "  [" + code + "]";
        }

        return string.IsNullOrEmpty(code) ? "Server rejected a packet" : "Server rejected: " + code;
    }

    private static string ShortId(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return "?";
        }

        return id.Length <= 28 ? id : id.Substring(0, 12) + "…" + id.Substring(id.Length - 6);
    }
}
