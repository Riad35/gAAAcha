using UnityEngine;

/// <summary>
/// Shared OnGUI chrome: panels, bars, rarity edges. No uGUI canvas.
/// </summary>
public static class UiChrome
{
    public static readonly Color Ink = new Color(0.07f, 0.08f, 0.11f, 0.94f);
    public static readonly Color Well = new Color(0.11f, 0.12f, 0.16f, 0.96f);
    public static readonly Color Gold = new Color(0.86f, 0.72f, 0.32f, 1f);
    public static readonly Color Steel = new Color(0.46f, 0.5f, 0.58f, 1f);
    public static readonly Color Hp = new Color(0.82f, 0.2f, 0.22f, 1f);
    public static readonly Color Mp = new Color(0.24f, 0.5f, 0.95f, 1f);
    public static readonly Color Xp = new Color(0.9f, 0.76f, 0.22f, 1f);

    public static void Panel(Rect rect, Color border)
    {
        GUI.color = border;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Ink;
        GUI.DrawTexture(Inset(rect, 2f), Texture2D.whiteTexture);
        GUI.color = new Color(1f, 1f, 1f, 0.06f);
        GUI.DrawTexture(new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, 1.5f), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    public static void Border(Rect rect, Color color, float t = 2f)
    {
        GUI.color = color;
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, t), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x, rect.yMax - t, rect.width, t), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x, rect.y, t, rect.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.xMax - t, rect.y, t, rect.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    public static void Slot(Rect rect, Color border)
    {
        GUI.color = Well;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        Border(rect, border, 1.5f);
    }

    public static void Bar(Rect rect, float ratio, Color fill, string label, string value)
    {
        GUI.color = new Color(0.04f, 0.04f, 0.05f, 0.92f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        var inner = Inset(rect, 1.5f);
        GUI.color = fill * 0.35f;
        GUI.DrawTexture(inner, Texture2D.whiteTexture);
        GUI.color = fill;
        var w = inner.width * Mathf.Clamp01(ratio);
        if (w > 0.5f)
        {
            GUI.DrawTexture(new Rect(inner.x, inner.y, w, inner.height), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, 0.22f);
            GUI.DrawTexture(new Rect(inner.x, inner.y, w, Mathf.Max(1f, inner.height * 0.35f)), Texture2D.whiteTexture);
        }

        GUI.color = Color.white;
        var text = string.IsNullOrEmpty(label)
            ? (value ?? "")
            : (string.IsNullOrEmpty(value) ? label : label + "  " + value);
        if (!string.IsNullOrEmpty(text))
        {
            GUI.Label(new Rect(rect.x + 4f, rect.y - 1f, rect.width - 8f, rect.height + 2f), text);
        }
    }

    public static Color Rarity(string itemId, string hint = null)
    {
        var s = ((hint ?? "") + " " + (itemId ?? "")).ToLowerInvariant();
        if (s.IndexOf("ssr") >= 0 || s.IndexOf("card_") >= 0 || s.IndexOf("aurel") >= 0 || s.IndexOf("nyla") >= 0
            || s.IndexOf("portrait") >= 0)
        {
            return new Color(1f, 0.82f, 0.28f, 1f);
        }

        if (s.IndexOf("sr") >= 0 || s.IndexOf("spirit") >= 0)
        {
            return new Color(0.78f, 0.42f, 1f, 1f);
        }

        if (s.IndexOf("ash") >= 0 || s.IndexOf("ticket") >= 0)
        {
            return new Color(0.35f, 0.72f, 1f, 1f);
        }

        if (s.IndexOf("iron") >= 0)
        {
            return new Color(0.4f, 0.85f, 0.55f, 1f);
        }

        if (string.IsNullOrEmpty(itemId))
        {
            return new Color(0.28f, 0.3f, 0.34f, 1f);
        }

        return Steel;
    }

    private static Font _font;
    private static Texture2D _cursorTex;
    private static bool _ready;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlayEnter()
    {
        _ready = false;
        _font = null;
        _cursorTex = null;
    }

    public static void Ensure()
    {
        if (_ready)
        {
            return;
        }

        _font = Font.CreateDynamicFontFromOSFont(
            new[] { "Consolas", "Lucida Console", "Courier New", "Segoe UI", "Arial" }, 14);
        _cursorTex = MakeCursor();
        Cursor.SetCursor(_cursorTex, new Vector2(2f, 2f), CursorMode.Auto);
        _ready = true;
    }

    public static void ApplyGuiSkin()
    {
        Ensure();
        if (_font == null || GUI.skin == null)
        {
            return;
        }

        GUI.skin.label.font = _font;
        GUI.skin.button.font = _font;
        GUI.skin.box.font = _font;
        GUI.skin.textField.font = _font;
        GUI.skin.textArea.font = _font;
        if (GUI.skin.window != null)
        {
            GUI.skin.window.font = _font;
        }
    }

    public static Vector2 ScreenToGui(Vector2 screenPx)
    {
        var s = Mathf.Max(0.01f, GUI.matrix.m00);
        return new Vector2(screenPx.x / s, (Screen.height - screenPx.y) / s);
    }

    public static void DrawFloat(Rect rect, string text, Color color, int size)
    {
        Ensure();
        var style = new GUIStyle(GUI.skin.label)
        {
            font = _font,
            fontSize = Mathf.Max(12, size),
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
        };
        style.normal.textColor = new Color(0f, 0f, 0f, 0.9f);
        GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, style);
        GUI.Label(new Rect(rect.x - 1f, rect.y + 1f, rect.width, rect.height), text, style);
        GUI.Label(new Rect(rect.x + 1f, rect.y - 1f, rect.width, rect.height), text, style);
        GUI.Label(new Rect(rect.x - 1f, rect.y - 1f, rect.width, rect.height), text, style);
        style.normal.textColor = color;
        GUI.Label(rect, text, style);
    }

    private static Texture2D MakeCursor()
    {
        const int s = 32;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color[s * s];
        for (var i = 0; i < px.Length; i++)
        {
            px[i] = Color.clear;
        }

        // Pointer triangle (top-left) + gold shaft.
        for (var y = 1; y <= 18; y++)
        {
            var w = Mathf.Min(y, 10);
            for (var x = 1; x <= w; x++)
            {
                var edge = x == 1 || x == w || y == 1 || y == 18;
                px[y * s + x] = edge
                    ? new Color(0.08f, 0.07f, 0.05f, 1f)
                    : new Color(0.95f, 0.82f, 0.28f, 1f);
            }
        }

        for (var y = 12; y <= 26; y++)
        {
            for (var x = 8; x <= 12; x++)
            {
                var edge = x == 8 || x == 12 || y == 26;
                px[y * s + x] = edge
                    ? new Color(0.08f, 0.07f, 0.05f, 1f)
                    : new Color(0.55f, 0.82f, 0.95f, 1f);
            }
        }

        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    private static Rect Inset(Rect r, float t)
    {
        return new Rect(r.x + t, r.y + t, r.width - t * 2f, r.height - t * 2f);
    }
}
