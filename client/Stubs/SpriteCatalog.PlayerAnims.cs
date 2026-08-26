using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Player clips from Assets/_Project/Art/Sprites/player/female.
/// Peaceful: idle_calm / rotation stills + run. Combat: equipped weapon pack.
/// Rotation folders are 8 static facings (north, north_east, east, …), not clips.
/// Missing facings fall back to a neighbor or a flipped opposite.
/// </summary>
public static partial class SpriteCatalog
{
    private const string FemaleRoot = "Sprites/player/female";
    private const string IdleBase = FemaleRoot + "/idle_base/Idle_base";
    private const string IdleAnims = IdleBase + "/animations_base";
    private const string IdleRots = IdleBase + "/rotations";
    private const string IdleRotsBase = IdleBase + "/rotations_base";
    private const string IdleCalm = "Sprites/player/idle_rotation_calm";
    private const string BowRoot = FemaleRoot + "/idle_holding_longbow/holding_longbow";
    private const string DagRoot = FemaleRoot + "/idle_holding_daggers/holding_daggers";
    private const string StaffRoot = FemaleRoot + "/idle_holding_staff/holding_staff_long";
    private const string ZweiRoot = FemaleRoot + "/idle_holding_zweihander/holding_zweihander";
    private const string ZweiSkills = ZweiRoot + "/animations_zweih/zweihand_skills";
    private const string ZweiEmotes = ZweiRoot + "/animations_zweih/emo_zweihand";
    private const float IdleRotationPpuMul = 1f;
    private const float PlayerSeqPpu = 180f;

    public static bool TryEmote(int digit, out string emoteId, out string blocked)
    {
        emoteId = null;
        blocked = null;
        var weapon = StanceWeapon();
        switch (digit)
        {
            case 1:
                emoteId = "emo_scared";
                return true;
            case 2:
                emoteId = weapon == "sword" ? "emo_zweih_wink" : "emo_scared";
                return true;
            case 3:
                emoteId = weapon == "sword" ? "emo_zweih_bet" : "emo_scared";
                return true;
            case 4:
                emoteId = "idle_sitting";
                return true;
            case 5:
                emoteId = "south_powerup";
                return true;
            case 6:
                if (weapon == "staff")
                {
                    emoteId = "staff_emo_victory";
                    return true;
                }

                if (weapon == "sword")
                {
                    emoteId = "emo_zwei_readying";
                    return true;
                }

                blocked = "Shift+6 needs sword or staff";
                return false;
            case 7:
                if (weapon != "sword")
                {
                    blocked = "Shift+7 needs a sword";
                    return false;
                }

                emoteId = "emo_zweihand_taunt";
                return true;
            case 8:
                if (weapon == "staff")
                {
                    emoteId = "staff_emo_victory";
                    return true;
                }

                if (weapon != "sword")
                {
                    blocked = "Shift+8 needs a sword";
                    return false;
                }

                emoteId = "emo_zweih_come_get_me";
                return true;
            case 9:
                if (weapon != "sword")
                {
                    blocked = "Shift+9 needs a sword";
                    return false;
                }

                emoteId = "emo_zweih_victory";
                return true;
            case 0:
                if (weapon != "sword")
                {
                    blocked = "Shift+0 needs a sword";
                    return false;
                }

                emoteId = "emo_zweih_hellyeah";
                return true;
            default:
                blocked = "unknown emote";
                return false;
        }
    }

    /// <summary>South-facing idle still for the character/equipped paperdoll.</summary>
    public static Sprite PlayerPaperdoll(bool showWeapon)
    {
        var frames = GetEightDirClip(Clip.Idle, 0, showWeapon, null, out _);
        return frames != null && frames.Length > 0 ? frames[0] : null;
    }

    private static Sprite[] GetEightDirClip(Clip clip, int facing, bool inCombat, string variant, out bool flipX)
    {
        if (!PlayerBodySpritesEnabled)
        {
            flipX = false;
            LastClipFlipX = false;
            return null;
        }

        facing = ((facing % 8) + 8) % 8;
        EnsureAnimKitsLoaded();
        var weapon = StanceWeapon();
        var cacheKey = "8d:" + weapon + ":" + (inCombat ? "c" : "p") + ":" + clip + ":" +
                       (variant ?? "") + ":" + facing;
        if (TryLiveClip(cacheKey, out var cached))
        {
            flipX = ClipFlipCache.TryGetValue(cacheKey, out var cachedFlip) && cachedFlip;
            LastClipFlipX = flipX;
            return cached;
        }

        var frames = ResolvePlayerClip(clip, facing, inCombat, weapon, variant, out flipX);
        if (frames == null || frames.Length == 0)
        {
            LastClipFlipX = false;
            flipX = false;
            return null;
        }

        ClipCache[cacheKey] = frames;
        ClipFlipCache[cacheKey] = flipX;
        LastClipFlipX = flipX;
        return frames;
    }

    private static Sprite[] ResolvePlayerClip(
        Clip clip, int facing, bool inCombat, string weapon, string variant, out bool flipX)
    {
        flipX = false;
        switch (clip)
        {
            case Clip.Pickup:
            {
                var kit = TryLoadKit("unarmed", "pickup", CardinalNorthSouth(facing), out flipX);
                return Ok(kit)
                    ? kit
                    : LoadBestFacing(IdleAnims + "/idle_pickingup_animation", CardinalNorthSouth(facing), out flipX);
            }
            case Clip.Hurt:
            {
                var kit = TryLoadKit("unarmed", "hurt", facing, out flipX);
                return Ok(kit) ? kit : LoadHurtFrom(IdleAnims + "/idle_taking_dmg", facing, out flipX);
            }
            case Clip.Death:
            {
                var kit = TryLoadKit("unarmed", "death", facing, out flipX);
                return Ok(kit)
                    ? kit
                    : LoadBestFacing(IdleAnims + "/idle_falling_death", facing, out flipX);
            }
            case Clip.Emote:
                return LoadEmote(variant, facing, out flipX);
            case Clip.Attack:
            case Clip.WalkAttack:
            case Clip.RunAttack:
            {
                var kit = TryLoadKit(weapon, "attack", facing, out flipX);
                return Ok(kit) ? kit : LoadAttack(weapon, facing, out flipX);
            }
            case Clip.Skill:
                return LoadSkill(weapon, variant, facing, out flipX);
            case Clip.Walk:
            case Clip.Run:
            {
                if (inCombat)
                {
                    var kit = TryLoadKit(weapon, "run", facing, out flipX);
                    return Ok(kit) ? kit : LoadCombatRun(weapon, facing, out flipX);
                }

                var peaceful = TryLoadKit("unarmed", "run", facing, out flipX);
                return Ok(peaceful)
                    ? peaceful
                    : LoadBestFacing(IdleAnims + "/idle_running", facing, out flipX);
            }
            default:
                if (inCombat)
                {
                    var kit = TryLoadKit(weapon, "idle", facing, out flipX);
                    return Ok(kit) ? kit : LoadCombatIdle(weapon, facing, out flipX);
                }

                var calm = TryLoadKit("unarmed", "idle", facing, out flipX);
                return Ok(calm) ? calm : LoadPeacefulIdle(facing, out flipX);
        }
    }

    private static Sprite[] LoadHurtFrom(string parent, int facing, out bool flipX)
    {
        facing = ((facing % 8) + 8) % 8;
        if (facing == 2 || facing == 6)
        {
            flipX = facing == 6;
            var side = LoadSequence(parent + "/facing_side");
            if (Ok(side))
            {
                return side;
            }
        }

        var folder = facing == 3 || facing == 4 || facing == 5 ? "facing_north" : "facing_south";
        flipX = false;
        var frames = LoadSequence(parent + "/" + folder);
        if (Ok(frames))
        {
            return frames;
        }

        return LoadBestFacing(parent, facing, out flipX);
    }

    private static Sprite[] LoadPeacefulIdle(int facing, out bool flipX)
    {
        return FirstRotationFacing(facing, out flipX, IdleRotsBase, IdleRots, IdleCalm);
    }

    private static Sprite[] LoadCombatIdle(string weapon, int facing, out bool flipX)
    {
        switch (weapon)
        {
            case "bow":
                return LoadRotationFacing(BowRoot + "/rotations_longbow", facing, out flipX);
            case "daggers":
                return LoadRotationFacing(DagRoot + "/rotations_daggers", facing, out flipX);
            case "staff":
                return LoadRotationFacing(StaffRoot + "/rotations_staff", facing, out flipX);
            case "sword":
                return LoadRotationFacing(ZweiRoot + "/rotations_zweih", facing, out flipX);
            default:
                return LoadPeacefulIdle(facing, out flipX);
        }
    }

    /// <summary>One still for the facing. Rotation packs are poses, never a looping clip.</summary>
    private static Sprite[] LoadRotationFacing(string parent, int facing, out bool flipX)
    {
        flipX = false;
        facing = ((facing % 8) + 8) % 8;

        var still = LoadFacingStill(parent, facing, IdleRotationPpuMul);
        if (Ok(still))
        {
            return still;
        }

        var mirror = MirrorFacing(facing);
        if (mirror >= 0)
        {
            var mirrored = LoadFacingStill(parent, mirror, IdleRotationPpuMul);
            if (Ok(mirrored))
            {
                flipX = true;
                return mirrored;
            }
        }

        for (var delta = 1; delta <= 3; delta++)
        {
            var cw = (facing + delta) % 8;
            var neighbor = LoadFacingStill(parent, cw, IdleRotationPpuMul);
            if (Ok(neighbor))
            {
                flipX = NeedsFlip(facing, cw);
                return neighbor;
            }

            var ccw = (facing + 8 - delta) % 8;
            neighbor = LoadFacingStill(parent, ccw, IdleRotationPpuMul);
            if (Ok(neighbor))
            {
                flipX = NeedsFlip(facing, ccw);
                return neighbor;
            }
        }

        return null;
    }

    private static Sprite[] FirstRotationFacing(int facing, out bool flipX, params string[] parents)
    {
        flipX = false;
        if (parents == null)
        {
            return null;
        }

        for (var i = 0; i < parents.Length; i++)
        {
            var frames = LoadRotationFacing(parents[i], facing, out flipX);
            if (Ok(frames))
            {
                return frames;
            }
        }

        return null;
    }

    private static Sprite[] LoadCombatRun(string weapon, int facing, out bool flipX)
    {
        Sprite[] frames;
        switch (weapon)
        {
            case "bow":
                frames = LoadBestFacing(
                    BowRoot + "/animations_longbow/running_forward_running_switfly_elegant_stride",
                    facing, out flipX);
                break;
            case "daggers":
                frames = LoadBestFacing(
                    DagRoot + "/animations_daggers/forward_leaning_very_fast_steps_jumping_running_ru",
                    facing, out flipX);
                break;
            case "staff":
                frames = LoadBestFacing(
                    StaffRoot + "/animations_staff/running_foeward_running_while_holding_thwo_handed",
                    facing, out flipX);
                break;
            case "sword":
                frames = LoadBestFacing(ZweiRoot + "/animations_zweih/running_zweihander", facing, out flipX);
                break;
            default:
                return LoadBestFacing(IdleAnims + "/idle_running", facing, out flipX);
        }

        if (Ok(frames))
        {
            return frames;
        }

        return LoadBestFacing(IdleAnims + "/idle_running", facing, out flipX);
    }

    private static Sprite[] LoadAttack(string weapon, int facing, out bool flipX)
    {
        flipX = false;
        switch (weapon)
        {
            case "bow":
                return LoadBestFacing(
                    BowRoot + "/animations_longbow/loading_bow_with_arrow_shooting_arrow_stable_uprig",
                    facing, out flipX);
            case "daggers":
                return LoadCombatRun("daggers", facing, out flipX);
            case "staff":
                return LoadBestFacing(StaffRoot + "/animations_staff/chanting", facing, out flipX);
            case "sword":
                return LoadSequence(ZweiSkills + "/zweih_autoattack");
            default:
                return LoadBestFacing(IdleAnims + "/idle_running", facing, out flipX);
        }
    }

    private static Sprite[] LoadSkill(string weapon, string skillId, int facing, out bool flipX)
    {
        var id = skillId ?? "";
        if (id == "rest")
        {
            return LoadEmote("idle_sitting", facing, out flipX);
        }

        if (id == "powerup")
        {
            return LoadEmote("south_powerup", facing, out flipX);
        }

        if (weapon == "sword")
        {
            var sword = LoadSequence(ZweiSkills + "/" + SwordSkillFolder(id));
            if (Ok(sword))
            {
                flipX = false;
                return sword;
            }
        }

        if (id == "arrow_rain" || weapon == "bow")
        {
            return LoadAttack("bow", facing, out flipX);
        }

        if (id == "arcane_nova" || weapon == "staff")
        {
            if (id == "arcane_nova" || id == "thunderstorm" || id == "explosion"
                || id == "mend" || id == "power_chant" || id == "stun_bolt"
                || id == "ember_dot" || id == "ward" || id == "group_chant")
            {
                return LoadAttack("staff", facing, out flipX);
            }
        }

        if (id == "knife_fan" || (weapon == "daggers" && (id == "slash" || id == "blind_dust")))
        {
            return LoadAttack("daggers", facing, out flipX);
        }

        return LoadAttack(weapon, facing, out flipX);
    }

    private static string SwordSkillFolder(string skillId)
    {
        switch (skillId)
        {
            case "slash":
                return "zweih_focus_slash";
            case "cleave":
                return "zweih_swordwhirl";
            case "shockwave":
                return "zweih_windcutter";
            case "hook_shot":
                return "zweih_blows_of_fury";
            case "war_cry":
                return "zweih_buff_warcry";
            case "iron_stance":
                return "zweih_block";
            case "rally":
                return "zweih_buff_ready_up";
            case "decoy":
                return "zweih_taunt";
            case "shove":
                return "zweih_pike";
            case "dash":
                return "zweih_sworddance";
            case "auto_attack":
            case "auto_attack_off":
            default:
                return "zweih_autoattack";
        }
    }

    private static Sprite[] LoadEmote(string emoteId, int facing, out bool flipX)
    {
        flipX = false;
        switch (emoteId)
        {
            case "emo_scared":
                return FirstOk(
                    LoadSequence(FemaleRoot + "/emotions/emo_scared"),
                    LoadSequence(ZweiEmotes + "/emo_zweih_scared"));
            case "idle_sitting":
            {
                var sit = TryLoadKit("unarmed", "sit", facing, out flipX);
                return Ok(sit) ? sit : LoadBestFacing(IdleAnims + "/idle_sitting", facing, out flipX);
            }
            case "south_powerup":
                return FirstOk(
                    LoadSequence(ZweiEmotes + "/emo_zweihand_getting_pumped"),
                    LoadSequence(StaffRoot + "/emote_staff/emo_turnaround_with_staff"),
                    LoadSequence(FemaleRoot + "/emotions/emo_scared"));
            case "emo_zweih_wink":
                return LoadSequence(ZweiEmotes + "/emo_zweih_wink");
            case "emo_zweih_bet":
                return LoadSequence(ZweiEmotes + "/emo_zweih_bet");
            case "emo_zwei_readying":
                return LoadSequence(ZweiEmotes + "/emo_zwei_readying");
            case "emo_zweihand_taunt":
                return LoadSequence(ZweiEmotes + "/emo_zweihand_taunt");
            case "emo_zweih_come_get_me":
                return LoadSequence(ZweiRoot + "/emotes_zweih/emo_zweih_come_get_me");
            case "emo_zweih_victory":
                return LoadSequence(ZweiEmotes + "/emo_zweih_victory");
            case "emo_zweih_hellyeah":
                return LoadSequence(ZweiEmotes + "/emo_zweih_hellyeah");
            case "staff_emo_victory":
                return LoadSequence(StaffRoot + "/emote_staff/emo_turnaround_with_staff");
            default:
                return LoadSequence(FemaleRoot + "/emotions/emo_scared");
        }
    }

    private static int CardinalNorthSouth(int facing)
    {
        return facing == 3 || facing == 4 || facing == 5 ? 4 : 0;
    }

    private static Sprite[] LoadBestFacing(string parent, int facing, out bool flipX, float stillPpuMul = 1f)
    {
        flipX = false;
        facing = ((facing % 8) + 8) % 8;

        // Frame folders (walk / zwei run) win over a leftover still of the same facing.
        var seq = TryFacingAliases(parent, facing);
        if (Ok(seq))
        {
            return seq;
        }

        var still = LoadFacingStill(parent, facing, stillPpuMul);
        if (Ok(still))
        {
            return still;
        }

        var mirror = MirrorFacing(facing);
        if (mirror >= 0)
        {
            var mirrored = TryFacingAliases(parent, mirror);
            if (Ok(mirrored))
            {
                flipX = true;
                return mirrored;
            }

            var mirrorStill = LoadFacingStill(parent, mirror, stillPpuMul);
            if (Ok(mirrorStill))
            {
                flipX = true;
                return mirrorStill;
            }
        }

        var scored = BestNamedChild(parent, facing, out var sourceFacing);
        if (Ok(scored))
        {
            flipX = NeedsFlip(facing, sourceFacing);
            return scored;
        }

        return FirstOk(
            TryFacingAliases(parent, 0),
            LoadFacingStill(parent, 0, stillPpuMul),
            LoadAnimFramesOnly(parent));
    }

    /// <summary>Any PNG sequence in the folder — frame_*.png or south-west_0001.png style.</summary>
    private static Sprite[] LoadAnimFramesOnly(string resourceDir)
    {
        return LoadSequence(resourceDir);
    }

    private static Sprite[] TryFacingAliases(string parent, int facing)
    {
        var names = AliasesFor(facing);
        for (var i = 0; i < names.Length; i++)
        {
            var frames = LoadSequence(parent + "/" + names[i]);
            if (Ok(frames))
            {
                return frames;
            }
        }

        return null;
    }

    private static string[] AliasesFor(int facing)
    {
        switch (((facing % 8) + 8) % 8)
        {
            case 1:
                return new[] { "south-east", "south_east", "southeast", "southE", "east2" };
            case 2:
                return new[] { "east", "west2" };
            case 3:
                return new[] { "north-east", "north_east", "northeast", "northE" };
            case 4:
                return new[] { "north", "idle_north", "pickingup_north", "facing_north" };
            case 5:
                return new[] { "north-west", "north_west", "northwest", "northW" };
            case 6:
                return new[] { "west" };
            case 7:
                return new[] { "south-west", "south_west", "southwest", "southW" };
            default:
                return new[] { "south", "idle_south", "pickingup_south", "facing_south" };
        }
    }

    private static int MirrorFacing(int facing)
    {
        switch (((facing % 8) + 8) % 8)
        {
            case 1: return 7;
            case 2: return 6;
            case 3: return 5;
            case 5: return 3;
            case 6: return 2;
            case 7: return 1;
            default: return -1;
        }
    }

    private static bool NeedsFlip(int want, int have)
    {
        return FacingLooksLeft(want, true) != FacingLooksLeft(have, true);
    }

    private static bool HasFacingChildren(string parent)
    {
        var disk = ToDisk(parent);
        if (!Directory.Exists(disk))
        {
            return false;
        }

        try
        {
            var dirs = Directory.GetDirectories(disk);
            for (var i = 0; i < dirs.Length; i++)
            {
                if (ParseFacing(Path.GetFileName(dirs[i])) >= 0)
                {
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static Sprite[] BestNamedChild(string parent, int facing, out int sourceFacing)
    {
        sourceFacing = facing;
        var disk = ToDisk(parent);
        if (!Directory.Exists(disk))
        {
            return null;
        }

        var bestScore = int.MinValue;
        string bestRel = null;
        var bestFace = facing;
        var maxDepth = HasFacingChildren(parent) ? 0 : 3;
        WalkFacingDirs(disk, parent, 0, maxDepth, (rel, name) =>
        {
            var parsed = ParseFacing(name);
            if (parsed < 0)
            {
                return;
            }

            var delta = FacingDelta(facing, parsed);
            var score = 80 - delta * 10;
            if (name.IndexOf("east2", StringComparison.OrdinalIgnoreCase) >= 0 && facing == 1)
            {
                score = 95;
            }

            if (rel.IndexOf("zwei", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("twoh", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 4;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestRel = rel;
                bestFace = parsed;
            }
        });

        if (bestRel == null)
        {
            return null;
        }

        sourceFacing = bestFace;
        return LoadSequence(bestRel);
    }

    private static void WalkFacingDirs(string disk, string rel, int depth, int maxDepth, Action<string, string> visit)
    {
        if (depth > maxDepth || !Directory.Exists(disk))
        {
            return;
        }

        string[] dirs;
        try
        {
            dirs = Directory.GetDirectories(disk);
        }
        catch
        {
            return;
        }

        for (var i = 0; i < dirs.Length; i++)
        {
            var name = Path.GetFileName(dirs[i]);
            var childRel = rel + "/" + name;
            visit(childRel, name);
            WalkFacingDirs(dirs[i], childRel, depth + 1, maxDepth, visit);
        }
    }

    private static int FacingDelta(int a, int b)
    {
        var d = Math.Abs(a - b);
        return d > 4 ? 8 - d : d;
    }

    private static int ParseFacing(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return -1;
        }

        var s = name.ToLowerInvariant().Replace('_', '-');
        if (s == "west2" || s.Contains("west2"))
        {
            return 2;
        }

        if (s == "east2" || s.Contains("east2"))
        {
            return 1;
        }

        if (s.Contains("facing-side") || s.EndsWith("-side"))
        {
            return 2;
        }

        if (s.Contains("south-east") || s.Contains("southeast") || ContainsToken(s, "southe"))
        {
            return 1;
        }

        if (s.Contains("north-east") || s.Contains("northeast") || ContainsToken(s, "northe"))
        {
            return 3;
        }

        if (s.Contains("south-west") || s.Contains("southwest"))
        {
            return 7;
        }

        if (s.Contains("north-west") || s.Contains("northwest"))
        {
            return 5;
        }

        if (ContainsToken(s, "south"))
        {
            return 0;
        }

        if (ContainsToken(s, "north"))
        {
            return 4;
        }

        if (ContainsToken(s, "east"))
        {
            return 2;
        }

        if (ContainsToken(s, "west"))
        {
            return 6;
        }

        return -1;
    }

    private static bool ContainsToken(string hyphenated, string token)
    {
        if (hyphenated == token)
        {
            return true;
        }

        return hyphenated.StartsWith(token + "-", StringComparison.Ordinal)
               || hyphenated.EndsWith("-" + token, StringComparison.Ordinal)
               || hyphenated.Contains("-" + token + "-");
    }

    private static Sprite[] LoadFacingStill(string parent, int facing, float stillPpuMul = 1f)
    {
        var names = AliasesFor(facing);
        for (var i = 0; i < names.Length; i++)
        {
            var sp = LoadBodySprite(parent + "/" + names[i], stillPpuMul);
            if (sp != null)
            {
                return new[] { sp };
            }
        }

        return null;
    }

    private static Sprite[] LoadSequence(string resourceDir)
    {
        var frames = LoadFrameFolder(resourceDir, PlayerSeqPpu, true);
        if (Ok(frames))
        {
            return frames;
        }

        var disk = ToDisk(resourceDir);
        if (!Directory.Exists(disk))
        {
            return null;
        }

        var leaf = Path.GetFileName(disk);
        var nested = Path.Combine(disk, leaf);
        if (Directory.Exists(nested))
        {
            frames = LoadFrameFolder(resourceDir + "/" + leaf, PlayerSeqPpu, true);
            if (Ok(frames))
            {
                return frames;
            }
        }

        return null;
    }

    private static Sprite LoadBodySprite(string resourcePath, float stillPpuMul = 1f)
    {
        var cacheKey = "body:" + resourcePath + ":seq" + PlayerSeqPpu.ToString("0") + ":" + stillPpuMul.ToString("0.00") + ":g";
        if (Cache.TryGetValue(cacheKey, out var cached) && cached != null)
        {
            return cached;
        }

        var diskPng = ResolveSpriteFile(resourcePath);
        if (string.IsNullOrEmpty(diskPng) || !File.Exists(diskPng))
        {
            return null;
        }

        var tex = LoadPngFile(diskPng);
        if (tex == null)
        {
            return null;
        }

        try
        {
            // Same PPU as run/attack sequences. Canvas-max PPU made 128 stills ~1.4× taller
            // than 180 run (same opaque height, smaller canvas).
            var ppu = PlayerSeqPpu * Mathf.Max(0.01f, stillPpuMul);
            var sprite = Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                PivotAtFeet(tex),
                ppu);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            Cache[cacheKey] = sprite;
            return sprite;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Load every wired player clip + monster sheet row through Unity LoadImage.
    /// Logs missing, invisible, and locomotion flip-fallbacks. Returns issue count.
    /// </summary>
    public static int AuditWiredClips()
    {
        EnsureAnimKitsLoaded();
        var saved = HeldWeaponId;
        var issues = 0;
        try
        {
            issues += AuditPlayerSet("", false, Clip.Idle, null, true);
            issues += AuditPlayerSet("", false, Clip.Run, null, true);
            issues += AuditPlayerSet("sword", true, Clip.Idle, null, true);
            issues += AuditPlayerSet("sword", true, Clip.Run, null, true);
            issues += AuditPlayerSet("sword", true, Clip.Attack, null, false);
            issues += AuditPlayerSet("bow", true, Clip.Idle, null, true);
            issues += AuditPlayerSet("bow", true, Clip.Run, null, true);
            issues += AuditPlayerSet("bow", true, Clip.Attack, null, true);
            issues += AuditPlayerSet("dagger", true, Clip.Idle, null, true);
            issues += AuditPlayerSet("dagger", true, Clip.Run, null, true);
            issues += AuditPlayerSet("dagger", true, Clip.Attack, null, false);
            issues += AuditPlayerSet("staff", true, Clip.Idle, null, true);
            issues += AuditPlayerSet("staff", true, Clip.Run, null, true);
            issues += AuditPlayerSet("staff", true, Clip.Attack, null, false);
            issues += AuditPlayerSet("", false, Clip.Hurt, null, false);
            issues += AuditPlayerSet("", false, Clip.Death, null, true);
            issues += AuditPlayerSet("", false, Clip.Pickup, null, false);

            var bodies = new[]
            {
                "monster_slime_1", "monster_slime_2", "monster_slime_3",
                "monster_orc_1", "monster_orc_2", "monster_orc_3",
                "monster_plant_1", "monster_plant_2", "monster_plant_3",
            };
            var monsterClips = new[] { Clip.Idle, Clip.Walk, Clip.Run, Clip.Attack, Clip.Hurt, Clip.Death };
            for (var b = 0; b < bodies.Length; b++)
            {
                for (var c = 0; c < monsterClips.Length; c++)
                {
                    for (var row = 0; row < 4; row++)
                    {
                        var frames = GetClip(bodies[b], "monster", monsterClips[c], row);
                        if (!FramesAreVisible(frames))
                        {
                            issues++;
                            var tag = (frames == null || frames.Length == 0) ? "empty_facing" : "invisible";
                            GameLog.Warn(GameLog.Channel.Gfx,
                                "sprite-audit " + tag + " sheet=" + bodies[b] +
                                " clip=" + monsterClips[c] + " row=" + row);
                        }
                    }
                }
            }
        }
        finally
        {
            SetHeldWeapon(saved);
        }

        GameLog.Info(GameLog.Channel.Gfx, "sprite-audit done issues=" + issues);
        return issues;
    }

    private static int AuditPlayerSet(string weapon, bool combat, Clip clip, string variant, bool strictEight)
    {
        SetHeldWeapon(weapon);
        var n = 0;
        for (var facing = 0; facing < 8; facing++)
        {
            var frames = GetEightDirClip(clip, facing, combat, variant, out var flip);
            var tag = "sprite-audit clip=" + clip + " weapon=" + (weapon.Length == 0 ? "unarmed" : weapon) +
                      " combat=" + combat + " facing=" + facing;
            if (!FramesAreVisible(frames))
            {
                if (frames == null || frames.Length == 0)
                {
                    if (strictEight)
                    {
                        n++;
                        GameLog.Warn(GameLog.Channel.Gfx, tag + " empty_facing");
                    }
                }
                else
                {
                    n++;
                    GameLog.Warn(GameLog.Channel.Gfx, tag + " invisible");
                }

                continue;
            }

            if (flip && strictEight)
            {
                n++;
                GameLog.Warn(GameLog.Channel.Gfx, tag + " flip-fallback");
            }
        }

        return n;
    }

    private static string ToDisk(string resourceDir)
    {
        return ResolveSpriteDir(resourceDir);
    }

    private static bool Ok(Sprite[] frames)
    {
        return frames != null && frames.Length > 0;
    }

    private static Sprite[] FirstOk(params Sprite[][] packs)
    {
        if (packs == null)
        {
            return null;
        }

        for (var i = 0; i < packs.Length; i++)
        {
            if (Ok(packs[i]))
            {
                return packs[i];
            }
        }

        return null;
    }
}
