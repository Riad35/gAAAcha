using UnityEngine;

/// <summary>
/// Server tiles are cartesian (x, y). Unity world is XZ with Y-up.
/// Dimetric home pose: pitch 30° from horizontal, yaw 45° → 1×1 tile reads 2:1 (128×64 px at 1080p).
/// </summary>
public static class WorldCoords
{
    public const float WallHeight = 1.35f;
    public const float SpriteWorldHeight = 1.64f;
    public const float DefaultYaw = 45f;
    public const float PitchFromHorizontal = 30f;
    public const float CamDistance = 14f;
    public const float TileHeightPx = 64f;
    public const float TileWidthPx = 128f;

    /// <summary>Ground-diamond height of a 1×1 XZ tile at 30°/45° dimetric: √2 · sin(30°).</summary>
    public const float TileDiamondWorldH = 0.70710678f;

    public static Vector3 TileToWorld(float tileX, float tileY, float height = 0f)
    {
        return new Vector3(tileX, height, tileY);
    }

    public static Vector2 WorldToTile(Vector3 world)
    {
        return new Vector2(world.x, world.z);
    }

    public static Vector2 MapXZ(Vector3 world)
    {
        return new Vector2(world.x, world.z);
    }

    public static float MapDistance(Vector3 a, Vector3 b)
    {
        return Vector2.Distance(MapXZ(a), MapXZ(b));
    }

    public static float MapDistance(Vector2 mapA, Vector3 worldB)
    {
        return Vector2.Distance(mapA, MapXZ(worldB));
    }

    public static Vector3 OnGround(Vector3 world)
    {
        return new Vector3(world.x, 0f, world.z);
    }

    public static Vector3 Lift(Vector3 world, float height)
    {
        return new Vector3(world.x, height, world.z);
    }

    public static Vector3 AlongMap(Vector3 origin, Vector2 mapDir, float dist)
    {
        return new Vector3(origin.x + mapDir.x * dist, origin.y, origin.z + mapDir.y * dist);
    }

    public static Vector3 MapDir3(Vector2 mapDir)
    {
        return new Vector3(mapDir.x, 0f, mapDir.y);
    }

    /// <summary>
    /// Ortho half-height so a tile diamond is <paramref name="tileHeightPx"/> tall
    /// (and 2× that wide) at the home dimetric yaw.
    /// </summary>
    public static float OrthoSizeForTilePixels(float screenHeightPx, float tileHeightPx = TileHeightPx)
    {
        var pxPerWorld = tileHeightPx / TileDiamondWorldH;
        return Mathf.Max(1f, screenHeightPx / (2f * pxPerWorld));
    }
}
