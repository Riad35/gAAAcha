using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Combat VFX from Assets/_Project/Art/vfx. Black-matte sheets are keyed transparent.
/// </summary>
public static class VfxCatalog
{
    private const float SheetFps = 12f;
    private const float ClipFps = 16f;

    private static readonly Dictionary<string, Sprite[]> Cache = new Dictionary<string, Sprite[]>();
    private static readonly Regex TrailingNum = new Regex(@"(\d+)\s*$", RegexOptions.Compiled);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlayEnter()
    {
        Cache.Clear();
    }

    private static bool TryLive(string key, out Sprite[] frames)
    {
        if (Cache.TryGetValue(key, out frames) && frames != null && frames.Length > 0)
        {
            for (var i = 0; i < frames.Length; i++)
            {
                if (frames[i] != null && frames[i].texture != null)
                {
                    return true;
                }
            }
        }

        frames = null;
        return false;
    }

    public static Sprite[] MagicBolt()
    {
        return FirstSheet("projectiles/3", "projectiles/3_2");
    }

    public static Sprite[] EmberBolt()
    {
        return FirstSheet("projectiles/4", "projectiles/4_1");
    }

    public static Sprite[] LockIcon()
    {
        return HorizontalSheet("target_icon/target_icon_animation");
    }

    public static Sprite[] LightningBurst()
    {
        return FolderSequence("explosions/PNG/Lightning", name =>
            name.IndexOf("part", StringComparison.OrdinalIgnoreCase) < 0
            && (name.IndexOf("beginning", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("cycle", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("end", StringComparison.OrdinalIgnoreCase) >= 0));
    }

    public static Sprite[] NuclearBurst()
    {
        return FolderSequence("explosions/PNG/Nuclear_explosion", null);
    }

    public static float DefaultSheetFps => SheetFps;
    public static float DefaultClipFps => ClipFps;

    private static Sprite[] FirstSheet(params string[] rels)
    {
        for (var i = 0; i < rels.Length; i++)
        {
            var frames = HorizontalSheet(rels[i]);
            if (frames != null && frames.Length > 0)
            {
                return frames;
            }
        }

        return null;
    }

    public static Sprite[] HorizontalSheet(string relNoExt)
    {
        if (string.IsNullOrEmpty(relNoExt))
        {
            return null;
        }

        if (TryLive("hs:" + relNoExt, out var cached))
        {
            return cached;
        }

        var path = ToDisk(relNoExt) + ".png";
        var tex = LoadKeyedPng(path);
        if (tex == null)
        {
            return null;
        }

        var cell = tex.height;
        if (cell < 8 || tex.width < cell)
        {
            var one = new[] { MakeSprite(tex, 0, 0, tex.width, tex.height) };
            Cache["hs:" + relNoExt] = one;
            return one;
        }

        var count = Mathf.Max(1, tex.width / cell);
        var frames = new Sprite[count];
        for (var i = 0; i < count; i++)
        {
            frames[i] = MakeSprite(tex, i * cell, 0, cell, cell);
        }

        Cache["hs:" + relNoExt] = frames;
        return frames;
    }

    private static Sprite[] FolderSequence(string relDir, Func<string, bool> accept)
    {
        var key = "dir:" + relDir;
        if (TryLive(key, out var cached))
        {
            return cached;
        }

        var disk = ToDisk(relDir);
        if (!Directory.Exists(disk))
        {
            return null;
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(disk, "*.png");
        }
        catch
        {
            return null;
        }

        if (files == null || files.Length == 0)
        {
            return null;
        }

        Array.Sort(files, CompareVfxName);
        var packed = new List<Sprite>(files.Length);
        for (var i = 0; i < files.Length; i++)
        {
            var name = Path.GetFileNameWithoutExtension(files[i]);
            if (accept != null && !accept(name))
            {
                continue;
            }

            var tex = LoadKeyedPng(files[i]);
            if (tex == null)
            {
                continue;
            }

            packed.Add(MakeSprite(tex, 0, 0, tex.width, tex.height));
        }

        if (packed.Count == 0)
        {
            return null;
        }

        var frames = packed.ToArray();
        Cache[key] = frames;
        return frames;
    }

    private static int CompareVfxName(string a, string b)
    {
        var na = Path.GetFileNameWithoutExtension(a) ?? "";
        var nb = Path.GetFileNameWithoutExtension(b) ?? "";
        var ga = LightningGroup(na);
        var gb = LightningGroup(nb);
        if (ga != gb)
        {
            return ga.CompareTo(gb);
        }

        var ia = TrailingInt(na);
        var ib = TrailingInt(nb);
        if (ia != ib)
        {
            return ia.CompareTo(ib);
        }

        return string.Compare(na, nb, StringComparison.OrdinalIgnoreCase);
    }

    private static int LightningGroup(string name)
    {
        var s = name.ToLowerInvariant();
        if (s.Contains("beginning"))
        {
            return 0;
        }

        if (s.Contains("cycle"))
        {
            return 1;
        }

        if (s.Contains("end"))
        {
            return 2;
        }

        if (s.Contains("spot"))
        {
            return 3;
        }

        return 5;
    }

    private static int TrailingInt(string name)
    {
        var m = TrailingNum.Match(name);
        if (!m.Success)
        {
            return 0;
        }

        int.TryParse(m.Groups[1].Value, out var n);
        return n;
    }

    private static Sprite MakeSprite(Texture2D tex, int x, int y, int w, int h)
    {
        return Sprite.Create(tex, new Rect(x, y, w, h), new Vector2(0.5f, 0.5f), Mathf.Max(48f, h));
    }

    private static Texture2D LoadKeyedPng(string path)
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
            KeyOutNearBlack(tex);
            return tex;
        }
        catch
        {
            return null;
        }
    }

    private static void KeyOutNearBlack(Texture2D tex)
    {
        var pixels = tex.GetPixels();
        var dirty = false;
        for (var i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i];
            if (p.a < 0.04f || (p.r < 0.05f && p.g < 0.05f && p.b < 0.05f))
            {
                if (p.a > 0f || p.r > 0f || p.g > 0f || p.b > 0f)
                {
                    pixels[i] = Color.clear;
                    dirty = true;
                }
            }
        }

        if (dirty)
        {
            tex.SetPixels(pixels);
            tex.Apply();
        }
    }

    private static string ToDisk(string rel)
    {
        return Path.Combine(
            Application.dataPath,
            "_Project",
            "Art",
            "vfx",
            (rel ?? "").Replace('/', Path.DirectorySeparatorChar));
    }
}
