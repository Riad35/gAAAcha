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
        var blocked = new HashSet<Vector2Int>();
        var idx = mapJson.IndexOf("\"blocked\"");
        if (idx < 0)
        {
            return blocked;
        }

        var part = mapJson.Substring(idx);
        var cursor = 0;
        while (cursor < part.Length)
        {
            var xToken = part.IndexOf("\"x\":", cursor);
            if (xToken < 0)
            {
                break;
            }

            var yToken = part.IndexOf("\"y\":", xToken);
            if (yToken < 0 || yToken - xToken > 24)
            {
                cursor = xToken + 4;
                continue;
            }

            if (TryNumber(part.Substring(xToken, 20), "x", out var x) &&
                TryNumber(part.Substring(yToken, 20), "y", out var y))
            {
                blocked.Add(new Vector2Int(Mathf.RoundToInt(x), Mathf.RoundToInt(y)));
            }

            cursor = yToken + 4;
        }

        return blocked;
    }

    public static HashSet<Vector2Int> ParseHazardTiles(string mapJson)
    {
        var hazards = new HashSet<Vector2Int>();
        var idx = mapJson.IndexOf("\"hazards\"");
        if (idx < 0)
        {
            return hazards;
        }

        var part = mapJson.Substring(idx);
        var end = part.IndexOf(']');
        if (end > 0)
        {
            part = part.Substring(0, end + 1);
        }

        var cursor = 0;
        while (cursor < part.Length)
        {
            var xToken = part.IndexOf("\"x\":", cursor);
            if (xToken < 0)
            {
                break;
            }

            var yToken = part.IndexOf("\"y\":", xToken);
            if (yToken < 0 || yToken - xToken > 24)
            {
                cursor = xToken + 4;
                continue;
            }

            if (TryNumber(part.Substring(xToken, 20), "x", out var x) &&
                TryNumber(part.Substring(yToken, 20), "y", out var y))
            {
                hazards.Add(new Vector2Int(Mathf.RoundToInt(x), Mathf.RoundToInt(y)));
            }

            cursor = yToken + 4;
        }

        return hazards;
    }
}
