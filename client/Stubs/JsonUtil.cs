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
