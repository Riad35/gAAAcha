using System.Collections.Generic;
using UnityEngine;

/// <summary>Tiny JSON field helpers for gray-box protocol (no third-party deps).</summary>
public static class JsonUtil
{
    public static string ExtractString(string json, string key)
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

    public static bool TryNumber(string json, string key, out float value)
    {
        value = 0f;
        var token = "\"" + key + "\":";
        var start = json.IndexOf(token);
        if (start < 0)
        {
            return false;
        }

        start += token.Length;
        while (start < json.Length && (json[start] == ' ' || json[start] == '\t'))
        {
            start += 1;
        }

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

    public static bool TryInt(string json, string key, out int value)
    {
        value = 0;
        if (!TryNumber(json, key, out var f))
        {
            return false;
        }

        value = Mathf.RoundToInt(f);
        return true;
    }

    public static string SliceAround(string json, string marker, int before, int after)
    {
        var idx = json.IndexOf(marker);
        if (idx < 0)
        {
            return "";
        }

        var from = Mathf.Max(0, idx - before);
        return json.Substring(from, Mathf.Min(after, json.Length - from));
    }

    /// <summary>Extract a JSON object value for key (brace-matched). Avoids matching mapId etc.</summary>
    public static string ExtractObject(string json, string key)
    {
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
        {
            return "";
        }

        var token = "\"" + key + "\":";
        var searchFrom = 0;
        while (searchFrom < json.Length)
        {
            var idx = json.IndexOf(token, searchFrom);
            if (idx < 0)
            {
                return "";
            }

            // Reject longer keys like mapId / homeMapId that contain the same prefix.
            if (idx > 0)
            {
                var prev = json[idx - 1];
                if (prev != '{' && prev != ',' && prev != ' ' && prev != '\n' && prev != '\r' && prev != '\t')
                {
                    searchFrom = idx + token.Length;
                    continue;
                }
            }

            var i = idx + token.Length;
            while (i < json.Length && char.IsWhiteSpace(json[i]))
            {
                i += 1;
            }

            if (i >= json.Length || json[i] != '{')
            {
                searchFrom = idx + token.Length;
                continue;
            }

            var depth = 0;
            var inString = false;
            var escape = false;
            for (var j = i; j < json.Length; j++)
            {
                var c = json[j];
                if (inString)
                {
                    if (escape)
                    {
                        escape = false;
                    }
                    else if (c == '\\')
                    {
                        escape = true;
                    }
                    else if (c == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == '{')
                {
                    depth += 1;
                }
                else if (c == '}')
                {
                    depth -= 1;
                    if (depth == 0)
                    {
                        return json.Substring(i, j - i + 1);
                    }
                }
            }

            return "";
        }

        return "";
    }

    /// <summary>Brace-matched JSON array value for key. Rejects suffix matches (classSkillIds vs skillIds).</summary>
    public static string ExtractArray(string json, string key)
    {
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
        {
            return "";
        }

        var token = "\"" + key + "\":";
        var searchFrom = 0;
        while (searchFrom < json.Length)
        {
            var idx = json.IndexOf(token, searchFrom);
            if (idx < 0)
            {
                return "";
            }

            if (idx > 0)
            {
                var prev = json[idx - 1];
                if (char.IsLetterOrDigit(prev) || prev == '_')
                {
                    searchFrom = idx + token.Length;
                    continue;
                }
            }

            var i = idx + token.Length;
            while (i < json.Length && char.IsWhiteSpace(json[i]))
            {
                i += 1;
            }

            if (i >= json.Length || json[i] != '[')
            {
                searchFrom = idx + token.Length;
                continue;
            }

            var depth = 0;
            var inString = false;
            var escape = false;
            for (var j = i; j < json.Length; j++)
            {
                var c = json[j];
                if (inString)
                {
                    if (escape)
                    {
                        escape = false;
                    }
                    else if (c == '\\')
                    {
                        escape = true;
                    }
                    else if (c == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == '[')
                {
                    depth += 1;
                }
                else if (c == ']')
                {
                    depth -= 1;
                    if (depth == 0)
                    {
                        return json.Substring(i, j - i + 1);
                    }
                }
            }

            return "";
        }

        return "";
    }

    public static List<string> ExtractStringArray(string json, string key)
    {
        var into = new List<string>();
        var arr = ExtractArray(json, key);
        if (string.IsNullOrEmpty(arr))
        {
            return into;
        }

        var cursor = 0;
        while (cursor < arr.Length)
        {
            var a = arr.IndexOf('"', cursor);
            if (a < 0)
            {
                break;
            }

            var b = a + 1;
            var escape = false;
            while (b < arr.Length)
            {
                var c = arr[b];
                if (escape)
                {
                    escape = false;
                    b += 1;
                    continue;
                }

                if (c == '\\')
                {
                    escape = true;
                    b += 1;
                    continue;
                }

                if (c == '"')
                {
                    break;
                }

                b += 1;
            }

            if (b >= arr.Length)
            {
                break;
            }

            var s = arr.Substring(a + 1, b - a - 1);
            if (s.Length > 0 && !s.Contains(":"))
            {
                into.Add(s);
            }

            cursor = b + 1;
        }

        return into;
    }

    public static HashSet<Vector2Int> ParseBlockedTiles(string mapJson)
    {
        return ParseXyArray(mapJson, "blocked");
    }

    public static HashSet<Vector2Int> ParseHazardTiles(string mapJson)
    {
        return ParseXyArray(mapJson, "hazards");
    }

    public struct MapProp
    {
        public int X;
        public int Y;
        public string Kind;
    }

    public static List<MapProp> ParseMapProps(string mapJson)
    {
        var list = new List<MapProp>();
        var part = SliceNamedArray(mapJson, "props");
        if (string.IsNullOrEmpty(part))
        {
            return list;
        }

        var cursor = 0;
        while (cursor < part.Length)
        {
            var brace = part.IndexOf('{', cursor);
            if (brace < 0)
            {
                break;
            }

            var end = part.IndexOf('}', brace);
            if (end < 0)
            {
                break;
            }

            var obj = part.Substring(brace, end - brace + 1);
            cursor = end + 1;
            if (!TryNumber(obj, "x", out var x) || !TryNumber(obj, "y", out var y))
            {
                continue;
            }

            var kind = ExtractString(obj, "kind");
            if (string.IsNullOrEmpty(kind))
            {
                kind = "rock";
            }

            list.Add(new MapProp
            {
                X = Mathf.RoundToInt(x),
                Y = Mathf.RoundToInt(y),
                Kind = kind,
            });
        }

        return list;
    }

    private static HashSet<Vector2Int> ParseXyArray(string mapJson, string key)
    {
        var set = new HashSet<Vector2Int>();
        var part = SliceNamedArray(mapJson, key);
        if (string.IsNullOrEmpty(part))
        {
            return set;
        }

        var cursor = 0;
        while (cursor < part.Length)
        {
            var brace = part.IndexOf('{', cursor);
            if (brace < 0)
            {
                break;
            }

            var end = part.IndexOf('}', brace);
            if (end < 0)
            {
                break;
            }

            var obj = part.Substring(brace, end - brace + 1);
            cursor = end + 1;
            if (TryNumber(obj, "x", out var x) && TryNumber(obj, "y", out var y))
            {
                set.Add(new Vector2Int(Mathf.FloorToInt(x + 0.5f), Mathf.FloorToInt(y + 0.5f)));
            }
        }

        return set;
    }

    private static string SliceNamedArray(string json, string key)
    {
        if (string.IsNullOrEmpty(json))
        {
            return "";
        }

        var idx = json.IndexOf("\"" + key + "\"");
        if (idx < 0)
        {
            return "";
        }

        var start = json.IndexOf('[', idx);
        if (start < 0)
        {
            return "";
        }

        var depth = 0;
        for (var i = start; i < json.Length; i++)
        {
            if (json[i] == '[')
            {
                depth += 1;
            }
            else if (json[i] == ']')
            {
                depth -= 1;
                if (depth == 0)
                {
                    return json.Substring(start, i - start + 1);
                }
            }
        }

        return "";
    }
}
