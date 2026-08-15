using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime placeholder art: unlit colored squares (works without depending on 2D lights).
/// </summary>
public sealed class GrayBoxWorld
{
    private readonly Dictionary<string, Transform> _entities = new Dictionary<string, Transform>();
    private readonly Transform _root;
    private string _selfId = "local_you";

    public GrayBoxWorld()
    {
        _root = new GameObject("GrayBoxWorld").transform;
        FrameCamera();
        SpawnTileHints();
        // Visible immediately — do not wait for sync_state
        Place(_selfId, 2f, 6f, new Color(0.15f, 0.85f, 1f), "You");
        Place("monster_slime_1", 4f, 6f, new Color(0.2f, 1f, 0.25f), "Slime");
    }

    public void HandleMessage(string json)
    {
        if (json.Contains("\"type\":\"sync_state\""))
        {
            ApplyState(json);
            return;
        }

        if (json.Contains("\"type\":\"sync_move\""))
        {
            ApplyMove(json);
            return;
        }

        if (json.Contains("\"type\":\"sync_skill\""))
        {
            Flash(ExtractString(json, "targetId"));
        }
    }

    public void SetLocalPos(float x, float y)
    {
        Place(_selfId, x, y, new Color(0.15f, 0.85f, 1f), "You");
        FrameCameraOn(x, y);
    }

    private void ApplyState(string json)
    {
        var id = ExtractString(json, "id");
        if (!string.IsNullOrEmpty(id))
        {
            RemapSelf(id);
        }

        if (TryExtractPair(json, "\"you\"", out var youX, out var youY))
        {
            Place(_selfId, youX, youY, new Color(0.15f, 0.85f, 1f), "You");
            FrameCameraOn(youX, youY);
        }

        const string slimeId = "monster_slime_1";
        if (json.Contains(slimeId) && TryExtractNear(json, slimeId, out var mx, out var my))
        {
            Place(slimeId, mx, my, new Color(0.2f, 1f, 0.25f), "Slime");
        }
    }

    private void RemapSelf(string newId)
    {
        if (newId == _selfId)
        {
            return;
        }

        if (_entities.TryGetValue(_selfId, out var t))
        {
            _entities.Remove(_selfId);
            _entities[newId] = t;
            t.name = "You";
        }

        _selfId = newId;
    }

    private void ApplyMove(string json)
    {
        var id = ExtractString(json, "entityId");
        if (string.IsNullOrEmpty(id))
        {
            return;
        }

        if (!TryReadNumberAfter(json, "\"x\":", out var x) || !TryReadNumberAfter(json, "\"y\":", out var y))
        {
            return;
        }

        var isSelf = id == _selfId;
        Place(id, x, y, isSelf ? new Color(0.15f, 0.85f, 1f) : new Color(0.2f, 1f, 0.25f), isSelf ? "You" : id);
        if (isSelf)
        {
            FrameCameraOn(x, y);
        }
    }

    private void Place(string id, float x, float y, Color color, string label)
    {
        if (!_entities.TryGetValue(id, out var t))
        {
            t = CreateMarker(color, label).transform;
            t.SetParent(_root, false);
            _entities[id] = t;
        }

        t.position = new Vector3(x, y, 0f);
    }

    private void Flash(string id)
    {
        if (string.IsNullOrEmpty(id) || !_entities.TryGetValue(id, out var t))
        {
            return;
        }

        var sr = t.GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            sr.color = Color.white;
        }
    }

    private static GameObject CreateMarker(Color color, string label)
    {
        var go = new GameObject(label);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = MakeSprite(Color.white);
        sr.color = color;
        sr.sortingOrder = 20;
        ApplyUnlit(sr);
        go.transform.localScale = Vector3.one * 1.2f;
        return go;
    }

    private static void ApplyUnlit(SpriteRenderer sr)
    {
        var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
            ?? Shader.Find("Sprites/Default")
            ?? Shader.Find("Unlit/Color");
        if (shader != null)
        {
            sr.material = new Material(shader);
        }
    }

    private static Sprite MakeSprite(Color color)
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color[size * size];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = color;
        }

        tex.SetPixels(pixels);
        tex.Apply();
        tex.filterMode = FilterMode.Point;
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    private static void FrameCamera()
    {
        FrameCameraOn(3f, 6f);
    }

    private static void FrameCameraOn(float x, float y)
    {
        var cam = Camera.main;
        if (cam == null)
        {
            cam = Object.FindAnyObjectByType<Camera>();
        }

        if (cam == null)
        {
            return;
        }

        cam.orthographic = true;
        cam.orthographicSize = 6f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.08f, 0.09f, 0.12f);
        cam.transform.position = new Vector3(x, y, -10f);
    }

    private void SpawnTileHints()
    {
        for (var x = 0; x < 20; x++)
        {
            for (var y = 0; y < 12; y++)
            {
                var blocked = x == 8 && y >= 4 && y <= 6;
                var go = new GameObject(blocked ? "wall" : "tile");
                go.transform.SetParent(_root, false);
                go.transform.position = new Vector3(x, y, 1f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = MakeSprite(Color.white);
                sr.color = blocked
                    ? new Color(0.55f, 0.2f, 0.2f)
                    : ((x + y) % 2 == 0
                        ? new Color(0.22f, 0.24f, 0.28f)
                        : new Color(0.16f, 0.18f, 0.22f));
                sr.sortingOrder = 0;
                ApplyUnlit(sr);
            }
        }
    }

    private static string ExtractString(string json, string key)
    {
        var token = "\"" + key + "\":\"";
        var start = json.IndexOf(token);
        if (start < 0)
        {
            return "";
        }

        start += token.Length;
        var end = json.IndexOf('"', start);
        return end < 0 ? "" : json.Substring(start, end - start);
    }

    private static bool TryExtractPair(string json, string section, out float x, out float y)
    {
        x = 0f;
        y = 0f;
        var idx = json.IndexOf(section);
        if (idx < 0)
        {
            return false;
        }

        var slice = json.Substring(idx, Mathf.Min(220, json.Length - idx));
        return TryReadNumberAfter(slice, "\"x\":", out x) && TryReadNumberAfter(slice, "\"y\":", out y);
    }

    private static bool TryExtractNear(string json, string id, out float x, out float y)
    {
        x = 0f;
        y = 0f;
        var idx = json.IndexOf(id);
        if (idx < 0)
        {
            return false;
        }

        var from = Mathf.Max(0, idx - 20);
        var slice = json.Substring(from, Mathf.Min(260, json.Length - from));
        return TryReadNumberAfter(slice, "\"x\":", out x) && TryReadNumberAfter(slice, "\"y\":", out y);
    }

    private static bool TryReadNumberAfter(string json, string token, out float value)
    {
        value = 0f;
        var start = json.IndexOf(token);
        if (start < 0)
        {
            return false;
        }

        start += token.Length;
        var end = start;
        while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '.' || json[end] == '-'))
        {
            end += 1;
        }

        return end > start && float.TryParse(
            json.Substring(start, end - start),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out value);
    }
}
