using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Craftpix sheets: split into frames, play idle/walk clips. File load via StreamingAssets.
/// </summary>
public static partial class SpriteCatalog
{
    public enum Shape
    {
        Square,
        Diamond,
        Circle,
        Hex,
        Cross,
    }

    public enum Clip
    {
        Idle,
        Walk,
        Run,
        Attack,
        WalkAttack,
        RunAttack,
        Hurt,
        Death,
        Pickup,
        Skill,
        Emote,
    }

    private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();
    private static readonly Dictionary<string, Texture2D> TexCache = new Dictionary<string, Texture2D>();
    private static readonly Dictionary<string, Sprite[]> ClipCache = new Dictionary<string, Sprite[]>();
    private static readonly Dictionary<string, bool> ClipFlipCache = new Dictionary<string, bool>();

    /// <summary>
    /// Enter Play Mode Options has Disable Domain Reload. Static dictionaries survive
    /// Play exit, but the Texture2D / Sprite objects inside them do not. Drop them here
    /// so the next Play loads PNGs from disk again.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlayEnter()
    {
        ResetRuntimeCaches();
    }

    public static void ResetRuntimeCaches()
    {
        Cache.Clear();
        TexCache.Clear();
        ClipCache.Clear();
        ClipFlipCache.Clear();
        AnimKitsTried = false;
        LoadedKits = null;
        LoadedInfos = null;
        ClipByKey.Clear();
        PoseById.Clear();
        LastClipFlipX = false;
    }

    /// <summary>Clear stale Play-Mode textures, then LoadImage every wired clip.</summary>
    public static int Warmup()
    {
        ResetRuntimeCaches();
        var n = AuditWiredClips();
        GameLog.Info(GameLog.Channel.Gfx, "sprite warmup done issues=" + n + " clips=" + ClipCache.Count);
        return n;
    }

    private static bool TryLiveClip(string key, out Sprite[] frames)
    {
        if (ClipCache.TryGetValue(key, out frames) && ClipAlive(frames))
        {
            return true;
        }

        frames = null;
        return false;
    }

    /// <summary>True if the array still points at a live Unity texture (not a Play-exit leftover).</summary>
    private static bool ClipAlive(Sprite[] frames)
    {
        if (frames == null || frames.Length == 0)
        {
            return false;
        }

        for (var i = 0; i < frames.Length; i++)
        {
            if (frames[i] != null && frames[i].texture != null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>StreamingAssets body prefix: female (8-dir), player / player_fgt / player_rog (4-dir sheets).</summary>
    public static string PlayerSheetPrefix { get; private set; } = "female";
    public static string HeldWeaponId { get; private set; } = "";

    /// <summary>Female body pack under Assets/_Project/Art/Sprites/player/female.</summary>
    public static readonly bool PlayerBodySpritesEnabled = true;

    public static void SetHeldWeapon(string weaponId)
    {
        HeldWeaponId = weaponId ?? "";
    }

    public static bool HeldIsBow()
    {
        var id = HeldWeaponId ?? "";
        return id.IndexOf("bow", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static bool HasBowBodyArt()
    {
        if (!PlayerBodySpritesEnabled)
        {
            return false;
        }

        return TryFileSprite("Sprites/player/female/idle_holding_longbow/holding_longbow/rotations_longbow/south") != null
            || TryFileSprite("Sprites/player/female/idle_holding_longbow/holding_longbow/rotations_longbow/east") != null;
    }

    public static bool HeldIsSword()
    {
        var id = HeldWeaponId ?? "";
        return id.IndexOf("sword", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static bool HasSwordBodyArt()
    {
        if (!PlayerBodySpritesEnabled)
        {
            return false;
        }

        return TryFileSprite("Sprites/player/female/idle_holding_zweihander/holding_zweihander/rotations_zweih/south") != null
            || TryFileSprite("Sprites/player/female/idle_holding_zweihander/holding_zweihander/rotations_zweih/east") != null;
    }

    public static bool HeldIsDagger()
    {
        var id = HeldWeaponId ?? "";
        return id.IndexOf("dagger", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static bool HeldIsStaff()
    {
        var id = HeldWeaponId ?? "";
        return id.IndexOf("staff", System.StringComparison.OrdinalIgnoreCase) >= 0
            || id.IndexOf("tome", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>bow / sword / daggers / staff / unarmed from the equipped mainhand id.</summary>
    public static string StanceWeapon()
    {
        if (string.IsNullOrEmpty(HeldWeaponId))
        {
            return "unarmed";
        }

        if (HeldIsBow())
        {
            return "bow";
        }

        if (HeldIsDagger())
        {
            return "daggers";
        }

        if (HeldIsStaff())
        {
            return "staff";
        }

        if (HeldIsSword())
        {
            return "sword";
        }

        return "unarmed";
    }

    public static bool HasDrawnWeaponBodyArt()
    {
        if (!PlayerBodySpritesEnabled)
        {
            return false;
        }

        var w = StanceWeapon();
        return w == "bow" || w == "sword" || w == "daggers" || w == "staff";
    }

    public static void SetPlayerSheet(string classId)
    {
        PlayerSheetPrefix = classId == "fighter" ? "player_fgt"
            : classId == "rogue" ? "player_rog"
            : "female";
    }

    public static bool UsesEightDir(string id, string kind)
    {
        return IsPlayerKind(id, kind);
    }

    /// <summary>
    /// View-space cardinals (what the player sees). Screen left = west, right = east,
    /// down = south, up = north. Returns 0 S, 2 E, 4 N, 6 W.
    /// </summary>
    public static int FacingFromView(float sx, float sy, int currentFacing = -1)
    {
        if (sx * sx + sy * sy < 1e-10f)
        {
            return currentFacing >= 0 ? SnapToCardinal8(currentFacing) : 0;
        }

        var ax = Mathf.Abs(sx);
        var ay = Mathf.Abs(sy);
        var cur = currentFacing >= 0 ? SnapToCardinal8(currentFacing) : -1;

        // Keep the last side until the stick clearly crosses into a neighbor.
        if (cur == 2 || cur == 6)
        {
            if (ax * 1.25f >= ay)
            {
                return sx < 0f ? 6 : 2;
            }

            return sy < 0f ? 0 : 4;
        }

        if (cur == 0 || cur == 4)
        {
            if (ay * 1.25f >= ax)
            {
                return sy < 0f ? 0 : 4;
            }

            return sx < 0f ? 6 : 2;
        }

        if (ax >= ay)
        {
            return sx < 0f ? 6 : 2;
        }

        return sy < 0f ? 0 : 4;
    }

    /// <summary>
    /// Map-space cardinal. Tile +X is east, tile +Y / world +Z is north.
    /// Prefer <see cref="FacingFromView"/> for player locomotion (camera is the facing basis).
    /// </summary>
    public static int FacingFromWorldXZ(float dx, float dz, int currentFacing = -1)
    {
        if (dx * dx + dz * dz < 1e-8f)
        {
            return currentFacing >= 0 ? SnapToCardinal8(currentFacing) : 0;
        }

        if (Mathf.Abs(dx) >= Mathf.Abs(dz))
        {
            return dx < 0f ? 6 : 2;
        }

        return dz < 0f ? 0 : 4;
    }

    /// <summary>Snap an 8-dir index to north / south / east / west.</summary>
    public static int SnapToCardinal8(int facing8)
    {
        switch (ToFourDirRow(facing8))
        {
            case 1:
                return 6;
            case 2:
                return 2;
            case 3:
                return 4;
            default:
                return 0;
        }
    }

    /// <summary>
    /// Screen-space facing. 4-dir: 0 down, 1 left, 2 right, 3 up.
    /// 8-dir: 0 S, 1 SE, 2 E, 3 NE, 4 N, 5 NW, 6 W, 7 SW.
    /// Pass currentFacing to keep the last octant until the stick moves ~32° into a neighbor (stops left/right flicker).
    /// </summary>
    public static int FacingFromScreen(float sx, float sy, bool eightDir, int currentFacing = -1)
    {
        if (sx * sx + sy * sy < 1e-10f)
        {
            return currentFacing >= 0 ? (((currentFacing % 8) + 8) % 8) : 0;
        }

        if (!eightDir)
        {
            if (Mathf.Abs(sx) > Mathf.Abs(sy))
            {
                return sx < 0f ? 1 : 2;
            }

            return sy < 0f ? 0 : 3;
        }

        var angle = Mathf.Atan2(sx, -sy);
        if (angle < 0f)
        {
            angle += Mathf.PI * 2f;
        }

        var raw = Mathf.RoundToInt(angle / (Mathf.PI * 0.25f)) % 8;
        if (currentFacing < 0)
        {
            return raw;
        }

        var cur = ((currentFacing % 8) + 8) % 8;
        if (raw == cur)
        {
            return cur;
        }

        var currentDeg = cur * 45f;
        var angleDeg = angle * Mathf.Rad2Deg;
        if (Mathf.Abs(Mathf.DeltaAngle(currentDeg, angleDeg)) < 32f)
        {
            return cur;
        }

        return raw;
    }

    public static int ToFourDirRow(int facing8)
    {
        switch (((facing8 % 8) + 8) % 8)
        {
            case 0:
            case 1:
                return 0;
            case 6:
            case 7:
                return 1;
            case 2:
            case 3:
                return 2;
            default:
                return 3;
        }
    }

    public static bool FacingLooksLeft(int facing, bool eightDir)
    {
        if (!eightDir)
        {
            return facing == 1;
        }

        return facing == 5 || facing == 6 || facing == 7;
    }

    public static bool FacingFlipX(int facing, string id, string kind)
    {
        // 8-dir packs include real west/NW/SW art. Only flip when GetClip says the
        // frames were borrowed from an east-side folder (see LastClipFlipX).
        return false;
    }

    /// <summary>True if the last GetClip used east-side frames mirrored for a west facing.</summary>
    public static bool LastClipFlipX { get; private set; }

    public static Color TileEven(string mapId)
    {
        if (string.IsNullOrEmpty(mapId))
        {
            return new Color(0.22f, 0.24f, 0.28f);
        }

        if (mapId.StartsWith("town"))
        {
            return new Color(0.28f, 0.26f, 0.22f);
        }

        if (mapId.StartsWith("field"))
        {
            return new Color(0.2f, 0.32f, 0.18f);
        }

        if (mapId.Contains("marsh"))
        {
            return new Color(0.18f, 0.28f, 0.24f);
        }

        if (mapId.StartsWith("dungeon") || mapId.StartsWith("tower"))
        {
            return new Color(0.16f, 0.14f, 0.22f);
        }

        return new Color(0.22f, 0.24f, 0.28f);
    }

    public static Color TileOdd(string mapId)
    {
        var even = TileEven(mapId);
        return new Color(even.r * 0.78f, even.g * 0.78f, even.b * 0.78f);
    }

    public static Color Wall(string mapId)
    {
        if (!string.IsNullOrEmpty(mapId) && (mapId.StartsWith("dungeon") || mapId.StartsWith("tower")))
        {
            return new Color(0.35f, 0.2f, 0.45f);
        }

        if (!string.IsNullOrEmpty(mapId) && mapId.StartsWith("field"))
        {
            return new Color(0.35f, 0.4f, 0.22f);
        }

        return new Color(0.55f, 0.28f, 0.2f);
    }

    public static Shape ShapeFor(string id, string kind)
    {
        if (kind == "npc" || (!string.IsNullOrEmpty(id) && id.StartsWith("npc_")))
        {
            return Shape.Hex;
        }

        if (kind == "player" || (!string.IsNullOrEmpty(id) && id.StartsWith("player_")))
        {
            return Shape.Diamond;
        }

        if (!string.IsNullOrEmpty(id) && (id.Contains("boss") || id.Contains("crypt_lord")))
        {
            return Shape.Cross;
        }

        return Shape.Circle;
    }

    public static bool IsPlayerKind(string id, string kind)
    {
        return kind == "player" || (!string.IsNullOrEmpty(id) && id.StartsWith("player_"));
    }

    public static bool IsMonsterKind(string id, string kind)
    {
        return kind == "monster"
            || (!string.IsNullOrEmpty(id) && (id.StartsWith("monster_") || id.StartsWith("lab_")
                || id.Contains("slime") || id.Contains("orc") || id.Contains("plant")
                || id.Contains("tower_") || id.Contains("brute") || id.Contains("pest")));
    }

    /// <summary>StreamingAssets body prefix: slime/slime2/slime3, orc/orc2/orc3, plant/plant2/plant3.</summary>
    public static string MonsterBody(string id)
    {
        var s = id ?? "";
        if (ContainsAny(s, "king_slime", "king", "m_boss_f5", "tower_boss_f5", "apex"))
        {
            return "slime2";
        }

        if (ContainsAny(s, "pest", "beetle", "rat"))
        {
            return "slime3";
        }

        if (ContainsAny(s, "brute"))
        {
            return "orc2";
        }

        if (ContainsAny(s, "knight", "elite", "guard", "warden", "crypt_boss", "m_boss_f2", "tower_boss_f2"))
        {
            return "orc3";
        }

        if (ContainsAny(s, "ruins", "colossus", "marsh", "wisp", "golem"))
        {
            return "plant2";
        }

        if (ContainsAny(s, "plant"))
        {
            return "plant3";
        }

        if (ContainsAny(s, "crypt_skel", "skel") ||
            (ContainsAny(s, "crypt") && !ContainsAny(s, "boss", "warden", "lord")))
        {
            return "skel";
        }

        if (ContainsAny(s, "orc", "shade", "shadow"))
        {
            return "orc";
        }

        return "slime";
    }

    private static bool ContainsAny(string id, params string[] parts)
    {
        for (var i = 0; i < parts.Length; i++)
        {
            if (id.IndexOf(parts[i], System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    public static Sprite ForEntity(string id, string kind)
    {
        var frames = GetClip(id, kind, Clip.Idle);
        if (frames != null && frames.Length > 0)
        {
            return frames[0];
        }

        var shape = ShapeFor(id, kind);
        var fill = kind == "npc" || (!string.IsNullOrEmpty(id) && id.StartsWith("npc_"))
            ? new Color(0.55f, 0.75f, 0.95f)
            : kind == "portal"
                ? new Color(0.7f, 0.5f, 1f)
                : new Color(0.85f, 0.35f, 0.35f);
        if (IsPlayerKind(id, kind))
        {
            fill = new Color(0.35f, 0.7f, 0.95f);
        }

        if (IsPlayerKind(id, kind) || IsMonsterKind(id, kind))
        {
            GameLog.WarnOnce(GameLog.Channel.Gfx, "fallback-shape:" + kind + ":" + id,
                "fallback=shape  entity=" + id + "  kind=" + kind + "  shape=" + shape);
        }

        return MakeShape(shape, fill);
    }

    public static bool IsNpcKind(string id, string kind)
    {
        return kind == "npc" || (!string.IsNullOrEmpty(id) && id.StartsWith("npc_"));
    }

    public static bool IsPortalKind(string id, string kind)
    {
        return kind == "portal" || (!string.IsNullOrEmpty(id) && id.StartsWith("portal_"));
    }

    public static Color ClassTint(string classId)
    {
        switch (classId)
        {
            case "fighter":
            case "rogue":
                return Color.white;
            case "mage":
                return new Color(0.72f, 0.82f, 1f, 1f);
            case "marksman":
            case "archer":
                return new Color(0.75f, 1f, 0.75f, 1f);
            default:
                return Color.white;
        }
    }

    /// <summary>Idle / walk / run / attack / hurt / death frames. facingRow is 0–3 (4-dir sheet) or 0–7 (player 8-dir).</summary>
    public static Sprite[] GetClip(string id, string kind, Clip clip, int facingRow = 0, bool inCombat = false, string variant = null)
    {
        LastClipFlipX = false;
        if (id == "npc_class_master" || id == "npc_specialist")
        {
            var idle = GetEightDirClip(Clip.Idle, facingRow, false, null, out var fx);
            LastClipFlipX = fx;
            if (idle != null && idle.Length > 0)
            {
                return idle;
            }
        }

        if (IsNpcKind(id, kind) || IsPortalKind(id, kind))
        {
            var role = PropRole(id, kind);
            var key = "propclip:" + role;
            if (TryLiveClip(key, out var cachedProp))
            {
                return cachedProp;
            }

            var one = new[] { PixelProp(role) };
            ClipCache[key] = one;
            return one;
        }

        if (IsPlayerKind(id, kind) && !PlayerBodySpritesEnabled)
        {
            return null;
        }

        if (UsesEightDir(id, kind))
        {
            return GetEightDirClip(clip, facingRow, inCombat, variant, out _);
        }

        if (IsMonsterKind(id, kind) && IsSkeletonId(id))
        {
            return PixelSkeletonClip(clip);
        }

        string path = null;
        if (IsPlayerKind(id, kind))
        {
            var prefix = string.IsNullOrEmpty(PlayerSheetPrefix) ? "player" : PlayerSheetPrefix;
            path = clip switch
            {
                Clip.Walk => "Sprites/" + prefix + "_walk",
                Clip.Run => "Sprites/" + prefix + "_run",
                Clip.Attack => "Sprites/" + prefix + "_attack",
                Clip.WalkAttack => "Sprites/" + prefix + "_walk_attack",
                Clip.RunAttack => "Sprites/" + prefix + "_run_attack",
                Clip.Hurt => "Sprites/" + prefix + "_hurt",
                Clip.Death => "Sprites/" + prefix + "_death",
                _ => "Sprites/" + prefix + "_idle",
            };
        }
        else if (IsMonsterKind(id, kind))
        {
            var body = MonsterBody(id);
            path = clip switch
            {
                Clip.Walk => "Sprites/" + body + "_walk",
                Clip.Run => "Sprites/" + body + "_run",
                Clip.Attack => "Sprites/" + body + "_attack",
                Clip.WalkAttack => "Sprites/" + body + "_walk_attack",
                Clip.RunAttack => "Sprites/" + body + "_run_attack",
                Clip.Hurt => "Sprites/" + body + "_hurt",
                Clip.Death => "Sprites/" + body + "_death",
                _ => "Sprites/" + body + "_idle",
            };
        }

        if (path == null)
        {
            return null;
        }

        return SliceSheet(path, facingRow);
    }

    private static Sprite[] LoadFrameFolder(string resourceDir, float pixelsPerUnit = 0f, bool groundAlign = false)
    {
        var ppuKey = pixelsPerUnit > 0f ? pixelsPerUnit.ToString("0") : "h";
        var cacheKey = "seq:" + resourceDir + ":" + ppuKey + (groundAlign ? ":g" : "");
        if (TryLiveClip(cacheKey, out var cached))
        {
            return cached;
        }

        var disk = ResolveSpriteDir(resourceDir);
        if (!Directory.Exists(disk))
        {
            return null;
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(disk, "frame_*.png");
            if (files == null || files.Length == 0)
            {
                files = Directory.GetFiles(disk, "*.png");
            }
        }
        catch
        {
            return null;
        }

        if (files == null || files.Length == 0)
        {
            return null;
        }

        if (FolderIsFacingStills(files))
        {
            return null;
        }

        System.Array.Sort(files, System.StringComparer.OrdinalIgnoreCase);
        var packed = new List<Sprite>(files.Length);
        for (var i = 0; i < files.Length; i++)
        {
            var key = resourceDir + "/" + Path.GetFileNameWithoutExtension(files[i]) + ":" + ppuKey +
                      (groundAlign ? ":g" : "");
            if (Cache.TryGetValue(key, out var existing) && existing != null)
            {
                packed.Add(existing);
                continue;
            }

            var tex = LoadPngFile(files[i]);
            if (tex == null || !TextureHasVisiblePixels(tex))
            {
                continue;
            }

            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.name = Path.GetFileName(files[i]);
            try
            {
                var ppu = pixelsPerUnit > 0f ? pixelsPerUnit : Mathf.Max(tex.height, 64);
                var sp = Sprite.Create(
                    tex,
                    new Rect(0, 0, tex.width, tex.height),
                    groundAlign ? PivotAtFeet(tex) : new Vector2(0.5f, 0f),
                    ppu);
                Cache[key] = sp;
                packed.Add(sp);
            }
            catch
            {
                // skip bad frame
            }
        }

        if (packed.Count == 0)
        {
            return null;
        }

        var frames = packed.ToArray();
        ClipCache[cacheKey] = frames;
        return frames;
    }

    /// <summary>
    /// Rotation packs are 8 stills named south/east/north_east/…. Never treat those as a clip.
    /// </summary>
    private static bool FolderIsFacingStills(string[] files)
    {
        if (files == null || files.Length < 2)
        {
            return false;
        }

        var stills = 0;
        var numbered = 0;
        for (var i = 0; i < files.Length; i++)
        {
            var name = Path.GetFileNameWithoutExtension(files[i]) ?? "";
            if (IsNumberedFrameName(name))
            {
                numbered++;
                continue;
            }

            if (IsFacingStillName(name))
            {
                stills++;
            }
        }

        return stills >= 2 && numbered == 0;
    }

    private static bool IsNumberedFrameName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        if (name.StartsWith("frame_", System.StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var i = name.LastIndexOf('_');
        if (i < 0 || i >= name.Length - 1)
        {
            return false;
        }

        var digits = 0;
        for (var c = i + 1; c < name.Length; c++)
        {
            if (!char.IsDigit(name[c]))
            {
                return false;
            }

            digits++;
        }

        return digits >= 3;
    }

    private static bool IsFacingStillName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        var s = name.ToLowerInvariant().Replace('_', '-');
        switch (s)
        {
            case "south":
            case "south-east":
            case "east":
            case "north-east":
            case "north":
            case "north-west":
            case "west":
            case "south-west":
            case "southeast":
            case "northeast":
            case "northwest":
            case "southwest":
            case "east2":
            case "west2":
                return true;
            default:
                return false;
        }
    }

    /// <summary>Pivot at the lowest opaque row so padded 176/180 canvases stand on the ground.</summary>
    private static Vector2 PivotAtFeet(Texture2D tex)
    {
        if (tex == null)
        {
            return new Vector2(0.5f, 0f);
        }

        var w = tex.width;
        var h = tex.height;
        if (w <= 0 || h <= 0)
        {
            return new Vector2(0.5f, 0f);
        }

        Color32[] pixels;
        try
        {
            pixels = tex.GetPixels32();
        }
        catch
        {
            return new Vector2(0.5f, 0f);
        }

        // GetPixels32 is left-to-right, bottom-to-top. y = 0 is the canvas bottom.
        for (var y = 0; y < h; y++)
        {
            var row = y * w;
            for (var x = 0; x < w; x++)
            {
                if (pixels[row + x].a > 16)
                {
                    return new Vector2(0.5f, y / (float)h);
                }
            }
        }

        return new Vector2(0.5f, 0f);
    }

    public static Sprite ForFloor(string mapId, bool even)
    {
        string path;
        if (!string.IsNullOrEmpty(mapId) && mapId.Contains("marsh"))
        {
            path = even ? "Tiles/floor_mud" : "Tiles/floor_roots";
        }
        else if (!string.IsNullOrEmpty(mapId) && mapId.StartsWith("town"))
        {
            path = even ? "Tiles/floor_wood" : "Tiles/floor_cement";
        }
        else if (!string.IsNullOrEmpty(mapId) && (mapId.StartsWith("dungeon") || mapId.StartsWith("tower")))
        {
            path = even ? "Tiles/floor_brick2" : "Tiles/floor_rubble";
        }
        else if (!string.IsNullOrEmpty(mapId) && mapId.StartsWith("field"))
        {
            path = even ? "Tiles/floor_grass" : "Tiles/floor_grass2";
        }
        else
        {
            path = even ? "Tiles/floor_dirt" : "Tiles/floor_path";
        }

        var key = path + (even ? "_even" : "_odd");
        if (Cache.TryGetValue(key, out var cached) && cached != null)
        {
            return cached;
        }

        var full = FullTextureSprite(path, 256f);
        if (full == null)
        {
            return MakeShape(Shape.Square, even ? TileEven(mapId) : TileOdd(mapId));
        }

        Cache[key] = full;
        return full;
    }

    public static Sprite ForLoadArt(string mapId)
    {
        var biome = LoadBiome(mapId);
        var key = "loadart:" + biome;
        if (Cache.TryGetValue(key, out var cached) && cached != null)
        {
            return cached;
        }

        var sprite = SpriteFromCharMap(LoadArtRows(biome), 8f, 0.5f, 0.5f);
        Cache[key] = sprite;
        return sprite;
    }

    public static Sprite Portrait(string id)
    {
        var role = PortraitRole(id);
        var key = "portrait:" + role;
        if (Cache.TryGetValue(key, out var cached) && cached != null)
        {
            return cached;
        }

        var sprite = SpriteFromCharMap(PortraitRows(role), 12f, 0.5f, 0.5f);
        Cache[key] = sprite;
        return sprite;
    }

    public static Sprite BannerArt()
    {
        const string key = "bannerart";
        if (Cache.TryGetValue(key, out var cached) && cached != null)
        {
            return cached;
        }

        var sprite = SpriteFromCharMap(BannerRows(), 8f, 0.5f, 0.5f);
        Cache[key] = sprite;
        return sprite;
    }

    public static Sprite ClassCardArt(string classId)
    {
        var key = "cardart:" + (classId ?? "adventurer");
        if (Cache.TryGetValue(key, out var cached) && cached != null)
        {
            return cached;
        }

        var sprite = SpriteFromCharMap(ClassCardRows(classId), 12f, 0.5f, 0.5f);
        Cache[key] = sprite;
        return sprite;
    }

    public static Sprite ForWall(string mapId)
    {
        var wall = FullTextureSprite("Tiles/wall_mound", 256f);
        if (wall != null)
        {
            return wall;
        }

        return MakeShape(Shape.Square, Wall(mapId));
    }

    public static Sprite ForProp(string kind)
    {
        return PixelProp(string.IsNullOrEmpty(kind) ? "rock" : kind);
    }

    public static float PropScale(string kind)
    {
        switch (kind)
        {
            case "fountain":
                return 1.7f;
            case "stall":
                return 1.55f;
            case "gate":
                return 1.65f;
            case "crate":
                return 1.15f;
            case "pillar":
                return 1.8f;
            case "chest":
                return 1.25f;
            case "brazier":
                return 1.4f;
            case "bones":
                return 1.2f;
            default:
                return 1.35f;
        }
    }

    public static float EntityScale(string id, string kind)
    {
        const float body = WorldCoords.SpriteWorldHeight;
        const float oldArt = 2.2f;
        if (IsPortalKind(id, kind))
        {
            return body * (1.85f / oldArt);
        }

        if (IsNpcKind(id, kind))
        {
            var npc = (id ?? "").IndexOf("homestone", System.StringComparison.OrdinalIgnoreCase) >= 0 ? 1.45f : 1.75f;
            return body * (npc / oldArt);
        }

        if (HasArtSprite(id, kind))
        {
            if (!string.IsNullOrEmpty(id) && (id.Contains("king") || id.Contains("boss") || id.Contains("crypt_lord")
                || id.Contains("colossus") || id.Contains("warden") || id.Contains("apex")))
            {
                if (id.Contains("king"))
                {
                    return body * (3.45f / oldArt);
                }

                if (id.Contains("ruins") || id.Contains("colossus") || id.Contains("m_boss_f5") || id.Contains("apex"))
                {
                    return body * (2.15f / oldArt);
                }

                return body * (1.75f / oldArt);
            }

            if (ContainsAny(id, "crypt") && !ContainsAny(id, "boss", "warden", "lord"))
            {
                return body * (2.05f / oldArt);
            }

            return body;
        }

        var shape = ShapeFor(id, kind);
        return body * ((shape == Shape.Cross ? 1.45f : 1.2f) / oldArt);
    }

    /// <summary>Bone-pale crypt trash; other art bodies stay white.</summary>
    public static Color MonsterTint(string id)
    {
        var s = id ?? "";
        if (ContainsAny(s, "crypt") && !ContainsAny(s, "boss", "warden", "lord"))
        {
            return new Color(0.78f, 0.82f, 0.88f, 1f);
        }

        if (ContainsAny(s, "shadow", "shade"))
        {
            return new Color(0.55f, 0.42f, 0.72f, 1f);
        }

        return Color.white;
    }

    public static bool HasArtSprite(string id, string kind)
    {
        if (IsNpcKind(id, kind) || IsPortalKind(id, kind))
        {
            return true;
        }

        var frames = GetClip(id, kind, Clip.Idle);
        return frames != null && frames.Length > 0;
    }

    /// <summary>
    /// Craftpix top-down sheets: 4 direction rows (down/left/right/up) × N frame columns.
    /// 8-dir facings map onto those 4 rows. If a row is empty, another row is used so the
    /// body still draws.
    /// </summary>
    public static Sprite[] SliceSheet(string resourcePath, int facingRow = 0)
    {
        facingRow = ToFourDirRow(facingRow);
        var frames = SliceSheetRow(resourcePath, facingRow);
        if (frames != null && frames.Length > 0)
        {
            return frames;
        }

        for (var row = 0; row < 4; row++)
        {
            if (row == facingRow)
            {
                continue;
            }

            frames = SliceSheetRow(resourcePath, row);
            if (frames != null && frames.Length > 0)
            {
                return frames;
            }
        }

        var tex = LoadTexture(resourcePath);
        return tex != null ? SliceSheetHorizontalFallback(resourcePath, tex) : null;
    }

    private static Sprite[] SliceSheetRow(string resourcePath, int facingRow)
    {
        facingRow = Mathf.Clamp(facingRow, 0, 3);
        var cacheKey = resourcePath + "@d" + facingRow;
        if (TryLiveClip(cacheKey, out var cached))
        {
            return cached;
        }

        var tex = LoadTexture(resourcePath);
        if (tex == null)
        {
            return null;
        }

        const int dirRows = 4;
        if (tex.height % dirRows != 0)
        {
            GameLog.WarnOnce(GameLog.Channel.Gfx, "hdiv:" + resourcePath,
                "reason=height_not_divisible_by_4  sheet=" + resourcePath +
                "  tex=" + tex.width + "x" + tex.height);
            return SliceSheetHorizontalFallback(resourcePath, tex);
        }

        var frameH = tex.height / dirRows;
        // Craftpix cells are square: frameW == frameH (64x64 on these sheets).
        var frameW = frameH;
        if (frameW <= 0 || tex.width % frameW != 0)
        {
            GameLog.WarnOnce(GameLog.Channel.Gfx, "wdiv:" + resourcePath,
                "reason=width_not_divisible  sheet=" + resourcePath +
                "  tex=" + tex.width + "x" + tex.height + "  frame=" + frameW + "x" + frameH +
                "  fallback=strip");
            return SliceSheetHorizontalFallback(resourcePath, tex);
        }

        var cols = tex.width / frameW;
        // Top row in file = facing 0 (down). Unity tex y=0 is bottom.
        var y = tex.height - (facingRow + 1) * frameH;

        var packed = new List<Sprite>(cols);
        for (var i = 0; i < cols; i++)
        {
            var key = cacheKey + "#" + i;
            Sprite sp;
            if (Cache.TryGetValue(key, out var existing) && existing != null)
            {
                sp = existing;
            }
            else
            {
                try
                {
                    sp = Sprite.Create(
                        tex,
                        new Rect(i * frameW, y, frameW, frameH),
                        new Vector2(0.5f, 0f),
                        frameH);
                    Cache[key] = sp;
                }
                catch (System.Exception ex)
                {
                    GameLog.WarnOnce(GameLog.Channel.Gfx, "frame:" + resourcePath + ":" + i,
                        "reason=frame_create_failed  sheet=" + resourcePath + "  frame=" + i +
                        "  err=" + ex.Message);
                    return null;
                }
            }

            if (!FrameHasVisiblePixels(tex, i * frameW, y, frameW, frameH))
            {
                continue;
            }

            packed.Add(sp);
        }

        if (packed.Count == 0)
        {
            GameLog.WarnOnce(GameLog.Channel.Gfx, "empty:" + cacheKey,
                "reason=empty_facing  sheet=" + resourcePath +
                "  tex=" + tex.width + "x" + tex.height +
                "  frame=" + frameW + "x" + frameH +
                "  facing=" + facingRow);
            return null;
        }

        var frames = packed.ToArray();
        if (frames.Length > 0)
        {
            ClipCache[cacheKey] = frames;
        }

        return frames.Length > 0 ? frames : null;
    }

    public static bool SpriteIsVisible(Sprite sprite)
    {
        if (sprite == null || sprite.texture == null)
        {
            return false;
        }

        var r = sprite.textureRect;
        return FrameHasVisiblePixels(
            sprite.texture,
            Mathf.RoundToInt(r.x),
            Mathf.RoundToInt(r.y),
            Mathf.Max(1, Mathf.RoundToInt(r.width)),
            Mathf.Max(1, Mathf.RoundToInt(r.height)));
    }

    public static bool FramesAreVisible(Sprite[] frames)
    {
        if (frames == null || frames.Length == 0)
        {
            return false;
        }

        for (var i = 0; i < frames.Length; i++)
        {
            if (SpriteIsVisible(frames[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TextureHasVisiblePixels(Texture2D tex)
    {
        if (tex == null)
        {
            return false;
        }

        return FrameHasVisiblePixels(tex, 0, 0, tex.width, tex.height);
    }

    private static bool FrameHasVisiblePixels(Texture2D tex, int x, int y, int w, int h)
    {
        try
        {
            var stepX = Mathf.Max(1, w / 32);
            var stepY = Mathf.Max(1, h / 32);
            for (var py = y; py < y + h; py += stepY)
            {
                for (var px = x; px < x + w; px += stepX)
                {
                    if (tex.GetPixel(px, py).a > 0.08f)
                    {
                        return true;
                    }
                }
            }
        }
        catch
        {
            return true;
        }

        return false;
    }

    private static Sprite[] SliceSheetHorizontalFallback(string resourcePath, Texture2D tex)
    {
        var frameH = tex.height;
        var frameW = InferFrameWidth(tex.width, frameH);
        if (frameW <= 0 || tex.width % frameW != 0)
        {
            return null;
        }

        var count = tex.width / frameW;
        var frames = new Sprite[count];
        for (var i = 0; i < count; i++)
        {
            var key = resourcePath + "|h#" + i;
            if (Cache.TryGetValue(key, out var existing) && existing != null)
            {
                frames[i] = existing;
                continue;
            }

            var sp = Sprite.Create(
                tex,
                new Rect(i * frameW, 0, frameW, frameH),
                new Vector2(0.5f, 0f),
                frameH);
            Cache[key] = sp;
            frames[i] = sp;
        }

        ClipCache[resourcePath + "|h"] = frames;
        return frames;
    }

    private static int InferFrameWidth(int texW, int texH)
    {
        if (texH > 0 && texW % texH == 0)
        {
            return texH;
        }

        if (texH > 0 && texH % 2 == 0)
        {
            var half = texH / 2;
            if (half > 0 && texW % half == 0)
            {
                return half;
            }
        }

        var a = texW;
        var b = texH;
        while (b != 0)
        {
            var t = a % b;
            a = b;
            b = t;
        }

        return a > 0 ? a : texH;
    }

    private static Sprite FullTextureSprite(string resourcePath, float pixelsPerUnit)
    {
        if (Cache.TryGetValue(resourcePath, out var cached) && cached != null)
        {
            return cached;
        }

        var tex = LoadTexture(resourcePath);
        if (tex == null)
        {
            return null;
        }

        try
        {
            var sprite = Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit);
            Cache[resourcePath] = sprite;
            return sprite;
        }
        catch (System.Exception ex)
        {
            GameLog.WarnOnce(GameLog.Channel.Gfx, "full:" + resourcePath,
                "reason=full_sprite_failed  sheet=" + resourcePath + "  err=" + ex.Message);
            return null;
        }
    }

    private static Sprite TryFileSprite(string resourcePath)
    {
        if (Cache.TryGetValue(resourcePath, out var cached) && cached != null)
        {
            return cached;
        }

        var tex = LoadFromStreamingAssets(resourcePath);
        if (tex == null)
        {
            tex = LoadFromDataPath("Resources/" + resourcePath + ".png");
        }

        if (tex == null)
        {
            return null;
        }

        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.name = Path.GetFileName(resourcePath);
        TexCache[resourcePath] = tex;

        try
        {
            var ppu = Mathf.Max(16f, Mathf.Max(tex.width, tex.height));
            var sprite = Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                ppu);
            Cache[resourcePath] = sprite;
            return sprite;
        }
        catch
        {
            return null;
        }
    }

    private static Texture2D LoadTexture(string resourcePath)
    {
        if (TexCache.TryGetValue(resourcePath, out var cached) && cached != null)
        {
            return cached;
        }

        var tex = LoadFromStreamingAssets(resourcePath);
        if (tex == null)
        {
            tex = LoadFromDataPath("Resources/" + resourcePath + ".png");
        }

        if (tex == null)
        {
            var fromRes = Resources.Load<Texture2D>(resourcePath);
            if (fromRes != null)
            {
                try
                {
                    tex = fromRes.isReadable ? fromRes : DuplicateReadable(fromRes);
                }
                catch
                {
                    tex = null;
                }
            }
        }

        if (tex == null)
        {
            GameLog.WarnOnce(GameLog.Channel.Gfx, "load:" + resourcePath,
                "reason=sheet_load_failed  sheet=" + resourcePath + "  fallback=shape");
            return null;
        }

        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.name = Path.GetFileName(resourcePath);
        TexCache[resourcePath] = tex;
        return tex;
    }

    private static Texture2D DuplicateReadable(Texture2D src)
    {
        var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(src, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var copy = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
        copy.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
        copy.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        return copy;
    }

    private static Texture2D LoadFromStreamingAssets(string resourcePath)
    {
        return LoadPngFile(ResolveSpriteFile(resourcePath));
    }

    /// <summary>Loose PNGs: Art pack first (Editor), then StreamingAssets (builds / legacy).</summary>
    private static string ResolveSpriteDir(string resourceDir)
    {
        var rel = (resourceDir ?? "").Replace('/', Path.DirectorySeparatorChar);
        if (!string.IsNullOrEmpty(Application.dataPath))
        {
            var art = Path.Combine(Application.dataPath, "_Project", "Art", rel);
            if (Directory.Exists(art))
            {
                return art;
            }
        }

        return Path.Combine(Application.streamingAssetsPath, rel);
    }

    private static string ResolveSpriteFile(string resourcePath)
    {
        var rel = (resourcePath ?? "").Replace('/', Path.DirectorySeparatorChar) + ".png";
        if (!string.IsNullOrEmpty(Application.dataPath))
        {
            var art = Path.Combine(Application.dataPath, "_Project", "Art", rel);
            if (File.Exists(art))
            {
                return art;
            }
        }

        return Path.Combine(Application.streamingAssetsPath, rel);
    }

    private static Texture2D LoadFromDataPath(string relativeUnderAssets)
    {
        var path = Path.Combine(Application.dataPath, relativeUnderAssets.Replace('/', Path.DirectorySeparatorChar));
        return LoadPngFile(path);
    }

    private static Texture2D LoadPngFile(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var bytes = File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(bytes, false))
            {
                return null;
            }

            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.alphaIsTransparency = true;
            tex.hideFlags = HideFlags.HideAndDontSave;
            StripBlackUnderAlpha(tex);
            if (!TextureHasVisiblePixels(tex))
            {
                GameLog.WarnOnce(GameLog.Channel.Gfx, "empty-tex:" + path,
                    "reason=png_fully_transparent  path=" + path);
                return null;
            }

            return tex;
        }
        catch (System.Exception ex)
        {
            GameLog.WarnOnce(GameLog.Channel.Gfx, "file:" + path,
                "reason=file_load_failed  path=" + path + "  err=" + ex.Message);
            return null;
        }
    }

    /// <summary>PNG matte (black RGB under alpha 0) shows as a halo with opaque unlit shaders.</summary>
    private static void StripBlackUnderAlpha(Texture2D tex)
    {
        if (tex == null)
        {
            return;
        }

        Color[] pixels;
        try
        {
            pixels = tex.GetPixels();
        }
        catch
        {
            return;
        }

        var dirty = false;
        for (var i = 0; i < pixels.Length; i++)
        {
            if (pixels[i].a > 0.04f)
            {
                continue;
            }

            if (pixels[i].r != 0f || pixels[i].g != 0f || pixels[i].b != 0f || pixels[i].a != 0f)
            {
                pixels[i] = Color.clear;
                dirty = true;
            }
        }

        if (dirty)
        {
            tex.SetPixels(pixels);
            tex.Apply(false, false);
        }
    }

    public static Sprite MakeShape(Shape shape, Color fill)
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color[size * size];
        var cx = (size - 1) * 0.5f;
        var cy = (size - 1) * 0.5f;
        var edge = new Color(fill.r * 0.45f, fill.g * 0.45f, fill.b * 0.45f, 1f);

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var nx = (x - cx) / cx;
                var ny = (y - cy) / cy;
                var inside = false;
                var onEdge = false;
                switch (shape)
                {
                    case Shape.Square:
                        inside = Mathf.Abs(nx) <= 0.82f && Mathf.Abs(ny) <= 0.82f;
                        onEdge = inside && (Mathf.Abs(nx) > 0.68f || Mathf.Abs(ny) > 0.68f);
                        break;
                    case Shape.Diamond:
                        inside = Mathf.Abs(nx) + Mathf.Abs(ny) <= 0.95f;
                        onEdge = inside && Mathf.Abs(nx) + Mathf.Abs(ny) > 0.72f;
                        break;
                    case Shape.Circle:
                        var d = Mathf.Sqrt(nx * nx + ny * ny);
                        inside = d <= 0.92f;
                        onEdge = inside && d > 0.72f;
                        break;
                    case Shape.Hex:
                    {
                        var ax = Mathf.Abs(nx);
                        var ay = Mathf.Abs(ny);
                        inside = ay <= 0.86f && ax * 0.866f + ay * 0.5f <= 0.86f;
                        onEdge = inside && (ay > 0.68f || ax * 0.866f + ay * 0.5f > 0.68f);
                        break;
                    }
                    case Shape.Cross:
                        inside = (Mathf.Abs(nx) <= 0.28f && Mathf.Abs(ny) <= 0.9f) ||
                                 (Mathf.Abs(ny) <= 0.28f && Mathf.Abs(nx) <= 0.9f);
                        onEdge = inside && (
                            (Mathf.Abs(nx) > 0.18f && Mathf.Abs(nx) <= 0.28f) ||
                            (Mathf.Abs(ny) > 0.18f && Mathf.Abs(ny) <= 0.28f) ||
                            Mathf.Abs(nx) > 0.78f || Mathf.Abs(ny) > 0.78f);
                        break;
                }

                pixels[y * size + x] = !inside
                    ? Color.clear
                    : (onEdge ? edge : fill);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0f), size);
    }

    public static string WeaponCategory(string id)
    {
        if (string.IsNullOrEmpty(id) || id == "none")
        {
            return "";
        }

        if (id.IndexOf("bow", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "bow";
        }

        if (id.IndexOf("gun", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "gun";
        }

        if (id.IndexOf("staff", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "staff";
        }

        if (id.IndexOf("dagger", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "dagger";
        }

        return "sword";
    }

    public static Sprite WeaponMark(string weaponId)
    {
        var cat = WeaponCategory(weaponId);
        var key = "wpnart:" + cat;
        if (Cache.TryGetValue(key, out var cached) && cached != null)
        {
            return cached;
        }

        var sprite = MakePixelWeapon(cat);
        Cache[key] = sprite;
        return sprite;
    }

    private static Sprite MakePixelWeapon(string cat)
    {
        string[] rows;
        switch (cat)
        {
            case "bow":
                rows = new[]
                {
                    "........WW......",
                    ".......W..W.....",
                    "......W....T....",
                    ".....W.....T....",
                    "....W......T....",
                    "...W.......T....",
                    "..W........T....",
                    ".WWWWWWWWWWW....",
                    "..W........T....",
                    "...W.......T....",
                    "....W......T....",
                    ".....W.....T....",
                    "......W....T....",
                    ".......W..W.....",
                    "........WW......",
                    "................",
                };
                break;
            case "staff":
                rows = new[]
                {
                    ".......OOO......",
                    "......OGGGO.....",
                    ".......OOO......",
                    "........W.......",
                    "........W.......",
                    "........W.......",
                    "........W.......",
                    "........W.......",
                    "........W.......",
                    "........W.......",
                    "........W.......",
                    "........W.......",
                    "........W.......",
                    ".......WWW......",
                    "................",
                    "................",
                };
                break;
            case "gun":
                rows = new[]
                {
                    "................",
                    "................",
                    "....SSSSSSSS....",
                    "....S......S....",
                    "HHHHSSSSSSSS....",
                    "H..H............",
                    "H..H............",
                    ".HH.............",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                };
                break;
            case "dagger":
                rows = new[]
                {
                    "................",
                    ".........S......",
                    "........SS......",
                    ".......SS.......",
                    "......SS........",
                    ".....SS.........",
                    "....GGG.........",
                    ".....H..........",
                    ".....H..........",
                    ".....P..........",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                };
                break;
            default:
                rows = new[]
                {
                    ".............S..",
                    "............SS..",
                    "...........SS...",
                    "..........SS....",
                    ".........SS.....",
                    "........SS......",
                    ".......SS.......",
                    "......SS........",
                    ".....GGGGGGG....",
                    "......HH........",
                    "......HH........",
                    "......HH........",
                    "......PP........",
                    "................",
                    "................",
                    "................",
                };
                break;
        }

        return SpriteFromCharMap(rows);
    }

    public static Sprite SkillIcon(string skillId)
    {
        var fileId = string.IsNullOrEmpty(skillId) || skillId == "aa" ? "auto_attack" : skillId;
        var fromFile = TryFileSprite("Icons/skill_" + fileId);
        if (fromFile != null)
        {
            return fromFile;
        }

        var key = "skico:" + (skillId ?? "");
        if (Cache.TryGetValue(key, out var cached) && cached != null)
        {
            return cached;
        }

        var sprite = SpriteFromCharMap(SkillIconRows(skillId), 16f, 0.5f, 0.5f);
        Cache[key] = sprite;
        return sprite;
    }

    public static Sprite ItemIcon(string itemId)
    {
        if (string.IsNullOrEmpty(itemId))
        {
            return MakeShape(Shape.Square, new Color(0.15f, 0.15f, 0.18f, 1f));
        }

        var cat = WeaponCategory(itemId);
        if (!string.IsNullOrEmpty(cat) &&
            (itemId.IndexOf("sword", System.StringComparison.OrdinalIgnoreCase) >= 0
             || itemId.IndexOf("bow", System.StringComparison.OrdinalIgnoreCase) >= 0
             || itemId.IndexOf("staff", System.StringComparison.OrdinalIgnoreCase) >= 0
             || itemId.IndexOf("gun", System.StringComparison.OrdinalIgnoreCase) >= 0
             || itemId.IndexOf("dagger", System.StringComparison.OrdinalIgnoreCase) >= 0
             || itemId.StartsWith("wep_") || itemId.Contains("weapon")))
        {
            var weaponFile = TryFileSprite("Icons/item_" + cat);
            return weaponFile != null ? weaponFile : WeaponMark(itemId);
        }

        var role = ItemIconRole(itemId);
        var file = TryFileSprite("Icons/item_" + role);
        if (file != null)
        {
            return file;
        }

        var key = "itico:" + role;
        if (Cache.TryGetValue(key, out var cached) && cached != null)
        {
            return cached;
        }

        var sprite = SpriteFromCharMap(ItemIconRows(role), 16f, 0.5f, 0.5f);
        Cache[key] = sprite;
        return sprite;
    }

    public static void DrawGui(Rect rect, Sprite sprite, Color tint)
    {
        if (sprite == null || sprite.texture == null)
        {
            return;
        }

        var tex = sprite.texture;
        var tr = sprite.textureRect;
        var uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
        GUI.color = tint;
        GUI.DrawTextureWithTexCoords(rect, tex, uv);
    }

    private static string PropRole(string id, string kind)
    {
        if (IsPortalKind(id, kind))
        {
            return "portal";
        }

        var s = id ?? "";
        if (s.IndexOf("homestone", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "stone";
        }

        if (s.IndexOf("weapon", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            s.IndexOf("smith", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "smith";
        }

        if (s.IndexOf("cook", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "cook";
        }

        if (s.IndexOf("skill", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            s.IndexOf("trainer", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "trainer";
        }

        if (s.IndexOf("guard", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "guard";
        }

        if (s.IndexOf("auction", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "auction";
        }

        if (s.IndexOf("tower", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            s.IndexOf("card", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            s.IndexOf("broker", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "broker";
        }

        if (s.IndexOf("switch", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "switch";
        }

        if (s.IndexOf("quest", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            s.IndexOf("mira", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            s.IndexOf("kael", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            s.IndexOf("sil", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "quest";
        }

        if (s.IndexOf("item", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            s.IndexOf("vendor", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "vendor";
        }

        return "villager";
    }

    private static Sprite PixelProp(string role)
    {
        var key = "prop:" + role;
        if (Cache.TryGetValue(key, out var cached) && cached != null)
        {
            return cached;
        }

        var sprite = SpriteFromCharMap(PropRows(role), 16f, 0.5f, 0.12f);
        Cache[key] = sprite;
        return sprite;
    }

    private static string[] PropRows(string role)
    {
        switch (role)
        {
            case "portal":
                return new[]
                {
                    "..OOOOOOOOOO....",
                    ".O..........O...",
                    "O..LLLLLLLL..O..",
                    "O..L......L..O..",
                    "O..L.LLLL.L..O..",
                    "O..L.L..L.L..O..",
                    "O..L.L..L.L..O..",
                    "O..L.LLLL.L..O..",
                    "O..L......L..O..",
                    "O..LLLLLLLL..O..",
                    ".O..........O...",
                    "..OOOOOOOOOO....",
                    "....HHHHHH......",
                    "....H....H......",
                    "....HHHHHH......",
                    "................",
                };
            case "portal_active":
                return new[]
                {
                    "..OOOOOOOOOO....",
                    ".O.CCCCCCCC.O...",
                    "O.CLLLLLLLLC.O..",
                    "O.CL.CCCC.LC.O..",
                    "O.CL.CWWC.LC.O..",
                    "O.CL.CWWC.LC.O..",
                    "O.CL.CWWC.LC.O..",
                    "O.CL.CCCC.LC.O..",
                    "O.CL......LC.O..",
                    "O.CLLLLLLLLC.O..",
                    ".O.CCCCCCCC.O...",
                    "..OOOOOOOOOO....",
                    "....HHHHHH......",
                    "....HYYYYH......",
                    "....HHHHHH......",
                    "................",
                };
            case "stall":
                return new[]
                {
                    "................",
                    "...RRRRRRRRR....",
                    "..RRYYYYYYRRR...",
                    ".RRYYYYYYYYYRR..",
                    "HHHHHHHHHHHHHHHH",
                    "H..............H",
                    "H..OOOO..OOOO..H",
                    "H..O..O..O..O..H",
                    "H..OOOO..OOOO..H",
                    "H..............H",
                    "HHHHHHHHHHHHHHHH",
                    "...WWWWWWWWWW...",
                    "...W........W...",
                    "...WWWWWWWWWW...",
                    "................",
                    "................",
                };
            case "fountain":
                return new[]
                {
                    "................",
                    ".....CCCCCC.....",
                    "....CWWWWWWC....",
                    "...CWWBWWWBC....",
                    "...CWBWWWWBC....",
                    "....CWWWWWC.....",
                    ".....SSSSSS.....",
                    "....SSLLLLSS....",
                    "...SSLLLLLLSS...",
                    "...SLLLLLLLLS...",
                    "...SSLLLLLLSS...",
                    "....SSSSSSSS....",
                    "....HHHHHHHH....",
                    "...HH......HH...",
                    "...HHHHHHHHHH...",
                    "................",
                };
            case "rock":
                return new[]
                {
                    "................",
                    "................",
                    "......SS........",
                    ".....SSSS.......",
                    "....SSSSSS......",
                    "...SSHHHSSS.....",
                    "...SHHHHHHS.....",
                    "..SSHHHHHHSS....",
                    "..SHHHHHHHHS....",
                    "..SSHHHHHHSS....",
                    "...DSSHHSSD.....",
                    "...DDSSSSDD.....",
                    "....DDDDDD......",
                    "................",
                    "................",
                    "................",
                };
            case "gate":
                return new[]
                {
                    "................",
                    "..OOOO....OOOO..",
                    "..O..O....O..O..",
                    "..O..OOOOOO..O..",
                    "..O..OYYYYO..O..",
                    "..O..OYYYYO..O..",
                    "..OOOOYYYYOOOO..",
                    ".....OYYYYO.....",
                    ".....OYYYYO.....",
                    ".....OYYYYO.....",
                    ".....OOOOOO.....",
                    ".....HHHHHH.....",
                    "....HH....HH....",
                    "....HHHHHHHH....",
                    "................",
                    "................",
                };
            case "crate":
                return new[]
                {
                    "................",
                    "................",
                    "................",
                    "....OOOOOOOO....",
                    "....OYYYYYYO....",
                    "....OYYYYYYO....",
                    "....OYYYYYYO....",
                    "....OOOOOOOO....",
                    "....OHHHHHHO....",
                    "....OHHHHHHO....",
                    "....OOOOOOOO....",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                };
            case "pillar":
                return new[]
                {
                    "......OOOO......",
                    ".....OSSSSO.....",
                    ".....OSHHSO.....",
                    ".....OSSSSO.....",
                    "......OOOO......",
                    "......HHHH......",
                    "......HHHH......",
                    "......HHHH......",
                    "......HHHH......",
                    "......HHHH......",
                    "......HHHH......",
                    ".....HHHHHH.....",
                    "....HHHHHHHH....",
                    "...HH......HH...",
                    "...HHHHHHHHHH...",
                    "................",
                };
            case "brazier":
                return new[]
                {
                    "................",
                    "......YY........",
                    ".....YRRRY......",
                    "....YRRRRRY.....",
                    ".....ORRRO......",
                    "......OOOO......",
                    "......HHHH......",
                    ".....HHHHHH.....",
                    "....HH....HH....",
                    "....HHHHHHHH....",
                    ".....HHHHHH.....",
                    "......HHHH......",
                    "................",
                    "................",
                    "................",
                    "................",
                };
            case "bones":
                return new[]
                {
                    "................",
                    "................",
                    "................",
                    "....WW..WW......",
                    "...WWWWWWWW.....",
                    "...W.WWWW.W.....",
                    "....WWWWWW......",
                    ".....HHHH.......",
                    "....WW..WW......",
                    "...WW....WW.....",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                };
            case "chest":
                return new[]
                {
                    "................",
                    "................",
                    "................",
                    "....YYYYYYYY....",
                    "...YOOOOOOOOY...",
                    "...YOYYYYYYOY...",
                    "...YOOOOOOOOY...",
                    "....YYYYYYYY....",
                    "....YHHHHHHY....",
                    "....YHHHHHHY....",
                    "....YYYYYYYY....",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                };
            case "stone":
                return new[]
                {
                    "................",
                    "......SS........",
                    ".....SSSS.......",
                    "....SLLLLS......",
                    "...SSLLLLSS.....",
                    "...S.LLLL.S.....",
                    "...SSLLLLSS.....",
                    "....SLLLLS......",
                    ".....DSSSD......",
                    "....DDDDDD......",
                    "...DDHHHHDD.....",
                    "..DDHHHHHHDD....",
                    ".DDHHHHHHHHDD...",
                    "DDHHHHHHHHHHDD..",
                    "................",
                    "................",
                };
            case "switch":
                return new[]
                {
                    "................",
                    "......RR........",
                    ".....RRRR.......",
                    "......HH........",
                    "......HH........",
                    "......HH........",
                    "....AAAAAA......",
                    "...AAAAAAAA.....",
                    "...AAHHHHAA.....",
                    "...AAAAAAAA.....",
                    "....AAAAAA......",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                };
            default:
                return PersonRows(role);
        }
    }

    private static string[] PersonRows(string role)
    {
        char cloak = role switch
        {
            "smith" => 'R',
            "cook" => 'Y',
            "trainer" => 'O',
            "guard" => 'A',
            "auction" => 'G',
            "broker" => 'O',
            "quest" => 'E',
            "vendor" => 'B',
            _ => 'C',
        };
        var c = cloak.ToString();
        return new[]
        {
            "......HHHH......",
            ".....HKKKH......",
            ".....KSKSK......",
            "......KKK.......",
            ".....KKKKK......",
            "...." + c + c + c + c + c + "......",
            "...." + c + "." + c + "." + c + "......",
            "...." + c + c + c + c + c + "......",
            ".....HHHHH......",
            ".....H...H......",
            ".....H...H......",
            ".....H...H......",
            "....HH...HH.....",
            "................",
            "................",
            "................",
        };
    }

    private static string[] SkillIconRows(string id)
    {
        switch (id)
        {
            case "shot":
                return new[]
                {
                    "................",
                    "..............T.",
                    ".............TT.",
                    "............T...",
                    ".........WWWT...",
                    "........W...T...",
                    ".......W....T...",
                    "......W.........",
                    ".....W..........",
                    "....W...........",
                    "...W............",
                    "..W.............",
                    "................",
                    "................",
                    "................",
                    "................",
                };
            case "shockwave":
                return new[]
                {
                    "................",
                    ".....LLLLLL.....",
                    "...LL......LL...",
                    "..L..........L..",
                    ".L....LLLL....L.",
                    ".L...L....L...L.",
                    ".L...L.RR.L...L.",
                    ".L...L.RR.L...L.",
                    ".L...L....L...L.",
                    ".L....LLLL....L.",
                    "..L..........L..",
                    "...LL......LL...",
                    ".....LLLLLL.....",
                    "................",
                    "................",
                    "................",
                };
            case "dash":
                return new[]
                {
                    "................",
                    "................",
                    "....EE..........",
                    "...EEEE.........",
                    "..EEEEEE........",
                    ".EEEEEEEE.......",
                    "EEEEEEEEEE......",
                    ".EEEEEEEE.......",
                    "..EEEEEE........",
                    "...EEEE.........",
                    "....EE..........",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                };
            case "rally":
                return new[]
                {
                    "................",
                    ".H.RRRRRRR......",
                    ".H.RRRRRRR......",
                    ".H.RRRRRRR......",
                    ".H.RR...........",
                    ".H..............",
                    ".H..............",
                    ".H..............",
                    ".H..............",
                    ".H..............",
                    ".H..............",
                    ".HHHHHH.........",
                    "................",
                    "................",
                    "................",
                    "................",
                };
            case "hook_shot":
                return new[]
                {
                    "................",
                    ".........SSS....",
                    "........S...S...",
                    ".......S.....S..",
                    "......S......S..",
                    ".....S.......S..",
                    "....S...........",
                    "...S............",
                    "..S.............",
                    ".S..............",
                    "S...............",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                };
            case "mend":
                return new[]
                {
                    "................",
                    "......WWWW......",
                    "......WLLW......",
                    "......WLLW......",
                    "..WWWWWLLWWWWW..",
                    "..WLLLLLLLLLLW..",
                    "..WLLLLLLLLLLW..",
                    "..WWWWWLLWWWWW..",
                    "......WLLW......",
                    "......WLLW......",
                    "......WWWW......",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                };
            case "decoy":
                return new[]
                {
                    "................",
                    "......AAAA......",
                    ".....A....A.....",
                    "....A......A....",
                    "...A...LL...A...",
                    "...A...LL...A...",
                    "...A........A...",
                    "....A......A....",
                    ".....A....A.....",
                    "......AAAA......",
                    ".......HH.......",
                    ".......HH.......",
                    "................",
                    "................",
                    "................",
                    "................",
                };
            case "slash":
                return new[]
                {
                    "................",
                    "...........WW...",
                    "..........WW....",
                    ".........WW.....",
                    "........WW......",
                    ".......WW.......",
                    "......WW........",
                    ".....WWS........",
                    "....WWSS........",
                    "...WW...........",
                    "..WW............",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                };
            case "war_cry":
                return new[]
                {
                    "................",
                    "......RRRR......",
                    ".....RRRRRR.....",
                    "....RR....RR....",
                    "...RR.HHHH.RR...",
                    "...RR.H..H.RR...",
                    "....RRHHHHRR....",
                    ".....RRRRRR.....",
                    "......R..R......",
                    ".....R....R.....",
                    "....R......R....",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                };
            case "shove":
                return new[]
                {
                    "................",
                    "......YYYY......",
                    ".....YYYYYY.....",
                    "....YYYYYYYY....",
                    "....YYHHHHYY....",
                    "....YYYYYYYY....",
                    ".....YYYYYY.....",
                    "......YYYY......",
                    ".....WWWWWW.....",
                    "....WW....WW....",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                };
            case "iron_stance":
            case "barrier":
                return new[]
                {
                    "................",
                    ".....AAAAAA.....",
                    "....AAHHHHAA....",
                    "...AAH....HAA...",
                    "...AH......HA...",
                    "...AH......HA...",
                    "...AAH....HAA...",
                    "....AAHHHHAA....",
                    ".....AAAAAA.....",
                    "......HHHH......",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                };
            case "stun_bolt":
                return new[]
                {
                    "................",
                    "........YY......",
                    ".......YY.......",
                    "......YYYY......",
                    ".....YY.........",
                    "....YYYYYY......",
                    "......YY........",
                    ".....YY.........",
                    "....YY..........",
                    "...YY...........",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                };
            case "ember_dot":
                return new[]
                {
                    "................",
                    "......YY........",
                    ".....YRRRY......",
                    "....YRRRRRY.....",
                    "....RRRRRRR.....",
                    ".....RRORR......",
                    "......RRR.......",
                    "......OOO.......",
                    ".....HHHHH......",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                };
            case "blind_dust":
                return new[]
                {
                    "................",
                    "....SS..SS......",
                    "...SSSSSSSS.....",
                    "..SS......SS....",
                    "..S........S....",
                    "...SS....SS.....",
                    "....SSSSSS......",
                    ".....SSSS.......",
                    "......HH........",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                };
            case "power_chant":
            case "group_chant":
                return new[]
                {
                    "................",
                    "....EEEEEEEE....",
                    "...EE......EE...",
                    "...EE.LLLL.EE...",
                    "...EE.L..L.EE...",
                    "...EE.LLLL.EE...",
                    "...EE......EE...",
                    "...EEEEEEEEEE...",
                    "....HHHHHHHH....",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                };
            case "ward":
            case "elemental_focus":
                return new[]
                {
                    "................",
                    "......LL........",
                    ".....LLLL.......",
                    "....LLWWLL......",
                    "...LLW..WLL.....",
                    "....LLWWLL......",
                    ".....LLLL.......",
                    "......LL........",
                    ".....HHHH.......",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                };
            case "pull":
                return new[]
                {
                    "................",
                    "..SS............",
                    ".S..S...........",
                    "S....S..........",
                    "S.....WWWWWW....",
                    "S....S..........",
                    ".S..S...........",
                    "..SS............",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                };
            case "haste":
                return new[]
                {
                    "................",
                    "....EE....EE....",
                    "...EEEE..EEEE...",
                    "..EE..EEEE..EE..",
                    "......HHHH......",
                    ".....HH..HH.....",
                    ".....HH..HH.....",
                    "....HHHHHHHH....",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                };
            case "ally_mend":
            case "cleanse_light":
                return SkillIconRows("mend");
            case "target_burst":
                return new[]
                {
                    "................",
                    "......RR........",
                    ".....RRRR.......",
                    "..R.RRRRRR.R....",
                    "...RRRWWWRRR....",
                    "....RRWWWRR.....",
                    "...RRRWWWRRR....",
                    "..R.RRRRRR.R....",
                    ".....RRRR.......",
                    "......RR........",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                };
            default:
                return new[]
                {
                    ".............S..",
                    "............SS..",
                    "...........SS...",
                    "..........SS....",
                    ".........SS.....",
                    "........SS......",
                    ".......SS.......",
                    "......SS........",
                    ".....GGGGGGG....",
                    "......HH........",
                    "......HH........",
                    "......HH........",
                    "......PP........",
                    "................",
                    "................",
                    "................",
                };
        }
    }

    private static string ItemIconRole(string id)
    {
        if (id.IndexOf("gold", System.StringComparison.OrdinalIgnoreCase) >= 0
            || id.IndexOf("coin", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "gold";
        }

        if (id.StartsWith("spirit_")) return "spirit";
        if (id.StartsWith("card_") || id.StartsWith("char_")) return "card";
        if (id.Contains("ticket")) return "ticket";
        if (id.Contains("dust")) return "dust";
        if (id.Contains("stew") || id.Contains("ration") || id.Contains("bread") || id.Contains("food")) return "food";
        if (id.Contains("helm") || id.Contains("hat")) return "helm";
        if (id.Contains("boot")) return "boots";
        if (id.Contains("glove")) return "gloves";
        if (id.Contains("armor") || id.Contains("mail") || id.Contains("leather") || id.Contains("plate")) return "armor";
        if (id.Contains("homestone") || id.Contains("stone")) return "stone";
        if (id.Contains("acc") || id.Contains("ring") || id.Contains("amulet")) return "acc";
        return "item";
    }

    private static string[] ItemIconRows(string role)
    {
        switch (role)
        {
            case "food":
                return new[]
                {
                    "................",
                    "....HHHHHHHH....",
                    "...HYYYYYYYYH...",
                    "...HYYRRRRYYH...",
                    "...HYYYYYYYYH...",
                    "....HHHHHHHH....",
                    ".....WWWWWW.....",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                };
            case "dust":
                return new[]
                {
                    "................",
                    "......LL........",
                    ".....LLLL.......",
                    "...L.LLLL.L.....",
                    "....LLLLLL......",
                    "...LLLLLLLL.....",
                    "....OLOLOL......",
                    ".....OOOO.......",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                };
            case "spirit":
                return new[]
                {
                    "................",
                    "......OOO.......",
                    ".....OGGGO......",
                    "....OGLLLGO.....",
                    "....OGLLLGO.....",
                    ".....OGGGO......",
                    "......OOO.......",
                    ".......L........",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                };
            case "card":
                return new[]
                {
                    "................",
                    "....GGGGGG......",
                    "...G......G.....",
                    "...G.OOOO.G.....",
                    "...G.O..O.G.....",
                    "...G.OOOO.G.....",
                    "...G......G.....",
                    "....GGGGGG......",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                };
            case "helm":
                return new[]
                {
                    "................",
                    ".....AAAAAA.....",
                    "....AAAAAAAA....",
                    "...AA......AA...",
                    "...AA.KKKK.AA...",
                    "...AA.K..K.AA...",
                    "....AAAAAAAA....",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                };
            case "boots":
                return new[]
                {
                    "................",
                    "..HH......HH....",
                    "..HH......HH....",
                    "..HH......HH....",
                    ".HHHH....HHHH...",
                    ".HHHH....HHHH...",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                };
            case "gloves":
                return new[]
                {
                    "................",
                    "...KKK...KKK....",
                    "...K.K...K.K....",
                    "...KKK...KKK....",
                    "...KKK...KKK....",
                    "...HHH...HHH....",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                };
            case "armor":
                return new[]
                {
                    "................",
                    "....A......A....",
                    "...AAAAAAAAAA...",
                    "...AA.AAAA.AA...",
                    "...AAAAAAAAAA...",
                    "...AA......AA...",
                    "...AAAAAAAAAA...",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                };
            case "acc":
                return new[]
                {
                    "................",
                    "......GG........",
                    ".....GLLG.......",
                    "......GG........",
                    "......HH........",
                    ".....HHHH.......",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                };
            default:
                return new[]
                {
                    "................",
                    "....WWWWWW......",
                    "...W......W.....",
                    "...W.LLLL.W.....",
                    "...W......W.....",
                    "....WWWWWW......",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                    "................",
                };
        }
    }

    private static bool IsSkeletonId(string id)
    {
        var s = id ?? "";
        return ContainsAny(s, "crypt_skel", "skel") ||
            (ContainsAny(s, "crypt") && !ContainsAny(s, "boss", "warden", "lord"));
    }

    private static Sprite[] PixelSkeletonClip(Clip clip)
    {
        var key = "skelclip:" + clip;
        if (TryLiveClip(key, out var cached))
        {
            return cached;
        }

        var one = new[] { SpriteFromCharMap(SkeletonRows(clip), 16f, 0.5f, 0.12f) };
        ClipCache[key] = one;
        return one;
    }

    private static Sprite[] PixelBowAttack()
    {
        const string key = "bowatk";
        if (TryLiveClip(key, out var cached))
        {
            return cached;
        }

        var one = new[] { SpriteFromCharMap(BowAttackRows(), 16f, 0.5f, 0.12f) };
        ClipCache[key] = one;
        return one;
    }

    private static string LoadBiome(string mapId)
    {
        var id = mapId ?? "";
        if (id.Contains("marsh")) return "marsh";
        if (id.StartsWith("town")) return "town";
        if (id.StartsWith("dungeon") || id.StartsWith("tower") || id.Contains("crypt") || id.Contains("ruins"))
        {
            return "dungeon";
        }

        if (id.StartsWith("field")) return "field";
        return "default";
    }

    private static string PortraitRole(string id)
    {
        var s = (id ?? "").ToLowerInvariant();
        if (s.Contains("aurel")) return "aurel";
        if (s.Contains("nyla")) return "nyla";
        if (s.Contains("fighter") || s.Contains("fgt")) return "fighter";
        if (s.Contains("mage")) return "mage";
        if (s.Contains("marksman") || s.Contains("archer")) return "marksman";
        if (s.Contains("rogue")) return "rogue";
        return "adventurer";
    }

    private static string[] LoadArtRows(string biome)
    {
        switch (biome)
        {
            case "town":
                return new[]
                {
                    "SSSSSSSSSSSSSSSSSSSSSSSS",
                    "SS....HHHH....HHHH....SS",
                    "SS....H..H....H..H....SS",
                    "SS....HHHH....HHHH....SS",
                    "SS....................SS",
                    "SS..WWWW....WWWW......SS",
                    "SS..W..W....W..W......SS",
                    "SSWWWWWWWWWWWWWWWWWWWWSS",
                    "DDDDDDDDDDDDDDDDDDDDDDDD",
                    "DD....OOOO....OOOO....DD",
                    "HHHHHHHHHHHHHHHHHHHHHHHH",
                    "HHHH....GG....GG....HHHH",
                    "HHHHHHHHHHHHHHHHHHHHHHHH",
                    "........................",
                    "........................",
                    "........................",
                };
            case "field":
                return new[]
                {
                    "AAAAAAAAAAAAAAAAAAAAAAAA",
                    "AA..GGGG....GGGG....AAAA",
                    "AA..G..G....G..G....AAAA",
                    "AAGGGGGGGGGGGGGGGGGGAAAA",
                    "GGGGGGGGGGGGGGGGGGGGGGGG",
                    "GG....SSSS....SSSS....GG",
                    "GG....S..S....S..S....GG",
                    "GGGGGGSSSSGGGGSSSSGGGGGG",
                    "GGGGGGGGGGGGGGGGGGGGGGGG",
                    "....GG....HHHH....GG....",
                    "HHHHHHHHHHHHHHHHHHHHHHHH",
                    "HHDDDDDDDDDDDDDDDDDDHHHH",
                    "HHHHHHHHHHHHHHHHHHHHHHHH",
                    "........................",
                    "........................",
                    "........................",
                };
            case "marsh":
                return new[]
                {
                    "AAAAAAAAAAAAAAAAAAAAAAAA",
                    "AA....LLLL....LLLL....AA",
                    "AA....L..L....L..L....AA",
                    "AALLLLLLLLLLLLLLLLLLAAAA",
                    "LLLLLLLLLLLLLLLLLLLLLLLL",
                    "LL....BBBB....BBBB....LL",
                    "LL....B..B....B..B....LL",
                    "LLLLLLBBBBLLLLBBBBLLLLLL",
                    "LLLLLLLLLLLLLLLLLLLLLLLL",
                    "DDDDLLLLDDDDLLLLDDDDLLLL",
                    "HHHHHHHHHHHHHHHHHHHHHHHH",
                    "HH....GGGG....GGGG....HH",
                    "HHHHHHHHHHHHHHHHHHHHHHHH",
                    "........................",
                    "........................",
                    "........................",
                };
            case "dungeon":
                return new[]
                {
                    "HHHHHHHHHHHHHHHHHHHHHHHH",
                    "HH....SSSS....SSSS....HH",
                    "HH....S..S....S..S....HH",
                    "HHSSSSSSSSSSSSSSSSSSHHHH",
                    "SSSSSSSSSSSSSSSSSSSSSSSS",
                    "SS....RRRR....RRRR....SS",
                    "SS....R..R....R..R....SS",
                    "SSRRRRRRRRRRRRRRRRRRSSSS",
                    "SSSSSSSSSSSSSSSSSSSSSSSS",
                    "DDDDSSSSDDDDSSSSDDDDSSSS",
                    "HHHHHHHHHHHHHHHHHHHHHHHH",
                    "HH....LLLL....LLLL....HH",
                    "HHHHHHHHHHHHHHHHHHHHHHHH",
                    "........................",
                    "........................",
                    "........................",
                };
            default:
                return new[]
                {
                    "AAAAAAAAAAAAAAAAAAAAAAAA",
                    "AA....DDDD....DDDD....AA",
                    "AA....D..D....D..D....AA",
                    "AADDDDDDDDDDDDDDDDDDAAAA",
                    "DDDDDDDDDDDDDDDDDDDDDDDD",
                    "HHHHHHHHHHHHHHHHHHHHHHHH",
                    "HH....WWWW....WWWW....HH",
                    "HHHHHHHHHHHHHHHHHHHHHHHH",
                    "........................",
                    "........................",
                    "........................",
                    "........................",
                    "........................",
                    "........................",
                    "........................",
                    "........................",
                };
        }
    }

    private static string[] PortraitRows(string role)
    {
        switch (role)
        {
            case "fighter":
                return FaceRows('R', 'S');
            case "mage":
                return FaceRows('L', 'A');
            case "marksman":
                return FaceRows('G', 'H');
            case "rogue":
                return FaceRows('D', 'P');
            case "aurel":
                return FaceRows('O', 'Y');
            case "nyla":
                return FaceRows('P', 'L');
            default:
                return FaceRows('T', 'W');
        }
    }

    private static string[] FaceRows(char hair, char cloth)
    {
        var h = hair.ToString();
        var c = cloth.ToString();
        return new[]
        {
            "................",
            "...." + h + h + h + h + h + h + h + h + "....",
            "..." + h + "WWWWWW" + h + "...",
            "..." + h + "W.WW.W" + h + "...",
            "..." + h + "WWWWWW" + h + "...",
            "...." + h + "WWWWWW" + h + "....",
            "....." + c + c + c + c + c + c + ".....",
            "...." + c + c + "HH" + c + c + "....",
            "...." + c + c + c + c + c + c + "....",
            ".....HHHHHH......",
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
        };
    }

    private static string[] BannerRows()
    {
        return new[]
        {
            "OOOOOOOOOOOOOOOOOOOOOOOO",
            "OYYYYYYYYYYYYYYYYYYYYYYO",
            "OY....HHHH....HHHH....YO",
            "OY....HGGH....HLLH....YO",
            "OY....HHHH....HHHH....YO",
            "OY....................YO",
            "OY..RRRR....WWWW......YO",
            "OY..RSSR....WLLW......YO",
            "OY..RRRR....WWWW......YO",
            "OYYYYYYYYYYYYYYYYYYYYYYO",
            "OOOOOOOOOOOOOOOOOOOOOOOO",
            "HHHHHHHHHHHHHHHHHHHHHHHH",
            "........................",
            "........................",
            "........................",
            "........................",
        };
    }

    private static string[] ClassCardRows(string classId)
    {
        return PortraitRows(PortraitRole(classId));
    }

    private static string[] SkeletonRows(Clip clip)
    {
        if (clip == Clip.Death)
        {
            return new[]
            {
                "................",
                "................",
                "................",
                "................",
                "................",
                "....WW..WW......",
                "...W.WW.W.......",
                "..WWWWWWWW......",
                "....HHHH........",
                "...H....H.......",
                "..HHHHHHHH......",
                "................",
                "................",
                "................",
                "................",
                "................",
            };
        }

        if (clip == Clip.Attack || clip == Clip.WalkAttack || clip == Clip.RunAttack)
        {
            return new[]
            {
                "................",
                "......WW........",
                ".....W.W........",
                "......W.........",
                ".....WWW...SS...",
                "......H.....S...",
                ".....HHH..SS....",
                "....H.H.H.......",
                "......H.........",
                ".....H.H........",
                "....H...H.......",
                "...H.....H......",
                "................",
                "................",
                "................",
                "................",
            };
        }

        return new[]
        {
            "................",
            "......WW........",
            ".....W.W........",
            "......W.........",
            ".....WWW........",
            "......H.........",
            ".....HHH........",
            "....H.H.H.......",
            "......H.........",
            ".....H.H........",
            "....H...H.......",
            "...H.....H......",
            "................",
            "................",
            "................",
            "................",
        };
    }

    private static string[] BowAttackRows()
    {
        return new[]
        {
            "................",
            "......TTT.......",
            ".....TWWWT......",
            ".....T.W.T......",
            ".....TWWWT......",
            "......THT.S.....",
            ".....HHHH.S.....",
            "....H.H.HSS.....",
            "......H...S.....",
            ".....H.H........",
            "....H...H.......",
            "...WW...WW......",
            "................",
            "................",
            "................",
            "................",
        };
    }

    private static Sprite SpriteFromCharMap(string[] rows)
    {
        return SpriteFromCharMap(rows, 16f, 0.35f, 0.2f);
    }

    private static Sprite SpriteFromCharMap(string[] rows, float ppu, float pivotX, float pivotY)
    {
        var h = rows.Length;
        var w = rows[0].Length;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var pixels = new Color[w * h];
        for (var y = 0; y < h; y++)
        {
            var row = rows[h - 1 - y];
            for (var x = 0; x < w; x++)
            {
                pixels[y * w + x] = CharPixel(x < row.Length ? row[x] : '.');
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(pivotX, pivotY), ppu);
    }

    private static Color CharPixel(char c)
    {
        switch (c)
        {
            case 'S': return new Color(0.82f, 0.86f, 0.92f, 1f);
            case 'D': return new Color(0.5f, 0.54f, 0.62f, 1f);
            case 'G': return new Color(0.82f, 0.68f, 0.22f, 1f);
            case 'H': return new Color(0.42f, 0.26f, 0.12f, 1f);
            case 'P': return new Color(0.7f, 0.55f, 0.2f, 1f);
            case 'W': return new Color(0.45f, 0.28f, 0.12f, 1f);
            case 'T': return new Color(0.92f, 0.9f, 0.82f, 1f);
            case 'O': return new Color(0.55f, 0.35f, 0.85f, 1f);
            case 'K': return new Color(0.86f, 0.68f, 0.5f, 1f);
            case 'R': return new Color(0.82f, 0.28f, 0.22f, 1f);
            case 'B': return new Color(0.28f, 0.48f, 0.82f, 1f);
            case 'Y': return new Color(0.95f, 0.8f, 0.25f, 1f);
            case 'E': return new Color(0.28f, 0.72f, 0.38f, 1f);
            case 'A': return new Color(0.62f, 0.66f, 0.72f, 1f);
            case 'L': return new Color(0.55f, 0.85f, 1f, 1f);
            case 'C': return new Color(0.45f, 0.52f, 0.62f, 1f);
            default: return Color.clear;
        }
    }
}
