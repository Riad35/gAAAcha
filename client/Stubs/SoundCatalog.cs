using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tiny procedural SFX bus (no wav files). Drop next to SpriteCatalog.
/// </summary>
public static class SoundCatalog
{
    public enum Id
    {
        UiClick,
        Loot,
        LevelUp,
        Portal,
        Pull,
        Swing,
        Bow,
        Hit,
        Hurt,
        Death,
        Crit,
        Shockwave,
        Dash,
        Rally,
        Hook,
        Mend,
        Decoy,
    }

    private const int Hz = 22050;
    private const float Master = 0.22f;
    private const float Retrigger = 0.07f;

    private static AudioSource _src;
    private static AudioSource _music;
    private static readonly Dictionary<Id, AudioClip> Clips = new Dictionary<Id, AudioClip>();
    private static readonly Dictionary<Id, float> Last = new Dictionary<Id, float>();
    private static readonly Dictionary<string, AudioClip> Loops = new Dictionary<string, AudioClip>();
    private static bool _musicOn = true;
    private static string _musicKey = "";

    public static void Ensure()
    {
        if (_src != null)
        {
            return;
        }

        var go = new GameObject("sfx_bus");
        Object.DontDestroyOnLoad(go);
        _src = go.AddComponent<AudioSource>();
        _src.playOnAwake = false;
        _src.spatialBlend = 0f;
        _src.volume = 1f;
        _music = go.AddComponent<AudioSource>();
        _music.playOnAwake = false;
        _music.loop = true;
        _music.spatialBlend = 0f;
        _music.volume = 0.07f;
        BuildAll();
    }

    public static void Play(Id id, float volume = 1f)
    {
        Ensure();
        if (Last.TryGetValue(id, out var at) && Time.unscaledTime - at < Retrigger)
        {
            return;
        }

        if (!Clips.TryGetValue(id, out var clip) || clip == null || _src == null)
        {
            return;
        }

        Last[id] = Time.unscaledTime;
        _src.PlayOneShot(clip, Master * Mathf.Clamp01(volume));
    }

    public static void PlaySkill(string skillId)
    {
        switch (skillId)
        {
            case "shot":
            case "stun_bolt":
            case "thunderstorm":
                Play(Id.Shockwave);
                return;
            case "explosion":
                Play(Id.Shockwave);
                return;
            case "shockwave":
                Play(Id.Shockwave);
                return;
            case "dash":
                Play(Id.Dash);
                return;
            case "rally":
                Play(Id.Rally);
                return;
            case "hook_shot":
            case "pull":
                Play(Id.Hook);
                return;
            case "mend":
                Play(Id.Mend);
                return;
            case "decoy":
                Play(Id.Decoy);
                return;
            default:
                Play(Id.Swing);
                return;
        }
    }

    private static void BuildAll()
    {
        Clips[Id.UiClick] = Tone("ui", 0.05f, 880f, 1320f, 0.5f, false);
        Clips[Id.Loot] = Chord("loot", 0.18f, 523f, 659f, 784f);
        Clips[Id.LevelUp] = Arp("lvl", 0.42f, 392f, 523f, 659f, 784f);
        Clips[Id.Portal] = Sweep("portal", 0.32f, 180f, 520f, true);
        Clips[Id.Pull] = Arp("pull", 0.38f, 330f, 440f, 554f, 740f);
        Clips[Id.Swing] = NoiseBurst("swing", 0.11f, 0.7f, 900f);
        Clips[Id.Bow] = Tone("bow", 0.09f, 620f, 240f, 0.7f, true);
        Clips[Id.Hit] = Thud("hit", 0.09f, 90f);
        Clips[Id.Hurt] = Tone("hurt", 0.12f, 280f, 140f, 0.65f, true);
        Clips[Id.Death] = Sweep("death", 0.34f, 220f, 70f, false);
        Clips[Id.Crit] = Chord("crit", 0.16f, 880f, 1174f, 1568f);
        Clips[Id.Shockwave] = Thud("wave", 0.2f, 55f);
        Clips[Id.Dash] = Sweep("dash", 0.14f, 240f, 720f, true);
        Clips[Id.Rally] = Chord("rally", 0.22f, 392f, 494f, 587f);
        Clips[Id.Hook] = Tone("hook", 0.12f, 200f, 480f, 0.6f, false);
        Clips[Id.Mend] = Chord("mend", 0.24f, 523f, 659f, 784f);
        Clips[Id.Decoy] = Sweep("decoy", 0.2f, 480f, 240f, true);
        Loops["town"] = Pad("town", 4f, 196f, 247f, 294f, 0.22f);
        Loops["field"] = Pad("field", 4f, 174f, 220f, 261f, 0.18f);
        Loops["dungeon"] = Pad("dungeon", 4f, 98f, 123f, 147f, 0.2f);
    }

    public static void SetMusicEnabled(bool on)
    {
        _musicOn = on;
        Ensure();
        if (_music == null)
        {
            return;
        }

        if (!_musicOn)
        {
            _music.Stop();
            return;
        }

        if (!string.IsNullOrEmpty(_musicKey) && Loops.TryGetValue(_musicKey, out var clip) && clip != null)
        {
            _music.clip = clip;
            if (!_music.isPlaying)
            {
                _music.Play();
            }
        }
    }

    public static void PlayMap(string mapId)
    {
        Ensure();
        var key = MapLoopKey(mapId);
        if (key == _musicKey)
        {
            if (_musicOn && _music != null && !_music.isPlaying && _music.clip != null)
            {
                _music.Play();
            }

            return;
        }

        _musicKey = key;
        if (_music == null)
        {
            return;
        }

        if (!_musicOn || string.IsNullOrEmpty(key) || !Loops.TryGetValue(key, out var clip) || clip == null)
        {
            _music.Stop();
            _music.clip = null;
            return;
        }

        _music.clip = clip;
        _music.Play();
    }

    private static string MapLoopKey(string mapId)
    {
        var id = mapId ?? "";
        if (id.StartsWith("town"))
        {
            return "town";
        }

        if (id.StartsWith("field") || id.Contains("marsh"))
        {
            return "field";
        }

        if (id.StartsWith("dungeon") || id.StartsWith("tower"))
        {
            return "dungeon";
        }

        return "";
    }

    private static AudioClip Pad(string name, float sec, float a, float b, float c, float amp)
    {
        var n = Mathf.Max(8, Mathf.RoundToInt(sec * Hz));
        var data = new float[n];
        for (var i = 0; i < n; i++)
        {
            var s = Mathf.Sin(i * 2f * Mathf.PI * a / Hz) * 0.55f
                + Mathf.Sin(i * 2f * Mathf.PI * b / Hz) * 0.35f
                + Mathf.Sin(i * 2f * Mathf.PI * c / Hz) * 0.22f;
            data[i] = s * amp;
        }

        return Clip("loop_" + name, data);
    }

    private static AudioClip Tone(string name, float sec, float f0, float f1, float amp, bool noise)
    {
        var n = Mathf.Max(8, Mathf.RoundToInt(sec * Hz));
        var data = new float[n];
        for (var i = 0; i < n; i++)
        {
            var t = i / (float)(n - 1);
            var env = t < 0.08f ? t / 0.08f : 1f - (t - 0.08f) / 0.92f;
            env = Mathf.Clamp01(env);
            var f = Mathf.Lerp(f0, f1, t);
            var s = Mathf.Sin(i * 2f * Mathf.PI * f / Hz);
            if (noise)
            {
                s = s * 0.7f + (Hash(i) * 2f - 1f) * 0.3f;
            }

            data[i] = s * env * amp;
        }

        return Clip(name, data);
    }

    private static AudioClip Sweep(string name, float sec, float f0, float f1, bool air)
    {
        var n = Mathf.Max(8, Mathf.RoundToInt(sec * Hz));
        var data = new float[n];
        for (var i = 0; i < n; i++)
        {
            var t = i / (float)(n - 1);
            var env = Mathf.Sin(t * Mathf.PI);
            var f = Mathf.Lerp(f0, f1, t * t);
            var s = Mathf.Sin(i * 2f * Mathf.PI * f / Hz);
            if (air)
            {
                s = s * 0.55f + (Hash(i + 17) * 2f - 1f) * 0.45f * (1f - t);
            }

            data[i] = s * env * 0.7f;
        }

        return Clip(name, data);
    }

    private static AudioClip Thud(string name, float sec, float f)
    {
        var n = Mathf.Max(8, Mathf.RoundToInt(sec * Hz));
        var data = new float[n];
        for (var i = 0; i < n; i++)
        {
            var t = i / (float)(n - 1);
            var env = Mathf.Exp(-t * 14f);
            var s = Mathf.Sin(i * 2f * Mathf.PI * f / Hz) * 0.75f
                + (Hash(i + 3) * 2f - 1f) * 0.25f * (1f - t);
            data[i] = s * env;
        }

        return Clip(name, data);
    }

    private static AudioClip NoiseBurst(string name, float sec, float amp, float hipass)
    {
        var n = Mathf.Max(8, Mathf.RoundToInt(sec * Hz));
        var data = new float[n];
        var prev = 0f;
        for (var i = 0; i < n; i++)
        {
            var t = i / (float)(n - 1);
            var env = t < 0.15f ? t / 0.15f : 1f - (t - 0.15f) / 0.85f;
            var raw = Hash(i + 91) * 2f - 1f;
            var hp = raw - prev;
            prev = raw;
            var tone = Mathf.Sin(i * 2f * Mathf.PI * hipass / Hz) * 0.25f;
            data[i] = (hp * 0.7f + tone) * Mathf.Clamp01(env) * amp;
        }

        return Clip(name, data);
    }

    private static AudioClip Chord(string name, float sec, float a, float b, float c)
    {
        var n = Mathf.Max(8, Mathf.RoundToInt(sec * Hz));
        var data = new float[n];
        for (var i = 0; i < n; i++)
        {
            var t = i / (float)(n - 1);
            var env = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
            var s = Mathf.Sin(i * 2f * Mathf.PI * a / Hz)
                + Mathf.Sin(i * 2f * Mathf.PI * b / Hz) * 0.7f
                + Mathf.Sin(i * 2f * Mathf.PI * c / Hz) * 0.5f;
            data[i] = s * 0.28f * env;
        }

        return Clip(name, data);
    }

    private static AudioClip Arp(string name, float sec, float a, float b, float c, float d)
    {
        var n = Mathf.Max(8, Mathf.RoundToInt(sec * Hz));
        var notes = new[] { a, b, c, d };
        var data = new float[n];
        var slice = n / 4;
        for (var i = 0; i < n; i++)
        {
            var ni = Mathf.Clamp(i / Mathf.Max(1, slice), 0, 3);
            var local = (i % Mathf.Max(1, slice)) / (float)Mathf.Max(1, slice);
            var env = Mathf.Sin(local * Mathf.PI);
            data[i] = Mathf.Sin(i * 2f * Mathf.PI * notes[ni] / Hz) * 0.45f * env;
        }

        return Clip(name, data);
    }

    private static AudioClip Clip(string name, float[] data)
    {
        var clip = AudioClip.Create("sfx_" + name, data.Length, 1, Hz, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static float Hash(int i)
    {
        var x = (uint)(i * 1103515245 + 12345);
        return (x & 0xFFFF) / 65535f;
    }
}
