using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 8-dir A* on the walkable tile grid. TileOf is floor(x+0.5) for path cells.
/// Move collision uses OverlapsSolid (strict cube interior), not round-to-tile.
/// </summary>
public static class MapPathing
{
    private static readonly int[] Dx = { 0, 1, 1, 1, 0, -1, -1, -1 };
    private static readonly int[] Dy = { 1, 1, 0, -1, -1, -1, 0, 1 };
    private static readonly int[] StepCost = { 10, 14, 10, 14, 10, 14, 10, 14 };

    public static int TileOf(float v)
    {
        return Mathf.FloorToInt(v + 0.5f);
    }

    /// <summary>
    /// True if (x,y) is strictly inside a 1×1 wall cube (or OOB). Faces are open so
    /// a floor corner at *.5 is not assigned to the diagonal wall (Math.round did that).
    /// </summary>
    public static bool OverlapsSolid(float x, float y, int mapW, int mapH, System.Func<int, int, bool> isWall)
    {
        const float inset = 0.5f - 1e-4f;
        var fx = Mathf.FloorToInt(x);
        var fy = Mathf.FloorToInt(y);
        for (var dy = 0; dy <= 1; dy++)
        {
            for (var dx = 0; dx <= 1; dx++)
            {
                var tx = fx + dx;
                var ty = fy + dy;
                if (Mathf.Abs(x - tx) >= inset || Mathf.Abs(y - ty) >= inset)
                {
                    continue;
                }

                if (tx < 0 || ty < 0 || tx >= mapW || ty >= mapH)
                {
                    return true;
                }

                if (isWall != null && isWall(tx, ty))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static void Depenetrate(ref float x, ref float y, int mapW, int mapH, System.Func<int, int, bool> isWall)
    {
        const float face = 0.5f;
        for (var n = 0; n < 4; n++)
        {
            var moved = false;
            var fx = Mathf.FloorToInt(x);
            var fy = Mathf.FloorToInt(y);
            for (var dy = 0; dy <= 1; dy++)
            {
                for (var dx = 0; dx <= 1; dx++)
                {
                    var tx = fx + dx;
                    var ty = fy + dy;
                    var solid = tx < 0 || ty < 0 || tx >= mapW || ty >= mapH
                        || (isWall != null && isWall(tx, ty));
                    if (!solid)
                    {
                        continue;
                    }

                    var ox = x - tx;
                    var oy = y - ty;
                    if (Mathf.Abs(ox) >= face - 1e-4f || Mathf.Abs(oy) >= face - 1e-4f)
                    {
                        continue;
                    }

                    if (Mathf.Abs(ox) >= Mathf.Abs(oy))
                    {
                        var sx = ox < 0f ? -1f : 1f;
                        x = tx + sx * face;
                    }
                    else
                    {
                        var sy = oy < 0f ? -1f : 1f;
                        y = ty + sy * face;
                    }

                    moved = true;
                }
            }

            if (!moved)
            {
                return;
            }
        }
    }

    public static bool Find(
        int sx, int sy, int tx, int ty,
        int mapW, int mapH,
        System.Func<int, int, bool> walkable,
        List<Vector2> path)
    {
        path.Clear();
        if (mapW <= 0 || mapH <= 0 || walkable == null)
        {
            return false;
        }

        sx = Mathf.Clamp(sx, 0, mapW - 1);
        sy = Mathf.Clamp(sy, 0, mapH - 1);
        if (!NearestWalkable(tx, ty, mapW, mapH, walkable, out tx, out ty))
        {
            return false;
        }

        if (sx == tx && sy == ty)
        {
            path.Add(new Vector2(tx, ty));
            return true;
        }

        var cells = mapW * mapH;
        var gScore = new int[cells];
        var parent = new int[cells];
        var closed = new bool[cells];
        for (var i = 0; i < cells; i++)
        {
            gScore[i] = int.MaxValue;
            parent[i] = -1;
        }

        var start = sy * mapW + sx;
        var goal = ty * mapW + tx;
        gScore[start] = 0;
        var open = new List<int>(64) { start };

        System.Func<int, int, bool> canStand = (x, y) =>
            (x == sx && y == sy) || walkable(x, y);

        while (open.Count > 0)
        {
            var bestI = 0;
            var bestF = int.MaxValue;
            for (var i = 0; i < open.Count; i++)
            {
                var iKey = open[i];
                var ix = iKey % mapW;
                var iy = iKey / mapW;
                var f = gScore[iKey] + Heuristic(ix, iy, tx, ty);
                if (f < bestF)
                {
                    bestF = f;
                    bestI = i;
                }
            }

            var cur = open[bestI];
            open.RemoveAt(bestI);
            if (cur == goal)
            {
                Reconstruct(parent, cur, mapW, path);
                return path.Count > 0;
            }

            closed[cur] = true;
            var cx = cur % mapW;
            var cy = cur / mapW;
            for (var d = 0; d < 8; d++)
            {
                var nx = cx + Dx[d];
                var ny = cy + Dy[d];
                if (nx < 0 || ny < 0 || nx >= mapW || ny >= mapH)
                {
                    continue;
                }

                if (!canStand(nx, ny))
                {
                    continue;
                }

                if (Dx[d] != 0 && Dy[d] != 0)
                {
                    if (!canStand(cx + Dx[d], cy) || !canStand(cx, cy + Dy[d]))
                    {
                        continue;
                    }
                }

                var nKey = ny * mapW + nx;
                if (closed[nKey])
                {
                    continue;
                }

                var tentative = gScore[cur] + StepCost[d];
                if (tentative >= gScore[nKey])
                {
                    continue;
                }

                parent[nKey] = cur;
                gScore[nKey] = tentative;
                if (!open.Contains(nKey))
                {
                    open.Add(nKey);
                }
            }
        }

        return false;
    }

    private static int Heuristic(int x, int y, int tx, int ty)
    {
        var dx = Mathf.Abs(x - tx);
        var dy = Mathf.Abs(y - ty);
        var diag = Mathf.Min(dx, dy);
        return diag * 14 + (dx + dy - 2 * diag) * 10;
    }

    private static bool NearestWalkable(
        int tx, int ty, int mapW, int mapH,
        System.Func<int, int, bool> walkable,
        out int ox, out int oy)
    {
        ox = tx;
        oy = ty;
        if (tx >= 0 && ty >= 0 && tx < mapW && ty < mapH && walkable(tx, ty))
        {
            return true;
        }

        for (var r = 1; r <= 8; r++)
        {
            for (var dy = -r; dy <= r; dy++)
            {
                for (var dx = -r; dx <= r; dx++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r)
                    {
                        continue;
                    }

                    var x = tx + dx;
                    var y = ty + dy;
                    if (x < 0 || y < 0 || x >= mapW || y >= mapH)
                    {
                        continue;
                    }

                    if (walkable(x, y))
                    {
                        ox = x;
                        oy = y;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static void Reconstruct(int[] parent, int cur, int mapW, List<Vector2> path)
    {
        var stack = new List<int>(32);
        while (cur >= 0)
        {
            stack.Add(cur);
            cur = parent[cur];
        }

        for (var i = stack.Count - 1; i >= 0; i--)
        {
            var key = stack[i];
            path.Add(new Vector2(key % mapW, key / mapW));
        }
    }

    private static void Smooth(List<Vector2> path, System.Func<int, int, bool> walkable)
    {
        if (path.Count < 3)
        {
            return;
        }

        var write = 1;
        var last = 0;
        for (var i = 2; i < path.Count; i++)
        {
            if (LineWalkable(path[last], path[i], walkable))
            {
                continue;
            }

            path[write] = path[i - 1];
            last = i - 1;
            write++;
        }

        path[write] = path[path.Count - 1];
        write++;
        if (write < path.Count)
        {
            path.RemoveRange(write, path.Count - write);
        }
    }

    private static bool LineWalkable(Vector2 a, Vector2 b, System.Func<int, int, bool> walkable)
    {
        var x0 = TileOf(a.x);
        var y0 = TileOf(a.y);
        var x1 = TileOf(b.x);
        var y1 = TileOf(b.y);
        var dx = Mathf.Abs(x1 - x0);
        var dy = Mathf.Abs(y1 - y0);
        var sx = x0 < x1 ? 1 : -1;
        var sy = y0 < y1 ? 1 : -1;
        var err = dx - dy;
        var x = x0;
        var y = y0;
        while (true)
        {
            if (!walkable(x, y))
            {
                return false;
            }

            if (x == x1 && y == y1)
            {
                return true;
            }

            var e2 = 2 * err;
            var stepX = e2 > -dy;
            var stepY = e2 < dx;
            if (stepX && stepY && (!walkable(x + sx, y) || !walkable(x, y + sy)))
            {
                return false;
            }

            if (stepX)
            {
                err -= dy;
                x += sx;
            }

            if (stepY)
            {
                err += dx;
                y += sy;
            }
        }
    }
}
