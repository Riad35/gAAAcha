using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Craftpix sheets: split into frames, play idle/walk clips. File load via StreamingAssets.
/// </summary>
public static class SpriteCatalog
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
    }

    private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();
    private static readonly Dictionary<string, Texture2D> TexCache = new Dictionary<string, Texture2D>();
    private static readonly Dictionary<string, Sprite[]> ClipCache = new Dictionary<string, Sprite[]>();

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

    /// <summary>StreamingAssets body prefix: slime, orc, or plant.</summary>
    public static string MonsterBody(string id)
    {
        var s = id ?? "";
        if (ContainsAny(s, "ruins", "colossus"))
        {
            return "plant";
        }

        if (ContainsAny(s, "crypt_boss", "m_boss_f2", "tower_boss_f2"))
        {
            return "orc";
        }

        if (ContainsAny(s, "m_boss_f5", "tower_boss_f5", "apex"))
        {
            return "slime";
        }

        if (ContainsAny(s, "orc", "brute", "guard", "knight", "elite", "crypt", "shade", "shadow", "warden"))
        {
            return "orc";
        }

        if (ContainsAny(s, "plant", "marsh", "pest", "beetle", "golem", "wisp"))
        {
            return "plant";
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

    /// <summary>Idle / walk / run / attack / hurt / death frames (facingRow 0=down/front … 3=up).</summary>
    public static Sprite[] GetClip(string id, string kind, Clip clip, int facingRow = 0)
    {
        string path = null;
        if (IsPlayerKind(id, kind))
        {
            path = clip switch
            {
                Clip.Walk => "Sprites/player_walk",
                Clip.Run => "Sprites/player_run",
                Clip.Attack => "Sprites/player_attack",
                Clip.WalkAttack => "Sprites/player_walk_attack",
                Clip.RunAttack => "Sprites/player_run_attack",
                Clip.Hurt => "Sprites/player_hurt",
                Clip.Death => "Sprites/player_death",
                _ => "Sprites/player_idle",
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

    public static Sprite ForFloor(string mapId, bool even)
    {
        string path;
        if (!string.IsNullOrEmpty(mapId) && mapId.Contains("marsh"))
        {
            path = "Tiles/floor_dirt";
        }
        else if (!string.IsNullOrEmpty(mapId) && mapId.StartsWith("town"))
        {
            path = "Tiles/floor_cement";
        }
        else if (!string.IsNullOrEmpty(mapId) && (mapId.StartsWith("dungeon") || mapId.StartsWith("tower")))
        {
            path = "Tiles/floor_brick";
        }
        else if (!string.IsNullOrEmpty(mapId) && mapId.StartsWith("field"))
        {
            path = "Tiles/floor_grass";
        }
        else
        {
            path = "Tiles/floor_dirt";
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

    public static Sprite ForWall(string mapId)
    {
        var wall = FullTextureSprite("Tiles/wall_mound", 256f);
        if (wall != null)
        {
            return wall;
        }

        return MakeShape(Shape.Square, Wall(mapId));
    }

    public static float EntityScale(string id, string kind)
    {
        if (HasArtSprite(id, kind))
        {
            if (!string.IsNullOrEmpty(id) && (id.Contains("king") || id.Contains("boss") || id.Contains("crypt_lord")
                || id.Contains("colossus") || id.Contains("warden") || id.Contains("apex")))
            {
                if (id.Contains("king"))
                {
                    return 3.45f;
                }

                if (id.Contains("ruins") || id.Contains("colossus") || id.Contains("m_boss_f5") || id.Contains("apex"))
                {
                    return 2.15f;
                }

                return 1.75f;
            }

            return 2.2f; // 64px frames need a bit of scale vs 1-world-unit tiles
        }

        var shape = ShapeFor(id, kind);
        return shape == Shape.Cross ? 1.45f : 1.2f;
    }

    public static bool HasArtSprite(string id, string kind)
    {
        var frames = GetClip(id, kind, Clip.Idle);
        return frames != null && frames.Length > 0;
    }

    /// <summary>
    /// Craftpix top-down sheets: 4 direction rows (down/left/right/up) × N frame columns.
    /// Returns the down-facing row as an animation clip.
    /// </summary>
    public static Sprite[] SliceSheet(string resourcePath, int facingRow = 0)
    {
        var cacheKey = resourcePath + "@d" + facingRow;
        if (ClipCache.TryGetValue(cacheKey, out var cached) && cached != null && cached.Length > 0)
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
        // Using width/3 was wrong (256px = 4 cells glued together → 4 characters side by side).
        var frameW = frameH;
        if (frameW <= 0 || tex.width % frameW != 0)
        {
            GameLog.WarnOnce(GameLog.Channel.Gfx, "wdiv:" + resourcePath,
                "reason=width_not_divisible  sheet=" + resourcePath +
                "  tex=" + tex.width + "x" + tex.height + "  frame=" + frameW + "x" + frameH +
                "  fallback=shape");
            return null;
        }

        var cols = tex.width / frameW;
        facingRow = Mathf.Clamp(facingRow, 0, dirRows - 1);
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
                        new Vector2(0.5f, 0.5f),
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

            // Skip fully transparent / empty cells (idle/death sheets often pad with blanks).
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
            // Fallback: at least first cell even if alpha check failed.
            var key0 = cacheKey + "#0";
            if (Cache.TryGetValue(key0, out var first) && first != null)
            {
                packed.Add(first);
            }
        }

        var frames = packed.ToArray();
        if (frames.Length > 0)
        {
            ClipCache[cacheKey] = frames;
        }

        return frames.Length > 0 ? frames : null;
    }

    private static bool FrameHasVisiblePixels(Texture2D tex, int x, int y, int w, int h)
    {
        try
        {
            var stepX = Mathf.Max(1, w / 8);
            var stepY = Mathf.Max(1, h / 8);
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
                new Vector2(0.5f, 0.5f),
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
        var path = Path.Combine(Application.streamingAssetsPath, resourcePath.Replace('/', Path.DirectorySeparatorChar) + ".png");
        return LoadPngFile(path);
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

            return tex;
        }
        catch (System.Exception ex)
        {
            GameLog.WarnOnce(GameLog.Channel.Gfx, "file:" + path,
                "reason=file_load_failed  path=" + path + "  err=" + ex.Message);
            return null;
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
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
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

    private static Sprite SpriteFromCharMap(string[] rows)
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
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.35f, 0.2f), 16f);
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
            default: return Color.clear;
        }
    }
}
