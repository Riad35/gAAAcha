using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Loads StreamingAssets/Data/animation_kits.json + sprite_infos.json at first clip resolve.
/// Folder paths are the live female pack; missing clips stay missing and fall back.
/// </summary>
public static partial class SpriteCatalog
{
    [Serializable]
    private class AnimKitsFile
    {
        public int version;
        public float ppu;
        public int frameDurationMs;
        public string[] facingNames;
        public AnimKitFile[] kits;
        public AnimSkillFile[] skills;
        public AnimEmoteFile[] emotes;
    }

    [Serializable]
    private class AnimKitFile
    {
        public string id;
        public AnimClipFile[] clips;
    }

    [Serializable]
    private class AnimClipFile
    {
        public string id;
        public string state;
        public string kind;
        public string status;
        public string pose;
        public int dirs;
        public string folder;
        public string[] altFolders;
        public string[] present;
        public string[] missing;
        public string fallback;
        public string task;
    }

    [Serializable]
    private class AnimSkillFile
    {
        public string id;
        public string weapon;
        public string folder;
        public string kit;
        public string clip;
        public string emote;
        public int dirs;
        public string state;
        public string status;
    }

    [Serializable]
    private class AnimEmoteFile
    {
        public string id;
        public string folder;
        public string[] altFolders;
        public string kit;
        public string clip;
        public int dirs;
        public string status;
    }

    [Serializable]
    private class SpriteInfosFile
    {
        public int version;
        public float defaultPpu;
        public string defaultPivot;
        public SpritePoseFile[] poses;
    }

    [Serializable]
    private class SpritePoseFile
    {
        public string id;
        public string pivot;
        public float ppu;
        public bool originYLater;
        public string task;
    }

    private static bool AnimKitsTried;
    private static AnimKitsFile LoadedKits;
    private static SpriteInfosFile LoadedInfos;
    private static readonly Dictionary<string, AnimClipFile> ClipByKey =
        new Dictionary<string, AnimClipFile>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, SpritePoseFile> PoseById =
        new Dictionary<string, SpritePoseFile>(StringComparer.OrdinalIgnoreCase);

    public static void EnsureAnimKitsLoaded()
    {
        if (AnimKitsTried)
        {
            return;
        }

        AnimKitsTried = true;
        LoadedKits = ReadJson<AnimKitsFile>("animation_kits.json");
        LoadedInfos = ReadJson<SpriteInfosFile>("sprite_infos.json");
        ClipByKey.Clear();
        PoseById.Clear();

        if (LoadedKits != null && LoadedKits.kits != null)
        {
            for (var k = 0; k < LoadedKits.kits.Length; k++)
            {
                var kit = LoadedKits.kits[k];
                if (kit == null || string.IsNullOrEmpty(kit.id) || kit.clips == null)
                {
                    continue;
                }

                for (var c = 0; c < kit.clips.Length; c++)
                {
                    var clip = kit.clips[c];
                    if (clip == null || string.IsNullOrEmpty(clip.id))
                    {
                        continue;
                    }

                    ClipByKey[kit.id + "." + clip.id] = clip;
                }
            }
        }

        if (LoadedInfos != null && LoadedInfos.poses != null)
        {
            for (var i = 0; i < LoadedInfos.poses.Length; i++)
            {
                var pose = LoadedInfos.poses[i];
                if (pose != null && !string.IsNullOrEmpty(pose.id))
                {
                    PoseById[pose.id] = pose;
                }
            }
        }

        if (LoadedKits == null || ClipByKey.Count == 0)
        {
            GameLog.Warn(GameLog.Channel.Gfx,
                "anim-kits missing or empty  path=StreamingAssets/Data/animation_kits.json  fallback=hardcoded");
            return;
        }

        GameLog.Info(GameLog.Channel.Gfx,
            "anim-kits loaded version=" + LoadedKits.version +
            " clips=" + ClipByKey.Count +
            " poses=" + PoseById.Count);
    }

    private static T ReadJson<T>(string fileName) where T : class
    {
        try
        {
            var path = Path.Combine(Application.streamingAssetsPath, "Data", fileName);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            return JsonUtility.FromJson<T>(json);
        }
        catch (Exception ex)
        {
            GameLog.Warn(GameLog.Channel.Gfx, "anim-kits read fail file=" + fileName + " err=" + ex.Message);
            return null;
        }
    }

    private static AnimClipFile FindKitClip(string kitId, string clipId)
    {
        EnsureAnimKitsLoaded();
        if (string.IsNullOrEmpty(kitId) || string.IsNullOrEmpty(clipId))
        {
            return null;
        }

        ClipByKey.TryGetValue(kitId + "." + clipId, out var clip);
        return clip;
    }

    private static Sprite[] TryLoadKit(string kitId, string clipId, int facing, out bool flipX)
    {
        return TryLoadKit(kitId, clipId, facing, 0, out flipX);
    }

    private static Sprite[] TryLoadKit(string kitId, string clipId, int facing, int depth, out bool flipX)
    {
        flipX = false;
        if (depth > 4 || string.IsNullOrEmpty(kitId) || string.IsNullOrEmpty(clipId))
        {
            return null;
        }

        var def = FindKitClip(kitId, clipId);
        if (def == null)
        {
            return null;
        }

        if (string.Equals(def.status, "missing", StringComparison.OrdinalIgnoreCase)
            || string.Equals(def.kind, "missing", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(def.folder))
        {
            return TryFallback(kitId, def.fallback, facing, depth, out flipX);
        }

        Sprite[] frames = null;
        if (string.Equals(def.kind, "stills", StringComparison.OrdinalIgnoreCase))
        {
            frames = FirstRotationFacing(facing, out flipX, CollectFolders(def));
        }
        else if (string.Equals(def.kind, "hurt", StringComparison.OrdinalIgnoreCase))
        {
            frames = LoadHurtFrom(def.folder, facing, out flipX);
        }
        else if (def.dirs == 1)
        {
            frames = LoadSequence(def.folder);
            flipX = false;
        }
        else
        {
            frames = LoadBestFacing(def.folder, facing, out flipX);
        }

        if (Ok(frames))
        {
            return frames;
        }

        return TryFallback(kitId, def.fallback, facing, depth, out flipX);
    }

    private static Sprite[] TryFallback(string kitId, string fallback, int facing, int depth, out bool flipX)
    {
        flipX = false;
        if (string.IsNullOrEmpty(fallback))
        {
            return null;
        }

        var otherKit = kitId;
        var otherClip = fallback;
        var dot = fallback.LastIndexOf('.');
        if (dot > 0 && dot < fallback.Length - 1)
        {
            otherKit = fallback.Substring(0, dot);
            otherClip = fallback.Substring(dot + 1);
        }

        return TryLoadKit(otherKit, otherClip, facing, depth + 1, out flipX);
    }

    private static string[] CollectFolders(AnimClipFile def)
    {
        var n = 1 + (def.altFolders != null ? def.altFolders.Length : 0);
        var folders = new string[n];
        folders[0] = def.folder;
        if (def.altFolders != null)
        {
            for (var i = 0; i < def.altFolders.Length; i++)
            {
                folders[i + 1] = def.altFolders[i];
            }
        }

        return folders;
    }
}
