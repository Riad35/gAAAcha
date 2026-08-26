using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// Map + entities with soft tile lerp and edge-only camera.
/// </summary>
public sealed class GrayBoxWorld : MonoBehaviour
{
    private const float MoveDuration = 0.15f;
    private const float MinMoveDur = 0.06f;
    private const float MaxMoveDur = 0.85f;
    private const float LongRange = 999f;
    private const float CamPitchFromHorizontal = WorldCoords.PitchFromHorizontal;
    private const float CamDistance = WorldCoords.CamDistance;
    private const float JustMovedSec = 0.12f;
    private const float CastFxDedupSec = 1.14f;

    private readonly Dictionary<string, EntityView> _entities = new Dictionary<string, EntityView>();
    private Transform _root;
    private Transform _heldMark;
    private SpriteRenderer _heldMarkSr;
    private string _heldWeaponId = "";
    private Transform _lockRing;
    private SpriteRenderer _lockRingSr;
    private Sprite[] _lockFrames;
    private float _lockAnimT;
    private Camera _cam;
    private float _camYaw = WorldCoords.DefaultYaw;
    private Light _isoSun;
    private static readonly Dictionary<string, Material> _tileMats = new Dictionary<string, Material>();
    private float _rmbLastX;
    private bool _rmbDragging;
    private string _selfId = "local_you";
    private string _lockTargetId = "";
    private string _castFxKey = "";
    private float _castFxUntil;
    private readonly List<FloatText> _floats = new List<FloatText>();
    private readonly Dictionary<string, BoltView> _projectiles = new Dictionary<string, BoltView>();
    private readonly Dictionary<string, List<Transform>> _statusMarkers = new Dictionary<string, List<Transform>>();
    private readonly List<TempFx> _tempFx = new List<TempFx>();
    private readonly List<Transform> _aimParts = new List<Transform>();
    private int _aimMode; // 0 none, 1 linear, 2 cone, 3 ground
    private Vector2 _aimDir = Vector2.right;
    private Vector3 _aimPoint;
    private float _aimRange;
    private float _aimWidth;
    private float _aimAngleDeg;
    private float _aimCastRange;
    private float _aimAoeRadius;
    private int _mapW = 20;
    private int _mapH = 12;
    private string _mapId = "town_ashen";
    private string _classId = "adventurer";
    private readonly HashSet<Vector2Int> _blockedTiles = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> _hazardTiles = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> _propTiles = new HashSet<Vector2Int>();

    private struct EntityView
    {
        public Transform Transform;
        public Transform HitRing;
        public SpriteRenderer Renderer;
        public Color BaseColor;
        public int Hp;
        public int MaxHp;
        public int Mp;
        public int MaxMp;
        public float HitRadius;
        public string Label;
        public string Kind;
        public float ThreatSelfPct;
        public string ThreatTopId;
        public List<string> StatusKinds;
        public List<float> StatusUntil;
        public Vector3 From;
        public Vector3 To;
        public float MoveT;
        public float MoveDur;
        public float MoveSpeed;
        public bool Moving;
        public SpriteCatalog.Clip AnimClip;
        public int AnimFrame;
        public float AnimTime;
        public bool UsesArt;
        public int Facing;
        public float FaceX;
        public float FaceZ;
        public float JustMovedUntil;
        public float AnimLockUntil;
        public bool AnimOneShot;
        public bool Dying;
        public float DeathRemoveAt;
        public int AnimFacingRow;
        public float HurtTintUntil;
        public string AnimVariant;
        public bool Resting;
        public SpriteRenderer Shadow;
        public int BlankTicks;
    }

    private struct FloatText
    {
        public Vector3 World;
        public string Text;
        public float Until;
        public Color Color;
        public int Size;
    }

    private struct TempFx
    {
        public GameObject Go;
        public SpriteRenderer Sr;
        public float Born;
        public float Until;
        public Vector3 Scale0;
        public Vector3 Scale1;
        public Color Color0;
        public Color Color1;
        public float SpinDeg;
        public float FaceZ;
        public string FollowId;
        public Vector3 FollowOff;
        public Sprite[] Frames;
        public float Fps;
        public bool Loop;
    }

    private struct BoltView
    {
        public Transform Transform;
        public SpriteRenderer Renderer;
        public Vector2 Vel;
        public float Speed;
        public string SkillId;
        public string TargetId;
        public string CasterId;
        public Color Color;
        public Sprite[] Frames;
        public float AnimT;
    }

    public struct TargetInfo
    {
        public string Id;
        public string Label;
        public string Kind;
        public int Hp;
        public int MaxHp;
        public int Mp;
        public int MaxMp;
        public float ThreatSelf;
        public string ThreatTopId;
        public string[] StatusKinds;
    }

    public string SelfId => _selfId;
    public string LockTargetId => _lockTargetId;
    public bool IsSelfMoving => _entities.TryGetValue(_selfId, out var v) && v.Moving;
    public float CameraYaw => _camYaw;

    public void Boot(int mapW = 20, int mapH = 12)
    {
        _mapW = mapW;
        _mapH = mapH;
        var auditN = SpriteCatalog.Warmup();
        if (auditN > 0)
        {
            GameLog.Warn(GameLog.Channel.Gfx, "sprite-audit boot issues=" + auditN);
        }

        _root = new GameObject("GrayBoxWorld").transform;
        _cam = Camera.main ?? Object.FindAnyObjectByType<Camera>();
        SetupCamera();
        SpawnTileHints();
        EnsureLockRing();
        Upsert(_selfId, 2f, 6f, new Color(0.15f, 0.85f, 1f), "You", 100, 100, 0.4f, true);
        SetEntityMeta(_selfId, "player", 50, 50);
        Upsert("monster_slime_1", 4f, 6f, new Color(0.2f, 1f, 0.25f), "Slime", 40, 40, 0.45f, true);
        SetEntityMeta("monster_slime_1", "monster", 0, 0);
        if (!SpriteCatalog.HasArtSprite("player_local", "player"))
        {
            GameLog.Warn(GameLog.Channel.Gfx,
                "reason=player_sheets_missing  fallback=shape");
        }
        else
        {
            GameLog.Info(GameLog.Channel.Gfx, "player sheets OK");
        }
        SetLockTarget("monster_slime_1");
        CenterCamera(3f, 6f);
    }

    public string MapId => _mapId;
    public int MapWidth => _mapW;
    public int MapHeight => _mapH;

    public bool InMapBounds(int tx, int ty)
    {
        return tx >= 0 && ty >= 0 && tx < _mapW && ty < _mapH;
    }

    public bool IsWallCell(int tx, int ty)
    {
        return _blockedTiles.Contains(new Vector2Int(tx, ty));
    }

    public bool IsHazardCell(int tx, int ty)
    {
        return _hazardTiles.Contains(new Vector2Int(tx, ty));
    }

    public bool IsPropCell(int tx, int ty)
    {
        return _propTiles.Contains(new Vector2Int(tx, ty));
    }

    public struct MinimapMark
    {
        public float X;
        public float Y;
        public string Kind;
    }

    public void CopyMinimapMarks(List<MinimapMark> into)
    {
        if (into == null)
        {
            return;
        }

        into.Clear();
        foreach (var pair in _entities)
        {
            var view = pair.Value;
            if (view.Transform == null)
            {
                continue;
            }

            var kind = view.Kind ?? "";
            if (pair.Key == _selfId)
            {
                kind = "self";
            }
            else if (kind == "monster" || pair.Key.StartsWith("monster_"))
            {
                if (view.Hp <= 0)
                {
                    continue;
                }

                kind = "monster";
            }
            else if (kind == "portal" || pair.Key.StartsWith("portal_"))
            {
                kind = "portal";
            }
            else if (kind == "npc" || pair.Key.StartsWith("npc_"))
            {
                kind = "npc";
            }
            else if (kind == "player" || pair.Key.StartsWith("player_"))
            {
                kind = "player";
            }
            else
            {
                continue;
            }

            var p = WorldCoords.MapXZ(view.Transform.position);
            into.Add(new MinimapMark { X = p.x, Y = p.y, Kind = kind });
        }
    }

    /// <summary>True if (x,y) is strictly inside a wall cube (same as server isSolidAt).</summary>
    public bool IsTileBlocked(float x, float y)
    {
        return MapPathing.OverlapsSolid(x, y, _mapW, _mapH, IsWallCell);
    }

    public void Depenetrate(ref float x, ref float y)
    {
        MapPathing.Depenetrate(ref x, ref y, _mapW, _mapH, IsWallCell);
    }

    public bool IsWalkableCell(int tx, int ty)
    {
        return InMapBounds(tx, ty) && !_blockedTiles.Contains(new Vector2Int(tx, ty));
    }

    /// <summary>A* tile path. Dest snaps to the nearest walkable cell. Start may be a wall (already standing there).</summary>
    public bool TryFindPath(float sx, float sy, float tx, float ty, List<Vector2> path)
    {
        if (path == null || _mapW <= 0 || _mapH <= 0)
        {
            return false;
        }

        var ox = MapPathing.TileOf(sx);
        var oy = MapPathing.TileOf(sy);
        return MapPathing.Find(
            ox, oy,
            MapPathing.TileOf(tx), MapPathing.TileOf(ty),
            _mapW, _mapH,
            (x, y) =>
            {
                if (!IsWalkableCell(x, y))
                {
                    return false;
                }

                if (x == ox && y == oy)
                {
                    return true;
                }

                return !WouldOverlapCombatant(x, y);
            },
            path);
    }

    /// <summary>True if moving to (x,y) is blocked by a wall, another player, or an NPC.</summary>
    public bool WouldBlockLocalMove(float x, float y)
    {
        return IsTileBlocked(x, y) || WouldOverlapCombatant(x, y);
    }

    public void RebuildMap(int mapW, int mapH, HashSet<Vector2Int> blocked, string mapId = null,
        HashSet<Vector2Int> hazards = null, List<JsonUtil.MapProp> props = null)
    {
        _mapW = mapW;
        _mapH = mapH;
        if (!string.IsNullOrEmpty(mapId) && mapId != _mapId)
        {
            _mapId = mapId;
            ClearLockTarget();
        }
        else if (!string.IsNullOrEmpty(mapId))
        {
            _mapId = mapId;
        }

        _blockedTiles.Clear();
        if (blocked != null)
        {
            foreach (var cell in blocked)
            {
                _blockedTiles.Add(cell);
            }
        }

        _hazardTiles.Clear();
        if (hazards != null)
        {
            foreach (var cell in hazards)
            {
                _hazardTiles.Add(cell);
            }
        }

        _propTiles.Clear();
        if (props != null)
        {
            for (var i = 0; i < props.Count; i++)
            {
                _propTiles.Add(new Vector2Int(props[i].X, props[i].Y));
            }
        }

        // Destroy old tiles under root named tile/wall/hazard
        if (_root != null)
        {
            for (var i = _root.childCount - 1; i >= 0; i--)
            {
                var c = _root.GetChild(i);
                if (c.name == "tile" || c.name == "wall" || c.name == "hazard" || c.name == "prop")
                {
                    Destroy(c.gameObject);
                }
            }
        }

        SpawnTileHints(blocked, hazards);
        SpawnMapProps(props);
        SoundCatalog.PlayMap(_mapId);
        var wallN = blocked?.Count ?? 0;
        var hazN = hazards?.Count ?? 0;
        var propN = props?.Count ?? 0;
        GameLog.Info(GameLog.Channel.World,
            "rebuild  map=" + _mapId + "  size=" + _mapW + "x" + _mapH +
            "  walls=" + wallN + "  hazards=" + hazN + "  props=" + propN);
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

        if (json.Contains("\"type\":\"sync_vitals\""))
        {
            ApplyVitals(json);
            return;
        }

        if (json.Contains("\"type\":\"sync_skill\""))
        {
            ApplySkill(json);
            return;
        }

        if (json.Contains("\"type\":\"sync_aoe\""))
        {
            ApplyAoe(json);
            return;
        }

        if (json.Contains("\"type\":\"sync_despawn\""))
        {
            var reason = JsonUtil.ExtractString(json, "reason");
            Despawn(JsonUtil.ExtractString(json, "entityId"), reason != "leave");
            return;
        }

        if (json.Contains("\"type\":\"sync_spawn\""))
        {
            ApplySpawn(json);
            return;
        }

        if (json.Contains("\"type\":\"sync_projectile_spawn\""))
        {
            ApplyProjectileSpawn(json);
            return;
        }

        if (json.Contains("\"type\":\"sync_projectile_move\""))
        {
            ApplyProjectileMove(json);
            return;
        }

        if (json.Contains("\"type\":\"sync_projectile_despawn\""))
        {
            DespawnProjectile(JsonUtil.ExtractString(json, "id"));
            return;
        }

        if (json.Contains("\"type\":\"sync_threat\""))
        {
            ApplyThreat(json);
            return;
        }

        if (json.Contains("\"type\":\"sync_status\""))
        {
            ApplyStatus(json);
            return;
        }

        if (json.Contains("\"type\":\"sync_fx\""))
        {
            PlaySyncFx(json);
        }
    }

    public void SetLocalPos(float x, float y, bool instant = false)
    {
        if (instant)
        {
            Upsert(_selfId, x, y, new Color(0.15f, 0.85f, 1f), "You",
                GetHp(_selfId, 100), GetMaxHp(_selfId, 100), GetHitRadius(_selfId, 0.4f), true);
            return;
        }

        MoveTo(_selfId, x, y, new Color(0.15f, 0.85f, 1f), "You", GetHp(_selfId, 100), GetMaxHp(_selfId, 100), GetHitRadius(_selfId, 0.4f));
    }

    public void ApplyReconcileHp(string entityId, int hp, bool hard)
    {
        if (string.IsNullOrEmpty(entityId) || !_entities.TryGetValue(entityId, out var view))
        {
            return;
        }

        // Predicted hits pass hp >= 1. Server 0 must apply or corpses freeze at 1 HP.
        if (!hard && hp > 0)
        {
            view.Hp = hp;
            _entities[entityId] = view;
            return;
        }

        view.Hp = hp;
        _entities[entityId] = view;
        if (hp <= 0)
        {
            BeginDeath(entityId);
        }
    }

    public string PickEntityNear(float worldX, float worldY, float tolerance)
    {
        string best = "";
        var bestDist = float.MaxValue;
        foreach (var pair in _entities)
        {
            if (pair.Key == _selfId || pair.Value.Transform == null || pair.Value.Hp <= 0)
            {
                continue;
            }

            var d = WorldCoords.MapDistance(new Vector2(worldX, worldY), pair.Value.Transform.position);
            var reach = pair.Value.HitRadius + tolerance;
            if (d <= reach && d < bestDist)
            {
                bestDist = d;
                best = pair.Key;
            }
        }

        return best;
    }

    /// <summary>Click-target: nearest combatant under the cursor (skips NPCs / portals).</summary>
    public string PickCombatTargetNear(float worldX, float worldY, float tolerance)
    {
        string best = "";
        var bestDist = float.MaxValue;
        foreach (var pair in _entities)
        {
            if (pair.Key == _selfId || pair.Value.Transform == null || pair.Value.Hp <= 0)
            {
                continue;
            }

            if (!IsCombatTargetKind(pair.Value.Kind, pair.Key))
            {
                continue;
            }

            var d = WorldCoords.MapDistance(new Vector2(worldX, worldY), pair.Value.Transform.position);
            var reach = Mathf.Max(0.7f, pair.Value.HitRadius) + tolerance;
            if (d <= reach && d < bestDist)
            {
                bestDist = d;
                best = pair.Key;
            }
        }

        return best;
    }

    /// <summary>Nearest NPC under the cursor (not combatants / portals).</summary>
    public string PickNpcNear(float worldX, float worldY, float tolerance)
    {
        string best = "";
        var bestDist = float.MaxValue;
        foreach (var pair in _entities)
        {
            if (pair.Value.Transform == null)
            {
                continue;
            }

            if (!IsNpcEntity(pair.Value.Kind, pair.Key))
            {
                continue;
            }

            var d = WorldCoords.MapDistance(new Vector2(worldX, worldY), pair.Value.Transform.position);
            var reach = Mathf.Max(0.7f, pair.Value.HitRadius) + tolerance;
            if (d <= reach && d < bestDist)
            {
                bestDist = d;
                best = pair.Key;
            }
        }

        return best;
    }

    private static bool IsNpcEntity(string kind, string id)
    {
        if (kind == "npc")
        {
            return true;
        }

        return !string.IsNullOrEmpty(id) && id.StartsWith("npc_");
    }

    public string PickPortalNear(float worldX, float worldY, float tolerance)
    {
        string best = "";
        var bestDist = float.MaxValue;
        foreach (var pair in _entities)
        {
            if (pair.Value.Transform == null)
            {
                continue;
            }

            var id = pair.Key;
            if (pair.Value.Kind != "portal" && (string.IsNullOrEmpty(id) || !id.StartsWith("portal_")))
            {
                continue;
            }

            var d = WorldCoords.MapDistance(new Vector2(worldX, worldY), pair.Value.Transform.position);
            if (d <= 0.9f + tolerance && d < bestDist)
            {
                bestDist = d;
                best = id;
            }
        }

        return best;
    }

    private static bool IsCombatTargetKind(string kind, string id)
    {
        if (!string.IsNullOrEmpty(id) && id.StartsWith("portal_"))
        {
            return false;
        }

        if (kind == "portal" || kind == "npc")
        {
            return false;
        }

        if (kind == "monster" || kind == "player")
        {
            return true;
        }

        // Kind sometimes missing after parse — fall back to id prefix.
        if (!string.IsNullOrEmpty(id) &&
            (id.StartsWith("monster") || id.StartsWith("lab_") || id.Contains("slime") ||
             id.Contains("dummy") || id.Contains("ragdoll") || id.Contains("cannon") ||
             id.StartsWith("player_")))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Closest living non-self entity whose edge-to-edge gap is within attackRange.
    /// </summary>
    public string FindClosestEnemyInRange(float attackRange)
    {
        Vector2 origin = Vector2.zero;
        var selfR = 0.4f;
        if (_entities.TryGetValue(_selfId, out var self) && self.Transform != null)
        {
            origin = WorldCoords.MapXZ(self.Transform.position);
            selfR = self.HitRadius > 0f ? self.HitRadius : 0.4f;
        }
        else
        {
            return FindClosestCombatTarget(attackRange + 2f);
        }

        string best = "";
        var bestGap = float.MaxValue;
        foreach (var pair in _entities)
        {
            if (pair.Key == _selfId || pair.Value.Transform == null || pair.Value.Hp <= 0)
            {
                continue;
            }

            if (!IsCombatTargetKind(pair.Value.Kind, pair.Key))
            {
                continue;
            }

            if (pair.Value.Kind == "player" || pair.Key.StartsWith("player_"))
            {
                continue;
            }

            var otherR = pair.Value.HitRadius > 0f ? pair.Value.HitRadius : 0.4f;
            var center = Vector2.Distance(origin, WorldCoords.MapXZ(pair.Value.Transform.position));
            var gap = Mathf.Max(0f, center - selfR - otherR);
            if (gap <= attackRange + 0.05f && gap < bestGap)
            {
                bestGap = gap;
                best = pair.Key;
            }
        }

        return best;
    }

    public string FindClosestInLongRange(float maxRange = 10f)
    {
        return FindClosestCombatTarget(maxRange);
    }

    public string FindClosestCombatTarget(float maxRange = LongRange)
    {
        var monster = FindClosestOfKind("monster", maxRange);
        if (!string.IsNullOrEmpty(monster))
        {
            return monster;
        }

        return FindClosestOfKind("player", maxRange);
    }

    public string FindClosestPlayer(float maxRange = LongRange)
    {
        return FindClosestOfKind("player", maxRange);
    }

    public bool IsPlayerEntity(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return false;
        }

        if (id == _selfId)
        {
            return true;
        }

        if (!_entities.TryGetValue(id, out var view))
        {
            return id.StartsWith("player_");
        }

        if (view.Kind == "player")
        {
            return true;
        }

        return !string.IsNullOrEmpty(id) && id.StartsWith("player_");
    }

    public bool IsMonsterEntity(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return false;
        }

        if (!_entities.TryGetValue(id, out var view))
        {
            return id.StartsWith("monster") || id.StartsWith("lab_");
        }

        if (view.Kind == "monster")
        {
            return true;
        }

        return id.StartsWith("monster") || id.StartsWith("lab_");
    }

    private string FindClosestOfKind(string kind, float maxRange)
    {
        Vector2 origin = Vector2.zero;
        var hasOrigin = false;
        if (_entities.TryGetValue(_selfId, out var self) && self.Transform != null)
        {
            origin = WorldCoords.MapXZ(self.Transform.position);
            hasOrigin = true;
        }

        string best = "";
        var bestDist = float.MaxValue;

        foreach (var pair in _entities)
        {
            if (pair.Key == _selfId || pair.Value.Transform == null || pair.Value.Hp <= 0)
            {
                continue;
            }

            if (!IsCombatTargetKind(pair.Value.Kind, pair.Key))
            {
                continue;
            }

            var isPlayer = pair.Value.Kind == "player" || pair.Key.StartsWith("player_");
            if (kind == "monster" && isPlayer)
            {
                continue;
            }

            if (kind == "player" && !isPlayer)
            {
                continue;
            }

            var pos = WorldCoords.MapXZ(pair.Value.Transform.position);
            var d = hasOrigin ? Vector2.Distance(origin, pos) : pos.sqrMagnitude;
            if (d <= maxRange && d < bestDist)
            {
                bestDist = d;
                best = pair.Key;
            }
        }

        return best;
    }

    public int CountCombatTargets()
    {
        var n = 0;
        foreach (var pair in _entities)
        {
            if (pair.Key == _selfId || pair.Value.Transform == null || pair.Value.Hp <= 0)
            {
                continue;
            }

            if (IsCombatTargetKind(pair.Value.Kind, pair.Key))
            {
                n++;
            }
        }

        return n;
    }

    public void SetLockTarget(string id)
    {
        if (string.IsNullOrEmpty(id) || !_entities.ContainsKey(id))
        {
            return;
        }

        _lockTargetId = id;
        UpdateLockRing();
    }

    public void ClearLockTarget()
    {
        _lockTargetId = "";
        if (_lockRing != null)
        {
            _lockRing.gameObject.SetActive(false);
        }
    }

    /// <summary>Lock the closest living combat target (monsters / players, not NPCs/portals).</summary>
    public string LockClosestEnemy(float maxRange = LongRange)
    {
        var closest = FindClosestCombatTarget(maxRange);
        if (string.IsNullOrEmpty(closest))
        {
            ClearLockTarget();
            return "";
        }

        SetLockTarget(closest);
        return closest;
    }

    /// <summary>Tab toggle: clear current lock, or acquire closest foe.</summary>
    public string ToggleLockClosest(float maxRange = LongRange)
    {
        if (!string.IsNullOrEmpty(_lockTargetId) && IsLiving(_lockTargetId))
        {
            ClearLockTarget();
            return "";
        }

        return LockClosestEnemy(maxRange);
    }

    /// <summary>Tab cycle: next living foe by distance (wraps). Empty lock acquires closest.</summary>
    public string CycleLockTarget()
    {
        var foes = BuildLivingFoesSortedByDist();
        if (foes.Count == 0)
        {
            ClearLockTarget();
            return "";
        }

        var idx = foes.IndexOf(_lockTargetId);
        var next = idx < 0 ? 0 : (idx + 1) % foes.Count;
        SetLockTarget(foes[next]);
        return foes[next];
    }

    public float DistanceSelfTo(string id)
    {
        if (string.IsNullOrEmpty(id) ||
            !_entities.TryGetValue(_selfId, out var self) || self.Transform == null ||
            !_entities.TryGetValue(id, out var other) || other.Transform == null)
        {
            return float.MaxValue;
        }

        var a = self.Transform.position;
        var b = other.Transform.position;
        return WorldCoords.MapDistance(a, b);
    }

    /// <summary>Edge-to-edge gap (mirrors server rangeGap).</summary>
    public float RangeGapTo(string id, float fromX, float fromY)
    {
        if (string.IsNullOrEmpty(id) || !_entities.TryGetValue(id, out var other) || other.Transform == null)
        {
            return float.MaxValue;
        }

        var selfR = 0.4f;
        if (_entities.TryGetValue(_selfId, out var self) && self.HitRadius > 0f)
        {
            selfR = self.HitRadius;
        }

        var otherR = other.HitRadius > 0f ? other.HitRadius : 0.4f;
        var center = WorldCoords.MapDistance(new Vector2(fromX, fromY), other.Transform.position);
        return Mathf.Max(0f, center - selfR - otherR);
    }

    public float RangeGapTo(string id)
    {
        if (!_entities.TryGetValue(_selfId, out var self) || self.Transform == null)
        {
            return float.MaxValue;
        }

        return RangeGapTo(id, self.Transform.position.x, self.Transform.position.z);
    }

    public float SelfHitRadius()
    {
        if (_entities.TryGetValue(_selfId, out var self) && self.HitRadius > 0f)
        {
            return self.HitRadius;
        }

        return 0.4f;
    }

    /// <summary>True if (x,y) would overlap another living combatant (mirrors server entityBlockedAt).</summary>
    public bool WouldOverlapCombatant(float x, float y)
    {
        var selfR = SelfHitRadius();
        foreach (var pair in _entities)
        {
            if (pair.Key == _selfId || pair.Value.Transform == null || pair.Value.Hp <= 0)
            {
                continue;
            }

            if (!IsCombatTargetKind(pair.Value.Kind, pair.Key) && pair.Value.Kind != "npc")
            {
                continue;
            }

            // NPCs sit on floor pads — walking through them is allowed (same as server).
            if (pair.Value.Kind == "npc"
                || (!string.IsNullOrEmpty(pair.Key) && pair.Key.StartsWith("npc_")))
            {
                continue;
            }

            // Monster–monster ignored on server; player may stand on monsters.
            if (pair.Value.Kind == "monster"
                || (!string.IsNullOrEmpty(pair.Key) && pair.Key.StartsWith("monster_")))
            {
                continue;
            }
            var otherR = pair.Value.HitRadius > 0f ? pair.Value.HitRadius : 0.4f;
            var d = Vector2.Distance(
                new Vector2(x, y),
                WorldCoords.MapXZ(pair.Value.Transform.position));
            if (d < selfR + otherR - 0.02f)
            {
                return true;
            }
        }

        return false;
    }

    public bool IsLivingTarget(string id)
    {
        return IsLiving(id);
    }

    public bool TryGetMapXY(string id, out float x, out float y)
    {
        x = 0f;
        y = 0f;
        if (string.IsNullOrEmpty(id) || !_entities.TryGetValue(id, out var view) || view.Transform == null)
        {
            return false;
        }

        x = view.Transform.position.x;
        y = view.Transform.position.z;
        return true;
    }

    public bool TryAutoLockFromThreat(string selfId)
    {
        if (!string.IsNullOrEmpty(_lockTargetId) && IsLiving(_lockTargetId))
        {
            return false;
        }

        if (!_entities.TryGetValue(selfId, out var self) || self.Transform == null)
        {
            return false;
        }

        string best = "";
        var bestDist = float.MaxValue;
        var selfPos = self.Transform.position;

        foreach (var pair in _entities)
        {
            if (pair.Value.Kind != "monster" || pair.Value.ThreatSelfPct <= 0f ||
                pair.Value.Transform == null || pair.Value.Hp <= 0)
            {
                continue;
            }

            var d = WorldCoords.MapDistance(selfPos, pair.Value.Transform.position);
            if (d < bestDist)
            {
                bestDist = d;
                best = pair.Key;
            }
        }

        if (string.IsNullOrEmpty(best))
        {
            return false;
        }

        SetLockTarget(best);
        return true;
    }

    public bool TryGetTargetInfo(out TargetInfo info)
    {
        info = default;
        if (string.IsNullOrEmpty(_lockTargetId) || !_entities.TryGetValue(_lockTargetId, out var view))
        {
            return false;
        }

        var kinds = view.StatusKinds;
        var arr = kinds != null && kinds.Count > 0 ? kinds.ToArray() : System.Array.Empty<string>();
        info = new TargetInfo
        {
            Id = _lockTargetId,
            Label = view.Label ?? "",
            Kind = view.Kind ?? "",
            Hp = view.Hp,
            MaxHp = view.MaxHp,
            Mp = view.Mp,
            MaxMp = view.MaxMp,
            ThreatSelf = view.ThreatSelfPct,
            ThreatTopId = view.ThreatTopId ?? "",
            StatusKinds = arr,
        };
        return true;
    }

    public Vector3? GetEntityWorldPos(string id)
    {
        if (string.IsNullOrEmpty(id) || !_entities.TryGetValue(id, out var view) || view.Transform == null)
        {
            return null;
        }

        return view.Transform.position;
    }

    /// <summary>
    /// Place FX on the entity, lifted off the ground so it draws in front of the body.
    /// </summary>
    private Vector3 FxAtEntity(string id, float headLift = 0f)
    {
        if (!string.IsNullOrEmpty(id) && _entities.TryGetValue(id, out var view) && view.Transform != null)
        {
            return FxAtWorld(view.Transform.position, headLift);
        }

        var pos = GetEntityWorldPos(id) ?? Vector3.zero;
        return FxAtWorld(pos, headLift);
    }

    private Vector3 FxAtWorld(Vector3 entityPos, float headLift = 0f)
    {
        var p = WorldCoords.Lift(entityPos, 0.55f + headLift);
        return p;
    }

    public void ClearAimIndicator()
    {
        for (var i = 0; i < _aimParts.Count; i++)
        {
            if (_aimParts[i] != null)
            {
                Destroy(_aimParts[i].gameObject);
            }
        }

        _aimParts.Clear();
        _aimMode = 0;
    }

    public void UpdateLinearAim(Vector3 caster, Vector2 dir, float range, float width)
    {
        if (dir.sqrMagnitude < 0.0001f)
        {
            dir = Vector2.right;
        }
        else
        {
            dir.Normalize();
        }

        _aimMode = 1;
        _aimDir = dir;
        _aimRange = Mathf.Max(0.1f, range);
        _aimWidth = Mathf.Max(0.05f, width);
        EnsureAimPartCount(1);
        PlaceLinearBeam(_aimParts[0], caster, _aimDir, _aimRange, _aimWidth);
    }

    public void UpdateConeAim(Vector3 caster, Vector2 dir, float range, float angleDeg)
    {
        if (dir.sqrMagnitude < 0.0001f)
        {
            dir = Vector2.right;
        }
        else
        {
            dir.Normalize();
        }

        _aimMode = 2;
        _aimDir = dir;
        _aimRange = Mathf.Max(0.1f, range);
        _aimAngleDeg = Mathf.Max(1f, angleDeg);
        EnsureAimPartCount(3);

        var half = _aimAngleDeg * 0.5f * Mathf.Deg2Rad;
        var left = RotateDir(_aimDir, half);
        var right = RotateDir(_aimDir, -half);
        const float edgeW = 0.08f;
        PlaceLinearBeam(_aimParts[0], caster, left, _aimRange, edgeW);
        PlaceLinearBeam(_aimParts[1], caster, right, _aimRange, edgeW);
        PlaceLinearBeam(_aimParts[2], caster, _aimDir, _aimRange, Mathf.Max(0.25f, _aimRange * 0.35f));
        SetAimPartColor(_aimParts[2], new Color(0.35f, 0.95f, 0.55f, 0.22f));
    }

    public void UpdateGroundAim(Vector3 caster, Vector3 point, float castRange, float aoeRadius)
    {
        _aimMode = 3;
        _aimCastRange = Mathf.Max(0.1f, castRange);
        _aimAoeRadius = Mathf.Max(0.1f, aoeRadius);

        var flat = WorldCoords.MapXZ(point) - WorldCoords.MapXZ(caster);
        if (flat.magnitude > _aimCastRange)
        {
            flat = flat.normalized * _aimCastRange;
        }

        _aimPoint = new Vector3(caster.x + flat.x, 0.04f, caster.z + flat.y);
        EnsureAimPartCount(2);

        var reticle = _aimParts[0];
        var d = _aimAoeRadius * 2f;
        reticle.position = _aimPoint;
        reticle.localScale = new Vector3(d, 0.025f, d);
        reticle.rotation = Quaternion.identity;
        SetAimPartColor(reticle, new Color(0.4f, 1f, 0.55f, 0.4f));

        var ring = _aimParts[1];
        var rd = _aimCastRange * 2f;
        ring.position = new Vector3(caster.x, 0.03f, caster.z);
        ring.localScale = new Vector3(rd, 0.02f, rd);
        ring.rotation = Quaternion.identity;
        SetAimPartColor(ring, new Color(0.5f, 0.9f, 1f, 0.18f));
    }

    public void DrawOverlays()
    {
        if (_cam == null)
        {
            return;
        }

        foreach (var pair in _entities)
        {
            var view = pair.Value;
            if (view.Transform == null)
            {
                continue;
            }

            var screen = _cam.WorldToScreenPoint(FxAtWorld(view.Transform.position, 0.55f));
            if (screen.z < 0f)
            {
                continue;
            }

            var gui = UiChrome.ScreenToGui(screen);
            var ratio = view.MaxHp <= 0 ? 0f : (float)view.Hp / view.MaxHp;
            if (!IsBossEntity(pair.Key, view.Kind, view.Label))
            {
                DrawBar(gui, ratio, pair.Key == _selfId ? Color.cyan : Color.green);
                UiChrome.DrawFloat(new Rect(gui.x - 40, gui.y - 28, 80, 18),
                    view.Hp + "/" + view.MaxHp, Color.white, 12);
            }
            else
            {
                UiChrome.DrawFloat(new Rect(gui.x - 60, gui.y - 28, 120, 18), view.Label, Color.white, 13);
            }
        }

        foreach (var pair in _projectiles)
        {
            var bolt = pair.Value;
            if (bolt.Transform == null || _cam == null)
            {
                continue;
            }

            var screen = _cam.WorldToScreenPoint(FxAtWorld(bolt.Transform.position, 0.2f));
            if (screen.z < 0f)
            {
                continue;
            }

            var gui = UiChrome.ScreenToGui(screen);
            GUI.color = bolt.Color;
            GUI.DrawTexture(new Rect(gui.x - 10f, gui.y - 6f, 20f, 12f), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        for (var i = _floats.Count - 1; i >= 0; i--)
        {
            var ft = _floats[i];
            if (Time.time > ft.Until)
            {
                _floats.RemoveAt(i);
                continue;
            }

            var lift = 0.55f + (ft.Until - Time.time) * 0.35f;
            var screen = _cam.WorldToScreenPoint(FxAtWorld(ft.World, lift));
            if (screen.z < 0f)
            {
                continue;
            }

            var gui = UiChrome.ScreenToGui(screen);
            var age = 1f - Mathf.Clamp01((ft.Until - Time.time) / 0.9f);
            var pop = 1f + Mathf.Sin(Mathf.Clamp01(age * 4f) * Mathf.PI) * 0.18f;
            var size = Mathf.RoundToInt(Mathf.Max(14, ft.Size) * pop);
            UiChrome.DrawFloat(new Rect(gui.x - 48, gui.y - 22, 96, 32), ft.Text, ft.Color, size);
        }
    }

    private void Update()
    {
        HandleCameraInput();

        var keys = new List<string>(_entities.Keys);
        foreach (var key in keys)
        {
            var view = _entities[key];
            if (view.Dying && Time.time >= view.DeathRemoveAt)
            {
                if (key == _selfId)
                {
                    continue;
                }

                FinishDespawn(key);
                continue;
            }

            var wasMoving = view.Moving;
            if (view.Moving && view.Transform != null && !view.Dying)
            {
                var dur = view.MoveDur > 0.001f ? view.MoveDur : MoveDuration;
                view.MoveT += Time.deltaTime / dur;
                if (view.MoveT >= 1f)
                {
                    view.MoveT = 1f;
                    view.Moving = false;
                    view.Transform.position = view.To;
                }
                else
                {
                    view.Transform.position = Vector3.Lerp(view.From, view.To, view.MoveT);
                }

                UpdateStatusMarkerPositions(key);
            }

            if (wasMoving && !view.Moving)
            {
                view.JustMovedUntil = Time.time + JustMovedSec;
            }

            try
            {
                TickEntityAnim(key, ref view);
                BillboardEntity(ref view);
            }
            catch (System.Exception ex)
            {
                GameLog.WarnOnce(GameLog.Channel.Gfx, "anim:" + key,
                    "reason=entity_anim_throw  entity=" + key + "  err=" + ex.Message);
            }

            _entities[key] = view;
        }

        if (_entities.TryGetValue(_selfId, out var self) && self.Transform != null)
        {
            UpdateOrbitCamera(self.Transform.position);
        }

        TickBolts();
        TickTempFx();
    }

    private void TickTempFx()
    {
        for (var i = _tempFx.Count - 1; i >= 0; i--)
        {
            var fx = _tempFx[i];
            if (fx.Go == null)
            {
                _tempFx.RemoveAt(i);
                continue;
            }

            var life = Mathf.Max(0.01f, fx.Until - fx.Born);
            var t = Mathf.Clamp01((Time.time - fx.Born) / life);
            var ease = 1f - (1f - t) * (1f - t);
            fx.Go.transform.localScale = Vector3.Lerp(fx.Scale0, fx.Scale1, ease);
            if (fx.Sr != null)
            {
                if (fx.Frames != null && fx.Frames.Length > 0)
                {
                    var fps = fx.Fps > 0.1f ? fx.Fps : 12f;
                    var idx = Mathf.FloorToInt((Time.time - fx.Born) * fps);
                    if (fx.Loop)
                    {
                        idx %= fx.Frames.Length;
                    }
                    else
                    {
                        idx = Mathf.Min(idx, fx.Frames.Length - 1);
                    }

                    fx.Sr.sprite = fx.Frames[idx];
                    fx.Sr.color = fx.Color0;
                }
                else
                {
                    fx.Sr.color = Color.Lerp(fx.Color0, fx.Color1, t);
                }
            }

            if (Mathf.Abs(fx.SpinDeg) > 0.01f || Mathf.Abs(fx.FaceZ) > 0.01f || _cam != null)
            {
                var billboard = _cam != null ? _cam.transform.rotation : Quaternion.identity;
                var z = fx.FaceZ + fx.SpinDeg * (Time.time - fx.Born);
                fx.Go.transform.rotation = billboard * Quaternion.Euler(0f, 0f, z);
            }

            if (!string.IsNullOrEmpty(fx.FollowId) &&
                _entities.TryGetValue(fx.FollowId, out var follow) &&
                follow.Transform != null)
            {
                fx.Go.transform.position = follow.Transform.position + fx.FollowOff;
            }

            if (Time.time <= fx.Until)
            {
                continue;
            }

            Destroy(fx.Go);
            _tempFx.RemoveAt(i);
        }
    }

    private void HandleCameraInput()
    {
        var mouse = Mouse.current;
        if (mouse == null)
        {
            // Touch swipe handled below.
        }
        else
        {
            // Camera orbit: middle-mouse drag (RMB = click-to-move).
            if (mouse.middleButton.wasPressedThisFrame)
            {
                _rmbDragging = true;
                _rmbLastX = mouse.position.ReadValue().x;
            }

            if (mouse.middleButton.wasReleasedThisFrame)
            {
                _rmbDragging = false;
            }

            if (_rmbDragging && mouse.middleButton.isPressed)
            {
                var x = mouse.position.ReadValue().x;
                var delta = x - _rmbLastX;
                if (Mathf.Abs(delta) > 2f)
                {
                    _camYaw += delta * 0.25f;
                }

                _rmbLastX = x;
            }
        }

        // Smartphone: one-finger horizontal swipe rotates yaw.
        var touch = Touchscreen.current;
        if (touch != null && touch.primaryTouch.press.isPressed)
        {
            var delta = touch.primaryTouch.delta.ReadValue();
            if (Mathf.Abs(delta.x) > Mathf.Abs(delta.y) && Mathf.Abs(delta.x) > 0.5f)
            {
                _camYaw += delta.x * 0.15f;
            }
        }
    }

    private void TickEntityAnim(string entityId, ref EntityView view)
    {
        if (view.Renderer == null)
        {
            return;
        }

        ApplyUnlit(view.Renderer);
        EnsureGroundShadow(ref view);

        if (!view.UsesArt)
        {
            if (view.Renderer.sprite == null || !SpriteCatalog.SpriteIsVisible(view.Renderer.sprite))
            {
                view.Renderer.sprite = SpriteCatalog.ForEntity(
                    view.Kind == "player" ? "player_local" : entityId, view.Kind);
                BindSpriteTexture(view.Renderer);
            }

            view.Renderer.enabled = true;
            return;
        }

        if (SpriteCatalog.IsPortalKind(entityId, view.Kind))
        {
            var near = false;
            if (_entities.TryGetValue(_selfId, out var me) && me.Transform != null && view.Transform != null)
            {
                var dx = me.Transform.position.x - view.Transform.position.x;
                var dy = me.Transform.position.z - view.Transform.position.z;
                near = (dx * dx) + (dy * dy) <= 2.4f * 2.4f;
            }

            view.Renderer.sprite = SpriteCatalog.ForProp(near ? "portal_active" : "portal");
            view.Renderer.color = Color.white;
            view.Renderer.flipX = false;
            BindSpriteTexture(view.Renderer);
            return;
        }

        var idHint = view.Kind == "player" ? "player_local" : entityId;
        var eight = SpriteCatalog.UsesEightDir(idHint, view.Kind);
        RefreshViewFacing(ref view);

        int facingRow;
        if (eight)
        {
            facingRow = Mathf.Clamp(view.Facing, 0, 7);
        }
        else
        {
            // Craftpix enemies are 4-dir (down/left/right/up). Map 8-dir look onto those rows.
            facingRow = SpriteCatalog.ToFourDirRow(view.Facing);
        }

        SpriteCatalog.Clip want;
        if (view.Dying || view.AnimClip == SpriteCatalog.Clip.Death)
        {
            want = SpriteCatalog.Clip.Death;
        }
        else
        {
            var locomoting = view.Moving || Time.time < view.JustMovedUntil;
            var sitRestLocked = view.AnimOneShot && Time.time < view.AnimLockUntil && IsSitRestAnim(view);
            if (locomoting && (view.Resting || sitRestLocked))
            {
                BreakSitRest(ref view);
            }

            if (view.AnimOneShot && Time.time < view.AnimLockUntil)
            {
                want = view.AnimClip;
            }
            else
            {
                view.AnimOneShot = false;
                view.AnimVariant = null;
                if (locomoting)
                {
                    want = SpriteCatalog.Clip.Run;
                }
                else if (view.Resting)
                {
                    want = SpriteCatalog.Clip.Emote;
                    view.AnimVariant = "idle_sitting";
                }
                else
                {
                    want = SpriteCatalog.Clip.Idle;
                }
            }
        }

        if (want != view.AnimClip || facingRow != view.AnimFacingRow)
        {
            var keepFrame = (IsLocoClip(want) && IsLocoClip(view.AnimClip))
                || (want == SpriteCatalog.Clip.Idle && view.AnimClip == SpriteCatalog.Clip.Idle)
                || (view.Resting && want == view.AnimClip && IsSitRestAnim(view));
            view.AnimClip = want;
            view.AnimFacingRow = facingRow;
            if (!keepFrame)
            {
                view.AnimFrame = 0;
                view.AnimTime = 0f;
            }
        }

        var inCombat = IsInCombat(entityId);
        var frames = EnsureVisibleFrames(
            idHint, view.Kind, view.AnimClip, facingRow, inCombat, view.AnimVariant, out var flipX, out var resolved);
        if (resolved != view.AnimClip)
        {
            if (view.AnimClip == SpriteCatalog.Clip.Idle)
            {
                GameLog.WarnOnce(GameLog.Channel.Gfx, "idle-empty:" + idHint + ":" + facingRow,
                    "reason=empty_idle_facing  entity=" + idHint + "  facing=" + facingRow +
                    "  fallback=" + resolved);
            }

            view.AnimClip = resolved;
        }

        var bodyTint = entityId == _selfId ? SpriteCatalog.ClassTint(_classId) : Color.white;
        if (Time.time < view.HurtTintUntil)
        {
            var pulse = 0.4f + 0.6f * Mathf.Abs(Mathf.Sin(Time.time * 28f));
            bodyTint = Color.Lerp(bodyTint, new Color(1f, 0.22f, 0.18f, 1f), pulse);
        }

        var fps = view.AnimClip switch
        {
            SpriteCatalog.Clip.Walk => 12f,
            SpriteCatalog.Clip.Run => 12f,
            SpriteCatalog.Clip.Attack => 12f,
            SpriteCatalog.Clip.WalkAttack => 12f,
            SpriteCatalog.Clip.RunAttack => 14f,
            SpriteCatalog.Clip.Pickup => 10f,
            SpriteCatalog.Clip.Skill => 12f,
            SpriteCatalog.Clip.Hurt => 10f,
            SpriteCatalog.Clip.Death => 8f,
            SpriteCatalog.Clip.Emote => 8f,
            _ => 6f,
        };
        view.AnimTime += Time.deltaTime;
        if (view.AnimTime >= 1f / fps)
        {
            view.AnimTime = 0f;
            if (view.AnimClip == SpriteCatalog.Clip.Death)
            {
                view.AnimFrame = Mathf.Min(view.AnimFrame + 1, frames.Length - 1);
            }
            else if (view.Resting || IsSitRestAnim(view))
            {
                AdvanceSitRestLoop(ref view, frames);
            }
            else if (view.AnimOneShot)
            {
                if (view.AnimFrame + 1 >= frames.Length)
                {
                    view.AnimOneShot = false;
                    view.AnimLockUntil = 0f;
                    view.AnimVariant = null;
                    view.AnimClip = SpriteCatalog.Clip.Idle;
                    view.AnimFrame = 0;
                    frames = EnsureVisibleFrames(
                        idHint, view.Kind, SpriteCatalog.Clip.Idle, facingRow, inCombat, null, out flipX, out resolved);
                    view.AnimClip = resolved;
                }
                else
                {
                    view.AnimFrame += 1;
                }
            }
            else
            {
                view.AnimFrame = (view.AnimFrame + 1) % frames.Length;
            }
        }

        if (view.AnimFrame >= frames.Length)
        {
            view.AnimFrame = 0;
        }

        view.Renderer.sprite = frames[view.AnimFrame];
        view.Renderer.color = bodyTint;
        view.Renderer.flipX = flipX;
        view.Renderer.enabled = true;
        BindSpriteTexture(view.Renderer);
        BindVisibleOrShape(ref view, idHint);
    }

    private static bool IsLocoClip(SpriteCatalog.Clip clip)
    {
        return clip == SpriteCatalog.Clip.Walk || clip == SpriteCatalog.Clip.Run;
    }

    /// <summary>Play sit-down once, then loop the last two seated frames.</summary>
    private static void AdvanceSitRestLoop(ref EntityView view, Sprite[] frames)
    {
        if (frames == null || frames.Length == 0)
        {
            return;
        }

        if (frames.Length <= 2)
        {
            view.AnimFrame = (view.AnimFrame + 1) % frames.Length;
            return;
        }

        var loopStart = frames.Length - 2;
        if (view.AnimFrame < loopStart)
        {
            view.AnimFrame += 1;
            return;
        }

        view.AnimFrame = loopStart + ((view.AnimFrame + 1 - loopStart) % 2);
    }

    private static Sprite[] ResolveAnimFrames(
        string idHint, string kind, SpriteCatalog.Clip clip, int facingRow, bool inCombat, string variant, out bool flipX)
    {
        var frames = SpriteCatalog.GetClip(idHint, kind, clip, facingRow, inCombat, variant);
        flipX = SpriteCatalog.LastClipFlipX;
        if (frames != null && frames.Length > 0)
        {
            return frames;
        }

        if (clip == SpriteCatalog.Clip.Run || clip == SpriteCatalog.Clip.WalkAttack || clip == SpriteCatalog.Clip.RunAttack)
        {
            var alt = clip == SpriteCatalog.Clip.Run ? SpriteCatalog.Clip.Walk : SpriteCatalog.Clip.Attack;
            frames = SpriteCatalog.GetClip(idHint, kind, alt, facingRow, inCombat, variant);
            flipX = SpriteCatalog.LastClipFlipX;
            if (frames != null && frames.Length > 0)
            {
                return frames;
            }
        }

        if (clip == SpriteCatalog.Clip.Idle || clip == SpriteCatalog.Clip.Hurt || clip == SpriteCatalog.Clip.Death
            || clip == SpriteCatalog.Clip.Pickup || clip == SpriteCatalog.Clip.Skill
            || clip == SpriteCatalog.Clip.Emote)
        {
            frames = SpriteCatalog.GetClip(idHint, kind, SpriteCatalog.Clip.Walk, facingRow, inCombat, variant);
            flipX = SpriteCatalog.LastClipFlipX;
            if (frames != null && frames.Length > 0)
            {
                return frames;
            }
        }

        frames = SpriteCatalog.GetClip(idHint, kind, SpriteCatalog.Clip.Walk, 0, inCombat, variant);
        flipX = SpriteCatalog.LastClipFlipX;
        return frames;
    }

    /// <summary>
    /// last-good clip → walk → run → idle → any facing → shape. Never returns empty.
    /// </summary>
    private static Sprite[] EnsureVisibleFrames(
        string idHint,
        string kind,
        SpriteCatalog.Clip clip,
        int facingRow,
        bool inCombat,
        string variant,
        out bool flipX,
        out SpriteCatalog.Clip used)
    {
        used = clip;
        var frames = ResolveAnimFrames(idHint, kind, clip, facingRow, inCombat, variant, out flipX);
        if (SpriteCatalog.FramesAreVisible(frames))
        {
            return frames;
        }

        var alts = new[]
        {
            SpriteCatalog.Clip.Walk,
            SpriteCatalog.Clip.Run,
            SpriteCatalog.Clip.Idle,
        };
        for (var i = 0; i < alts.Length; i++)
        {
            if (alts[i] == clip)
            {
                continue;
            }

            frames = ResolveAnimFrames(idHint, kind, alts[i], facingRow, inCombat, null, out flipX);
            if (SpriteCatalog.FramesAreVisible(frames))
            {
                used = alts[i];
                return frames;
            }
        }

        var maxDir = SpriteCatalog.UsesEightDir(idHint, kind) ? 8 : 4;
        for (var d = 0; d < maxDir; d++)
        {
            if (d == facingRow)
            {
                continue;
            }

            frames = ResolveAnimFrames(idHint, kind, SpriteCatalog.Clip.Walk, d, inCombat, null, out flipX);
            if (SpriteCatalog.FramesAreVisible(frames))
            {
                used = SpriteCatalog.Clip.Walk;
                return frames;
            }

            frames = ResolveAnimFrames(idHint, kind, SpriteCatalog.Clip.Idle, d, inCombat, null, out flipX);
            if (SpriteCatalog.FramesAreVisible(frames))
            {
                used = SpriteCatalog.Clip.Idle;
                return frames;
            }
        }

        flipX = false;
        GameLog.WarnOnce(GameLog.Channel.Gfx, "fallback-shape-anim:" + idHint,
            "reason=spawn_invisible  entity=" + idHint + "  kind=" + kind + "  fallback=shape");
        var shape = SpriteCatalog.ForEntity(idHint, kind);
        return new[] { shape };
    }

    private static void BindVisibleOrShape(ref EntityView view, string idHint)
    {
        if (view.Renderer == null)
        {
            return;
        }

        view.Renderer.enabled = true;
        if (SpriteCatalog.SpriteIsVisible(view.Renderer.sprite))
        {
            view.BlankTicks = 0;
            return;
        }

        view.BlankTicks += 1;
        view.Renderer.sprite = SpriteCatalog.ForEntity(idHint, view.Kind);
        BindSpriteTexture(view.Renderer);
        GameLog.WarnOnce(GameLog.Channel.Gfx, "bind-shape:" + idHint,
            "reason=spawn_invisible  entity=" + idHint + "  fallback=shape  ticks=" + view.BlankTicks);
    }

    private static void EnsureGroundShadow(ref EntityView view)
    {
        if (view.Transform == null || SpriteCatalog.IsPortalKind(null, view.Kind))
        {
            return;
        }

        if (view.Shadow != null)
        {
            return;
        }

        var existing = view.Transform.Find("ground_shadow");
        if (existing != null)
        {
            view.Shadow = existing.GetComponent<SpriteRenderer>();
            if (view.Shadow != null)
            {
                return;
            }
        }

        var go = new GameObject("ground_shadow");
        go.transform.SetParent(view.Transform, false);
        go.transform.localPosition = new Vector3(0f, 0.02f, 0.04f);
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        go.transform.localScale = new Vector3(0.9f, 0.5f, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GroundShadowSprite();
        sr.color = new Color(0f, 0f, 0f, 0.38f);
        sr.sortingOrder = 15;
        ApplyUnlit(sr);
        view.Shadow = sr;
    }

    private static Sprite _groundShadowSprite;

    private static Sprite GroundShadowSprite()
    {
        if (_groundShadowSprite != null)
        {
            return _groundShadowSprite;
        }

        const int s = 64;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[s * s];
        var cx = (s - 1) * 0.5f;
        var cy = (s - 1) * 0.5f;
        for (var y = 0; y < s; y++)
        {
            for (var x = 0; x < s; x++)
            {
                var nx = (x - cx) / (cx * 0.92f);
                var ny = (y - cy) / (cy * 0.55f);
                var d = (nx * nx) + (ny * ny);
                var a = d >= 1f ? 0f : Mathf.Clamp01(1f - d);
                a *= a;
                pixels[y * s + x] = new Color(0f, 0f, 0f, a);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        _groundShadowSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 48f);
        return _groundShadowSprite;
    }

    public void PlayAttackAnim(string entityId)
    {
        if (string.IsNullOrEmpty(entityId) || !_entities.TryGetValue(entityId, out var view))
        {
            return;
        }

        var clip = view.Moving
            ? (view.MoveT < 0.85f ? SpriteCatalog.Clip.RunAttack : SpriteCatalog.Clip.WalkAttack)
            : SpriteCatalog.Clip.Attack;
        var lockSec = clip == SpriteCatalog.Clip.RunAttack ? 0.875f
            : clip == SpriteCatalog.Clip.WalkAttack ? 1.05f
            : 1.225f;
        PlayOneShot(entityId, clip, lockSec);
    }

    public void PlayPickupAnim(string entityId = null)
    {
        var id = string.IsNullOrEmpty(entityId) ? _selfId : entityId;
        PlayOneShot(id, SpriteCatalog.Clip.Pickup, 0.6f);
    }

    /// <summary>Face the target, play the weapon swing, and spawn skill VFX (local predict + server confirm).</summary>
    public void PlayCastAttack(string casterId, string targetId, string skillId)
    {
        var id = MapCasterId(casterId);
        var fxKey = id + ":" + (skillId ?? "");
        if (fxKey == _castFxKey && Time.time < _castFxUntil)
        {
            return;
        }

        _castFxKey = fxKey;
        _castFxUntil = Time.time + CastFxDedupSec;
        PlaySkillFx(id, targetId, skillId);
        if (skillId == "shove")
        {
            FaceEntityToward(id, targetId);
            PlayOneShot(id, SpriteCatalog.Clip.Skill, 0.96f, skillId);
            return;
        }

        if (!IsStrikeSkill(skillId))
        {
            if (skillId == "war_cry" || skillId == "iron_stance" || skillId == "rally"
                || skillId == "decoy" || skillId == "power_chant" || skillId == "mend"
                || skillId == "group_chant" || skillId == "rest" || skillId == "powerup"
                || skillId == "cleave" || skillId == "arrow_rain" || skillId == "arcane_nova"
                || skillId == "knife_fan" || skillId == "dash" || skillId == "ward"
                || skillId == "haste" || skillId == "blind_dust"
                || skillId == "thunderstorm" || skillId == "explosion")
            {
                PlayOneShot(id, SpriteCatalog.Clip.Skill, 1.225f, skillId);
            }

            return;
        }

        FaceEntityToward(id, targetId);
        if (skillId == "shockwave" || skillId == "hook_shot" || skillId == "cleave"
            || skillId == "arrow_rain" || skillId == "arcane_nova" || skillId == "knife_fan"
            || skillId == "slash" || skillId == "thunderstorm" || skillId == "explosion")
        {
            PlayOneShot(id, SpriteCatalog.Clip.Skill, 1.225f, skillId);
            return;
        }

        PlayAttackAnim(id);
    }

    public string PlayEmoteSlot(int digit)
    {
        if (!SpriteCatalog.TryEmote(digit, out var emoteId, out var blocked))
        {
            return blocked ?? "";
        }

        PlayOneShot(_selfId, SpriteCatalog.Clip.Emote, 1.25f, emoteId);
        return "emote " + emoteId;
    }

    public void PlayLevelUpFx(string entityId = null)
    {
        var id = string.IsNullOrEmpty(entityId) ? _selfId : entityId;
        if (!_entities.TryGetValue(id, out var view) || view.Transform == null)
        {
            return;
        }

        var pos = view.Transform.position;
        SpawnRingFx(pos, 0.35f, 3.2f, new Color(1f, 0.9f, 0.35f, 0.9f),
            new Color(1f, 0.75f, 0.1f, 0f), 0.7f, 90f);
        SpawnBurstFx(pos + Vector3.up * 0.45f, new Color(1f, 0.95f, 0.55f, 0.95f), 1.6f, 0.55f);
        SpawnGlowFx(id, new Color(1f, 0.88f, 0.3f, 0.85f), 0.7f);
        PlayOneShot(id, SpriteCatalog.Clip.Emote, 1.2f, "south_powerup");
    }

    public void PlayTeleportFx(string entityId = null)
    {
        var id = string.IsNullOrEmpty(entityId) ? _selfId : entityId;
        Vector3 pos;
        if (_entities.TryGetValue(id, out var view) && view.Transform != null)
        {
            pos = view.Transform.position;
        }
        else
        {
            pos = Vector3.zero;
        }

        SpawnRingFx(pos, 0.2f, 1.4f, new Color(0.45f, 0.85f, 1f, 0.9f),
            new Color(0.2f, 0.5f, 1f, 0f), 0.45f, 120f);
        SpawnBurstFx(pos + Vector3.up * 0.8f, new Color(0.7f, 0.95f, 1f, 0.95f), 1.35f, 0.4f);
        SpawnFx(MakeGlowSprite(), pos + Vector3.up * 0.15f,
            new Vector3(0.25f, 0.4f, 1f), new Vector3(0.15f, 2.4f, 1f),
            new Color(0.65f, 0.95f, 1f, 0.9f), new Color(0.4f, 0.7f, 1f, 0f),
            0.5f, 0f, 0f, "", Vector3.zero, 48);
    }

    public void PlayGachaRevealFx()
    {
        Vector3 pos = Vector3.zero;
        if (_entities.TryGetValue(_selfId, out var view) && view.Transform != null)
        {
            pos = view.Transform.position;
        }

        SpawnRingFx(pos, 0.5f, 2.8f, new Color(1f, 0.75f, 0.2f, 0.85f),
            new Color(1f, 0.45f, 0.05f, 0f), 0.65f, 70f);
        SpawnBurstFx(pos + Vector3.up * 0.35f, new Color(1f, 0.9f, 0.4f, 0.95f), 1.5f, 0.5f);
        SpawnBurstFx(pos + new Vector3(0.4f, 0.55f, 0f), new Color(0.95f, 0.55f, 1f, 0.9f), 0.85f, 0.4f);
        SpawnBurstFx(pos + new Vector3(-0.35f, 0.5f, 0f), new Color(0.45f, 0.85f, 1f, 0.9f), 0.8f, 0.4f);
    }

    public void PlayPortalRipple(Vector3? at = null)
    {
        Vector3 pos;
        if (at.HasValue)
        {
            pos = at.Value;
        }
        else if (_entities.TryGetValue(_selfId, out var view) && view.Transform != null)
        {
            pos = view.Transform.position;
        }
        else
        {
            pos = Vector3.zero;
        }

        SpawnRingFx(pos, 0.3f, 3.6f, new Color(0.65f, 0.4f, 1f, 0.85f),
            new Color(0.4f, 0.2f, 0.9f, 0f), 0.55f, 40f);
        SpawnRingFx(pos, 0.15f, 2.2f, new Color(0.9f, 0.7f, 1f, 0.7f),
            new Color(0.55f, 0.3f, 1f, 0f), 0.4f, -60f);
        SpawnBurstFx(pos + Vector3.up * 0.2f, new Color(0.8f, 0.6f, 1f, 0.9f), 1.1f, 0.35f);
    }

    public static bool IsStrikeSkill(string skillId)
    {
        if (string.IsNullOrEmpty(skillId))
        {
            return false;
        }

        if (skillId == "auto_attack" || skillId == "auto_attack_off" || skillId == "slash" || skillId == "shot" || skillId == "hook_shot"
            || skillId == "shockwave" || skillId == "shove" || skillId == "pull" || skillId == "stun_bolt"
            || skillId == "cannon_flame" || skillId == "cleave" || skillId == "arrow_rain"
            || skillId == "arcane_nova" || skillId == "knife_fan"
            || skillId == "thunderstorm" || skillId == "explosion")
        {
            return true;
        }

        return skillId.EndsWith("_hit");
    }

    private void PlaySkillFx(string casterId, string targetId, string skillId)
    {
        if (string.IsNullOrEmpty(skillId))
        {
            return;
        }

        SoundCatalog.PlaySkill(skillId);

        var from = GetEntityWorldPos(casterId);
        if (!from.HasValue)
        {
            return;
        }

        var to = GetEntityWorldPos(targetId);
        var dir = to.HasValue
            ? WorldCoords.MapXZ(to.Value) - WorldCoords.MapXZ(from.Value)
            : FacingVector(casterId);
        if (dir.sqrMagnitude < 1e-6f)
        {
            dir = FacingVector(casterId);
        }

        dir.Normalize();
        var faceZ = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        var origin = from.Value + Vector3.up * 0.55f;

        switch (skillId)
        {
            case "auto_attack":
            case "auto_attack_off":
            case "slash":
            case "shove":
                SpawnSlashFx(WorldCoords.AlongMap(origin, dir, 0.45f), faceZ,
                    new Color(1f, 0.95f, 0.75f, 0.95f));
                break;
            case "shot":
            case "stun_bolt":
            case "cannon_flame":
                SpawnBurstFx(WorldCoords.AlongMap(origin, dir, 0.3f),
                    new Color(1f, 0.8f, 0.35f, 0.9f), 0.45f, 0.14f);
                break;
            case "thunderstorm":
                SpawnThunderstormFx(to ?? from.Value);
                break;
            case "explosion":
                SpawnNuclearRingFx(to ?? from.Value);
                break;
            case "shockwave":
                SpawnRingFx(from.Value, 0.45f, 5.2f, new Color(1f, 0.78f, 0.2f, 0.75f),
                    new Color(1f, 0.45f, 0.05f, 0f), 0.38f, 0f);
                break;
            case "dash":
                SpawnDashTrail(casterId, dir);
                break;
            case "rally":
                SpawnRingFx(from.Value, 0.4f, 2.4f, new Color(1f, 0.55f, 0.12f, 0.8f),
                    new Color(1f, 0.35f, 0.05f, 0f), 0.42f, 80f);
                SpawnGlowFx(casterId, new Color(1f, 0.6f, 0.15f, 0.75f), 0.5f);
                break;
            case "hook_shot":
            case "pull":
                SpawnHookLine(from.Value, to ?? WorldCoords.AlongMap(from.Value, dir, 3f));
                break;
            case "mend":
                SpawnGlowFx(string.IsNullOrEmpty(targetId) ? casterId : targetId,
                    new Color(0.35f, 1f, 0.55f, 0.9f), 0.55f);
                break;
            case "decoy":
                SpawnRingFx(from.Value, 0.55f, 1.65f, new Color(0.35f, 0.85f, 1f, 0.85f),
                    new Color(0.2f, 0.55f, 1f, 0f), 0.55f, -50f);
                SpawnGlowFx(casterId, new Color(0.4f, 0.8f, 1f, 0.7f), 0.5f);
                break;
        }
    }

    private Vector2 FacingVector(string id)
    {
        if (!_entities.TryGetValue(id, out var view))
        {
            return Vector2.right;
        }

        if (view.FaceX * view.FaceX + view.FaceZ * view.FaceZ > 1e-8f)
        {
            return new Vector2(view.FaceX, view.FaceZ);
        }

        var rad = view.Facing * 45f * Mathf.Deg2Rad;
        return new Vector2(Mathf.Sin(rad), -Mathf.Cos(rad));
    }

    public void SetHeldWeapon(string weaponId)
    {
        _heldWeaponId = weaponId ?? "";
        SpriteCatalog.SetHeldWeapon(_heldWeaponId);
        EnsureHeldMark();
        if (_heldMarkSr == null)
        {
            return;
        }

        _heldMarkSr.sprite = SpriteCatalog.WeaponMark(_heldWeaponId);
        PlaceHeldMark();
    }

    public void SetResting(bool resting)
    {
        if (!_entities.TryGetValue(_selfId, out var view))
        {
            return;
        }

        view.Resting = resting;
        if (!resting)
        {
            BreakSitRest(ref view);
        }

        _entities[_selfId] = view;
    }

    /// <summary>Leave sit/rest as soon as the local player actually moves.</summary>
    public void BreakRestFromMove()
    {
        if (!_entities.TryGetValue(_selfId, out var view))
        {
            return;
        }

        BreakSitRest(ref view);
        _entities[_selfId] = view;
    }

    private static void BreakSitRest(ref EntityView view)
    {
        view.Resting = false;
        if (!IsSitRestAnim(view))
        {
            return;
        }

        view.AnimOneShot = false;
        view.AnimLockUntil = 0f;
        view.AnimVariant = null;
        if (view.AnimClip == SpriteCatalog.Clip.Emote || view.AnimClip == SpriteCatalog.Clip.Skill)
        {
            view.AnimClip = SpriteCatalog.Clip.Idle;
            view.AnimFrame = 0;
            view.AnimTime = 0f;
        }
    }

    private static bool IsSitRestAnim(EntityView view)
    {
        var variant = view.AnimVariant ?? "";
        return variant == "idle_sitting" || variant == "rest";
    }

    private void EnsureHeldMark()
    {
        if (_heldMark != null)
        {
            return;
        }

        var go = new GameObject("held_weapon");
        _heldMarkSr = go.AddComponent<SpriteRenderer>();
        _heldMarkSr.sortingOrder = 28;
        ApplyUnlit(_heldMarkSr);
        _heldMark = go.transform;
        _heldMark.localScale = Vector3.one * 0.28f;
    }

    private void PlaceHeldMark()
    {
        if (_heldMark == null || string.IsNullOrEmpty(_heldWeaponId) || _heldWeaponId == "none")
        {
            if (_heldMark != null)
            {
                _heldMark.gameObject.SetActive(false);
            }

            return;
        }

        if (!_entities.TryGetValue(_selfId, out var self) || self.Transform == null)
        {
            return;
        }

        var drawnBody = SpriteCatalog.HasDrawnWeaponBodyArt();
        var combat = IsInCombat(_selfId);
        var emoting = self.AnimClip == SpriteCatalog.Clip.Emote
            || self.AnimClip == SpriteCatalog.Clip.Pickup
            || self.AnimClip == SpriteCatalog.Clip.Death;
        var hideMark = !combat || drawnBody || emoting;
        _heldMark.gameObject.SetActive(!hideMark);

        if (hideMark)
        {
            return;
        }

        if (_heldMark.parent != self.Transform)
        {
            _heldMark.SetParent(self.Transform, false);
        }

        var facing = self.Facing;
        var left = SpriteCatalog.FacingLooksLeft(facing, SpriteCatalog.UsesEightDir(_selfId, "player"));
        var handX = left ? -0.18f : 0.18f;
        _heldMark.localPosition = new Vector3(handX, 0.42f, -0.05f);
        _heldMark.localRotation = Quaternion.identity;
        var parentS = Mathf.Max(0.01f, self.Transform.localScale.x);
        _heldMark.localScale = Vector3.one * (0.62f / parentS);
        if (_heldMarkSr != null)
        {
            _heldMarkSr.flipX = left;
        }
    }

    public void PlayHurtAnim(string entityId)
    {
        if (!string.IsNullOrEmpty(entityId) && _entities.TryGetValue(entityId, out var hurt))
        {
            hurt.HurtTintUntil = Time.time + 0.28f;
            _entities[entityId] = hurt;
        }

        PlayOneShot(entityId, SpriteCatalog.Clip.Hurt, 0.35f);
    }

    private void PlayOneShot(string entityId, SpriteCatalog.Clip clip, float lockSec, string variant = null)
    {
        if (string.IsNullOrEmpty(entityId) || !_entities.TryGetValue(entityId, out var view))
        {
            return;
        }

        if (view.Dying || !view.UsesArt)
        {
            return;
        }

        // Don't interrupt death; hurt can interrupt attack.
        if (view.AnimClip == SpriteCatalog.Clip.Death)
        {
            return;
        }

        view.AnimClip = clip;
        view.AnimOneShot = true;
        view.AnimLockUntil = Time.time + lockSec;
        view.AnimFrame = 0;
        view.AnimTime = 0f;
        view.AnimFacingRow = -1;
        view.AnimVariant = variant;
        _entities[entityId] = view;
    }

    /// <summary>Drop attack/skill recovery so locomotion can play immediately.</summary>
    public void InterruptStrike(string entityId)
    {
        if (string.IsNullOrEmpty(entityId) || !_entities.TryGetValue(entityId, out var view))
        {
            return;
        }

        if (view.Dying || view.AnimClip == SpriteCatalog.Clip.Death)
        {
            return;
        }

        if (!view.AnimOneShot)
        {
            return;
        }

        var clip = view.AnimClip;
        if (clip != SpriteCatalog.Clip.Attack
            && clip != SpriteCatalog.Clip.WalkAttack
            && clip != SpriteCatalog.Clip.RunAttack
            && clip != SpriteCatalog.Clip.Skill)
        {
            return;
        }

        view.AnimOneShot = false;
        view.AnimLockUntil = 0f;
        view.AnimVariant = null;
        view.AnimClip = SpriteCatalog.Clip.Idle;
        view.AnimFrame = 0;
        view.AnimTime = 0f;
        _entities[entityId] = view;
    }

    private bool IsInCombat(string entityId)
    {
        if (string.IsNullOrEmpty(entityId))
        {
            return false;
        }

        if (entityId == _selfId)
        {
            if (!string.IsNullOrEmpty(_lockTargetId) && IsLivingFoe(_lockTargetId))
            {
                return true;
            }

            foreach (var pair in _entities)
            {
                if (pair.Value.Kind == "monster" && pair.Value.Hp > 0
                    && pair.Value.ThreatTopId == _selfId)
                {
                    return true;
                }
            }

            return false;
        }

        if (!_entities.TryGetValue(entityId, out var view))
        {
            return false;
        }

        return view.AnimOneShot && (view.AnimClip == SpriteCatalog.Clip.Attack
            || view.AnimClip == SpriteCatalog.Clip.WalkAttack
            || view.AnimClip == SpriteCatalog.Clip.RunAttack
            || view.AnimClip == SpriteCatalog.Clip.Skill
            || view.AnimClip == SpriteCatalog.Clip.Hurt);
    }

    public bool IsLivingFoe(string id)
    {
        if (!_entities.TryGetValue(id, out var view) || view.Hp <= 0 || view.Dying)
        {
            return false;
        }

        return view.Kind == "monster" || view.Kind == "player";
    }

    private void BillboardEntity(ref EntityView view)
    {
        if (view.Transform == null || _cam == null)
        {
            return;
        }

        YBillboard(view.Transform);
        SortSprite(view.Renderer, view.Transform.position);
        if (view.Shadow != null && view.Renderer != null)
        {
            view.Shadow.sortingOrder = view.Renderer.sortingOrder - 2;
        }
    }

    private void YBillboard(Transform t)
    {
        if (t == null || _cam == null)
        {
            return;
        }

        var toCam = _cam.transform.position - t.position;
        toCam.y = 0f;
        if (toCam.sqrMagnitude < 1e-8f)
        {
            return;
        }

        t.rotation = Quaternion.LookRotation(toCam.normalized, Vector3.up);
    }

    private static void SortSprite(SpriteRenderer sr, Vector3 worldPos)
    {
        if (sr == null)
        {
            return;
        }

        sr.sortingOrder = 20 + Mathf.RoundToInt(-(worldPos.x + worldPos.z) * 10f);
    }

    private void LateUpdate()
    {
        UpdateLockRing();
        PlaceHeldMark();
        RefreshAimFromSelf();
        BillboardProps();
        foreach (var key in new List<string>(_entities.Keys))
        {
            var view = _entities[key];
            if (view.UsesArt || view.Renderer == null)
            {
                continue;
            }

            if (view.Renderer.color == Color.white && Time.frameCount % 8 == 0)
            {
                view.Renderer.color = view.BaseColor;
                _entities[key] = view;
            }
        }
    }

    private void BillboardProps()
    {
        if (_root == null)
        {
            return;
        }

        for (var i = 0; i < _root.childCount; i++)
        {
            var c = _root.GetChild(i);
            if (c.name != "prop")
            {
                continue;
            }

            YBillboard(c);
            SortSprite(c.GetComponent<SpriteRenderer>(), c.position);
        }
    }

    private void RefreshAimFromSelf()
    {
        if (_aimMode == 0 || !_entities.TryGetValue(_selfId, out var self) || self.Transform == null)
        {
            return;
        }

        var caster = self.Transform.position;
        if (_aimMode == 1)
        {
            UpdateLinearAim(caster, _aimDir, _aimRange, _aimWidth);
        }
        else if (_aimMode == 2)
        {
            UpdateConeAim(caster, _aimDir, _aimRange, _aimAngleDeg);
        }
        else if (_aimMode == 3)
        {
            UpdateGroundAim(caster, _aimPoint, _aimCastRange, _aimAoeRadius);
        }
    }

    private void EnsureAimPartCount(int count)
    {
        var wantCylinder = _aimMode == 3;
        for (var i = _aimParts.Count - 1; i >= 0; i--)
        {
            var t = _aimParts[i];
            if (t == null)
            {
                _aimParts.RemoveAt(i);
                continue;
            }

            var mf = t.GetComponent<MeshFilter>();
            var isCylinder = mf != null && mf.sharedMesh != null && mf.sharedMesh.name.IndexOf("Cylinder") >= 0;
            if (wantCylinder != isCylinder)
            {
                Destroy(t.gameObject);
                _aimParts.RemoveAt(i);
            }
        }

        while (_aimParts.Count > count)
        {
            var last = _aimParts[_aimParts.Count - 1];
            _aimParts.RemoveAt(_aimParts.Count - 1);
            if (last != null)
            {
                Destroy(last.gameObject);
            }
        }

        while (_aimParts.Count < count)
        {
            var type = wantCylinder ? PrimitiveType.Cylinder : PrimitiveType.Cube;
            var go = GameObject.CreatePrimitive(type);
            go.name = "aim_indicator";
            if (_root != null)
            {
                go.transform.SetParent(_root, false);
            }

            StripCollider(go);
            SetAimPartColor(go.transform, new Color(0.35f, 0.95f, 0.55f, 0.35f));
            _aimParts.Add(go.transform);
        }
    }

    private static void PlaceLinearBeam(Transform t, Vector3 caster, Vector2 dir, float range, float width)
    {
        if (t == null)
        {
            return;
        }

        var d3 = WorldCoords.MapDir3(dir);
        t.position = WorldCoords.OnGround(caster) + d3 * (range * 0.5f) + Vector3.up * 0.08f;
        t.localScale = new Vector3(range, 0.08f, width);
        if (d3.sqrMagnitude > 0.0001f)
        {
            t.rotation = Quaternion.FromToRotation(Vector3.right, d3.normalized);
        }

        SetAimPartColor(t, new Color(0.35f, 0.95f, 0.55f, 0.35f));
    }

    private static Vector2 RotateDir(Vector2 dir, float radians)
    {
        var c = Mathf.Cos(radians);
        var s = Mathf.Sin(radians);
        return new Vector2(dir.x * c - dir.y * s, dir.x * s + dir.y * c);
    }

    private static void SetAimPartColor(Transform t, Color color)
    {
        if (t == null)
        {
            return;
        }

        ApplyUnlitColor(t.gameObject, color);
    }

    private void ApplyState(string json)
    {
        // Only remap from the "you" object — never the first "id" in the whole payload
        // (map / monsters / portals also have "id" fields).
        var you = JsonUtil.SliceAround(json, "\"you\"", 0, 2400);
        var selfId = JsonUtil.ExtractString(you, "id");
        if (!string.IsNullOrEmpty(selfId))
        {
            RemapSelf(selfId);
        }

        if (JsonUtil.TryNumber(you, "x", out var youX) && JsonUtil.TryNumber(you, "y", out var youY))
        {
            JsonUtil.TryInt(you, "hp", out var hp);
            JsonUtil.TryInt(you, "maxHp", out var maxHp);
            JsonUtil.TryInt(you, "mp", out var mp);
            JsonUtil.TryInt(you, "maxMp", out var maxMp);
            JsonUtil.TryNumber(you, "hitRadius", out var hr);
            var name = JsonUtil.ExtractString(you, "name");
            var kind = JsonUtil.ExtractString(you, "kind");
            var showHp = maxHp > 0 ? maxHp : 100;
            Upsert(_selfId, youX, youY, new Color(0.15f, 0.85f, 1f),
                string.IsNullOrEmpty(name) ? "You" : name,
                hp, showHp, hr > 0 ? hr : 0.4f, true);
            if (hp > 0)
            {
                Revive(_selfId);
            }
            SetEntityMeta(_selfId, string.IsNullOrEmpty(kind) ? "player" : kind, mp, maxMp > 0 ? maxMp : mp);
            PlaceHeldMark();
            CenterCamera(youX, youY);
        }

        ApplyMapFromState(json);
        ClearForeignEntities();
        ApplyEntitiesByIdPrefix(json, "monster_");
        ApplyEntitiesByIdPrefix(json, "lab_");
        ApplyEntitiesByIdPrefix(json, "npc_");
        // Fallback: some payloads space after colon ("id": "monster_…")
        ApplyEntitiesByIdPrefix(json, "monster_", allowSpacedColon: true);
        ApplyEntitiesByIdPrefix(json, "lab_", allowSpacedColon: true);
        ApplyEntitiesByIdPrefix(json, "npc_", allowSpacedColon: true);

        if (!string.IsNullOrEmpty(_lockTargetId) && !_entities.ContainsKey(_lockTargetId))
        {
            LockClosestEnemy();
        }
    }

    private void ClearForeignEntities()
    {
        var remove = new List<string>();
        foreach (var pair in _entities)
        {
            if (pair.Key == _selfId)
            {
                continue;
            }

            remove.Add(pair.Key);
        }

        for (var i = 0; i < remove.Count; i++)
        {
            if (_entities.TryGetValue(remove[i], out var view) && view.Transform != null)
            {
                Destroy(view.Transform.gameObject);
            }

            _entities.Remove(remove[i]);
            ClearStatusMarkers(remove[i]);
        }
    }

    public void UpsertPortalMarker(string id, float x, float y)
    {
        Upsert(id, x, y, new Color(1f, 0.55f, 0.12f), "GATE", 1, 1, 0.55f, true);
        SetEntityMeta(id, "portal", 0, 0);
    }

    private void ApplyMapFromState(string json)
    {
        var mapSlice = JsonUtil.ExtractObject(json, "map");
        if (mapSlice.Length == 0)
        {
            // Legacy fallback (may match mapId — keep for old payloads only).
            mapSlice = JsonUtil.SliceAround(json, "\"map\":{", 0, 12000);
            if (mapSlice.Length == 0)
            {
                mapSlice = JsonUtil.SliceAround(json, "\"map\": {", 0, 12000);
            }
        }

        if (mapSlice.Length == 0)
        {
            return;
        }

        if (!JsonUtil.TryInt(mapSlice, "width", out var w) || !JsonUtil.TryInt(mapSlice, "height", out var h))
        {
            return;
        }

        if (w <= 0 || h <= 0)
        {
            return;
        }

        var blocked = JsonUtil.ParseBlockedTiles(mapSlice);
        var hazards = JsonUtil.ParseHazardTiles(mapSlice);
        var props = JsonUtil.ParseMapProps(mapSlice);
        var mapId = JsonUtil.ExtractString(mapSlice, "id");
        RebuildMap(w, h, blocked, mapId, hazards, props);
    }

    private void ApplyEntitiesByIdPrefix(string json, string prefix, bool allowSpacedColon = false)
    {
        var cursor = 0;
        var seen = new HashSet<string>();
        var token = allowSpacedColon ? "\"id\": \"" + prefix : "\"id\":\"" + prefix;
        var idKeyLen = allowSpacedColon ? "\"id\": \"".Length : "\"id\":\"".Length;
        while (true)
        {
            var idx = json.IndexOf(token, cursor);
            if (idx < 0)
            {
                break;
            }

            var idStart = idx + idKeyLen;
            var idEnd = json.IndexOf('"', idStart);
            if (idEnd < 0)
            {
                break;
            }

            var id = json.Substring(idStart, idEnd - idStart);
            cursor = idEnd + 1;
            if (!seen.Add(id))
            {
                continue;
            }

            var idToken = allowSpacedColon ? "\"id\": \"" + id + "\"" : "\"id\":\"" + id + "\"";
            var slice = JsonUtil.SliceAround(json, idToken, 0, 600);
            if (slice.Length == 0)
            {
                continue;
            }

            if (!JsonUtil.TryNumber(slice, "x", out var mx) || !JsonUtil.TryNumber(slice, "y", out var my))
            {
                continue;
            }

            var hasHp = JsonUtil.TryInt(slice, "hp", out var hp);
            JsonUtil.TryInt(slice, "maxHp", out var maxHp);
            JsonUtil.TryInt(slice, "mp", out var mp);
            JsonUtil.TryInt(slice, "maxMp", out var maxMp);
            JsonUtil.TryNumber(slice, "hitRadius", out var hr);
            var name = JsonUtil.ExtractString(slice, "name");
            var kind = JsonUtil.ExtractString(slice, "kind");
            var inferredKind = !string.IsNullOrEmpty(kind)
                ? kind
                : (prefix.StartsWith("npc")
                    ? "npc"
                    : "monster");
            var label = !string.IsNullOrEmpty(name)
                ? name
                : id.Replace("monster_", "").Replace("npc_", "").Replace("lab_", "");
            var color = ColorForEntity(id, inferredKind);
            var fallbackHp = inferredKind == "npc" ? 1 : 40;
            Upsert(id, mx, my, color, label,
                hasHp ? hp : fallbackHp,
                maxHp > 0 ? maxHp : fallbackHp,
                hr > 0 ? hr : 0.4f, true);
            SetEntityMeta(id, inferredKind, mp, maxMp);
        }
    }

    private static Color ColorForEntity(string id, string kind)
    {
        if (kind == "npc" || (!string.IsNullOrEmpty(id) && id.StartsWith("npc_")))
        {
            return new Color(0.95f, 0.85f, 0.35f);
        }

        if (string.IsNullOrEmpty(id))
        {
            return new Color(0.2f, 1f, 0.25f);
        }

        if (id.Contains("ruins_boss") || id.Contains("colossus"))
        {
            return new Color(0.72f, 0.55f, 0.32f);
        }

        if (id.Contains("crypt_boss") || (id.Contains("warden") && id.Contains("boss")))
        {
            return new Color(0.55f, 0.32f, 0.75f);
        }

        if (id.Contains("m_boss_f5") || id.Contains("apex") || id.Contains("tower_boss_f5"))
        {
            return new Color(0.85f, 0.18f, 0.22f);
        }

        if (id.Contains("m_boss_f2") || id.Contains("tower_boss_f2"))
        {
            return new Color(0.85f, 0.62f, 0.22f);
        }

        if (id.Contains("boss"))
        {
            return new Color(0.9f, 0.35f, 0.2f);
        }

        if (id.Contains("ember") || id.Contains("shadow"))
        {
            return id.Contains("shadow")
                ? new Color(0.45f, 0.25f, 0.55f)
                : new Color(1f, 0.45f, 0.2f);
        }

        if (id.Contains("gust"))
        {
            return new Color(0.55f, 0.9f, 1f);
        }

        if (id.Contains("brute") || id.Contains("beetle"))
        {
            return new Color(0.7f, 0.45f, 0.85f);
        }

        if (id.Contains("tide"))
        {
            return new Color(0.25f, 0.55f, 1f);
        }

        return new Color(0.2f, 1f, 0.25f);
    }

    private void ApplyMove(string json)
    {
        var id = JsonUtil.ExtractString(json, "entityId");
        if (string.IsNullOrEmpty(id) || !JsonUtil.TryNumber(json, "x", out var x) || !JsonUtil.TryNumber(json, "y", out var y))
        {
            return;
        }

        var isSelf = id == _selfId;
        if (isSelf)
        {
            // Local prediction owns the player sprite; sync_move only acks last-good in NetworkBootstrap.
            return;
        }

        var label = GetLabel(id, id);
        var tint = ColorForEntity(id, _entities.TryGetValue(id, out var existing) ? existing.Kind : "monster");
        var hasSpeed = JsonUtil.TryNumber(json, "speed", out var speed);
        var snap = hasSpeed && speed <= 0.001f;
        MoveTo(id, x, y, tint, label, GetHp(id, 40), GetMaxHp(id, 40), GetHitRadius(id, 0.4f),
            snap, hasSpeed ? speed : -1f);
    }

    private void ApplyVitals(string json)
    {
        var id = JsonUtil.ExtractString(json, "entityId");
        if (string.IsNullOrEmpty(id) || !_entities.TryGetValue(id, out var view))
        {
            return;
        }

        if (JsonUtil.TryInt(json, "hp", out var hp))
        {
            view.Hp = hp;
            if (hp > 0 && view.Dying)
            {
                _entities[id] = view;
                Revive(id);
                view = _entities[id];
            }
            else if (hp <= 0)
            {
                _entities[id] = view;
                MaybeDieFromHp(id);
                if (!_entities.TryGetValue(id, out view))
                {
                    return;
                }
            }
        }

        if (JsonUtil.TryInt(json, "maxHp", out var maxHp) && maxHp > 0)
        {
            view.MaxHp = maxHp;
        }

        if (JsonUtil.TryInt(json, "mp", out var mp))
        {
            view.Mp = mp;
        }

        if (JsonUtil.TryInt(json, "maxMp", out var maxMp) && maxMp > 0)
        {
            view.MaxMp = maxMp;
        }

        _entities[id] = view;
    }

    private void ApplyThreat(string json)
    {
        var monsterId = JsonUtil.ExtractString(json, "monsterId");
        if (string.IsNullOrEmpty(monsterId) || !_entities.TryGetValue(monsterId, out var view))
        {
            return;
        }

        var topId = JsonUtil.ExtractString(json, "topId");
        view.ThreatTopId = topId ?? "";

        var selfPct = 0f;
        var cursor = 0;
        while (cursor < json.Length)
        {
            var pIdx = json.IndexOf("\"playerId\"", cursor);
            if (pIdx < 0)
            {
                break;
            }

            var slice = json.Substring(pIdx, Mathf.Min(120, json.Length - pIdx));
            var playerId = JsonUtil.ExtractString(slice, "playerId");
            if (playerId == _selfId && JsonUtil.TryNumber(slice, "pct", out var pct))
            {
                selfPct = pct;
                break;
            }

            cursor = pIdx + 10;
        }

        view.ThreatSelfPct = selfPct;
        _entities[monsterId] = view;
    }

    private void ApplyStatus(string json)
    {
        var entityId = JsonUtil.ExtractString(json, "entityId");
        if (string.IsNullOrEmpty(entityId) || !_entities.TryGetValue(entityId, out var view))
        {
            return;
        }

        EnsureStatusLists(ref view);
        var prevKinds = new List<string>(view.StatusKinds);
        view.StatusKinds.Clear();
        view.StatusUntil.Clear();

        var statusesIdx = json.IndexOf("\"statuses\"");
        if (statusesIdx >= 0)
        {
            var part = json.Substring(statusesIdx);
            var cursor = 0;
            while (cursor < part.Length)
            {
                var kIdx = part.IndexOf("\"kind\"", cursor);
                if (kIdx < 0)
                {
                    break;
                }

                var slice = part.Substring(kIdx, Mathf.Min(160, part.Length - kIdx));
                var kind = JsonUtil.ExtractString(slice, "kind");
                if (string.IsNullOrEmpty(kind))
                {
                    cursor = kIdx + 6;
                    continue;
                }

                JsonUtil.TryNumber(slice, "until", out var until);
                view.StatusKinds.Add(kind);
                view.StatusUntil.Add(until);
                cursor = kIdx + 6;
            }
        }

        _entities[entityId] = view;

        for (var i = 0; i < view.StatusKinds.Count; i++)
        {
            var kind = view.StatusKinds[i];
            var wasPresent = false;
            for (var p = 0; p < prevKinds.Count; p++)
            {
                if (prevKinds[p] == kind)
                {
                    wasPresent = true;
                    break;
                }
            }

            if (!wasPresent && view.Transform != null)
            {
                // Status pop primitives removed.
            }
        }

        RebuildStatusMarkers(entityId);
    }

    private void ApplySkill(string json)
    {
        var targetId = JsonUtil.ExtractString(json, "targetId");
        var casterId = JsonUtil.ExtractString(json, "casterId");
        JsonUtil.TryInt(json, "damage", out var damage);
        var hasHp = JsonUtil.TryInt(json, "hpAfter", out var hpAfter);
        var skillId = JsonUtil.ExtractString(json, "skillId");
        PlayCastAttack(casterId, targetId, skillId);
        ApplyHitFx(targetId, damage, hpAfter, skillId, json, hasHp);
        GameLog.DebugLine(GameLog.Channel.Gfx,
            "hitfx  target=" + targetId + "  skill=" + skillId +
            "  dmg=" + damage + "  hp=" + hpAfter + "  parent=entity_tile");
    }

    private string MapCasterId(string casterId)
    {
        if (string.IsNullOrEmpty(casterId))
        {
            return _selfId;
        }

        // Server you-id often differs from local_you; treat self hits via entity map.
        if (_entities.ContainsKey(casterId))
        {
            return casterId;
        }

        return _selfId;
    }

    private void FaceEntityToward(string fromId, string toId)
    {
        var id = MapCasterId(fromId);
        if (!_entities.TryGetValue(id, out var view) || view.Transform == null)
        {
            return;
        }

        if (string.IsNullOrEmpty(toId) || !_entities.TryGetValue(toId, out var target) || target.Transform == null)
        {
            return;
        }

        UpdateFacingFromDelta(ref view, target.Transform.position - view.Transform.position);
        _entities[id] = view;
    }

    private void ApplyAoe(string json)
    {
        var skillId = JsonUtil.ExtractString(json, "skillId");
        var casterId = JsonUtil.ExtractString(json, "casterId");
        var centerId = JsonUtil.ExtractString(json, "centerId");
        PlayCastAttack(casterId, centerId, skillId);
        var radius = 2.5f;
        if (JsonUtil.TryNumber(json, "aoeRadius", out var r) && r > 0)
        {
            radius = r;
        }

        if (skillId != "thunderstorm" && skillId != "explosion")
        {
            SpawnAoeRing(centerId, radius, 0.35f);
        }

        var cursor = 0;
        while (true)
        {
            var idx = json.IndexOf("\"targetId\"", cursor);
            if (idx < 0)
            {
                break;
            }

            var slice = json.Substring(idx, Mathf.Min(180, json.Length - idx));
            var targetId = JsonUtil.ExtractString(slice, "targetId");
            JsonUtil.TryInt(slice, "damage", out var damage);
            var hasHp = JsonUtil.TryInt(slice, "hpAfter", out var hpAfter);
            ApplyHitFx(targetId, damage, hpAfter, skillId, slice, hasHp);

            cursor = idx + 10;
        }
    }

    public void SpawnAoeRing(string centerId, float radius, float life)
    {
        var pos = GetEntityWorldPos(centerId);
        if (!pos.HasValue)
        {
            return;
        }

        var d = Mathf.Max(0.8f, radius * 2f);
        SpawnRingFx(pos.Value, d * 0.35f, d, new Color(1f, 0.82f, 0.25f, 0.7f),
            new Color(1f, 0.55f, 0.1f, 0f), Mathf.Max(0.18f, life), 0f);
    }

    private void ApplyHitFx(string targetId, int damage, int hpAfter, string skillId, string json = null, bool hpKnown = true)
    {
        if (string.IsNullOrEmpty(targetId) || !_entities.TryGetValue(targetId, out var view))
        {
            return;
        }

        if (hpKnown)
        {
            view.Hp = hpAfter;
        }
        if (view.Renderer != null)
        {
            view.Renderer.color = targetId == _selfId
                ? SpriteCatalog.ClassTint(_classId)
                : (view.UsesArt ? SpriteCatalog.MonsterTint(targetId) : view.BaseColor);
        }

        _entities[targetId] = view;
        var healish = skillId == "mend" || (skillId != null && skillId.Contains("heal"));
        var missed = json != null && json.Contains("\"missed\":true");
        var dead = view.Hp <= 0 || (hpKnown && hpAfter <= 0);
        if (!healish && dead)
        {
            BeginDeath(targetId);
        }
        else if (!healish && damage > 0)
        {
            PlayHurtAnim(targetId);
        }

        if (!healish && !missed && damage > 0 && view.Transform != null)
        {
            var spark = ElementFloatColor(json, false);
            SpawnImpactSpark(FxAtEntity(targetId, 0.12f), spark);
            SoundCatalog.Play(SoundCatalog.Id.Hit);
            if (skillId == "stun_bolt")
            {
                SpawnClipFx(VfxCatalog.LightningBurst(), FxAtEntity(targetId, 0.2f), 1.4f, VfxCatalog.DefaultClipFps);
            }
            if (json != null && json.Contains("\"crit\":true"))
            {
                SpawnBurstFx(FxAtEntity(targetId, 0.2f), new Color(1f, 0.85f, 0.25f, 0.95f), 0.9f, 0.22f);
                SoundCatalog.Play(SoundCatalog.Id.Crit);
            }
        }
        else if (healish && view.Transform != null)
        {
            SpawnGlowFx(targetId, new Color(0.35f, 1f, 0.55f, 0.85f), 0.55f);
        }

        var text = missed ? "MISS" : (healish ? "+" + Mathf.Max(1, damage > 0 ? damage : 20) : "-" + damage);
        var color = healish ? new Color(0.4f, 1f, 0.5f) : ElementFloatColor(json, missed);
        var crit = json != null && json.Contains("\"crit\":true");
        if (crit)
        {
            color = Color.Lerp(color, new Color(1f, 0.9f, 0.3f), 0.45f);
        }

        if ((damage > 0 || healish || missed) && view.Transform != null)
        {
            _floats.Add(new FloatText
            {
                World = FxAtEntity(targetId, 0.35f),
                Text = text,
                Until = Time.time + 0.9f,
                Color = color,
                Size = missed ? 16 : (crit ? 30 : 22),
            });
        }
    }

    private static Color ElementFloatColor(string json, bool missed)
    {
        if (missed)
        {
            return new Color(0.75f, 0.75f, 0.8f);
        }

        var element = json != null ? JsonUtil.ExtractString(json, "element") : "";
        var adv = json != null ? JsonUtil.ExtractString(json, "advantage") : "";
        Color c;
        switch (element)
        {
            case "water":
                c = new Color(0.35f, 0.65f, 1f);
                break;
            case "fire":
                c = new Color(1f, 0.4f, 0.2f);
                break;
            case "wind":
                c = new Color(0.45f, 1f, 0.55f);
                break;
            case "earth":
                c = new Color(0.9f, 0.75f, 0.3f);
                break;
            case "holy":
            case "light":
                c = new Color(1f, 0.95f, 0.7f);
                break;
            case "dark":
            case "shadow":
                c = new Color(0.65f, 0.4f, 1f);
                break;
            default:
                c = new Color(1f, 0.35f, 0.25f);
                break;
        }

        if (adv == "advantage")
        {
            c = Color.Lerp(c, Color.white, 0.25f);
        }
        else if (adv == "disadvantage")
        {
            c = Color.Lerp(c, Color.gray, 0.35f);
        }

        if (json != null && json.Contains("\"crit\":true"))
        {
            c = Color.Lerp(c, new Color(1f, 0.85f, 0.2f), 0.4f);
        }

        return c;
    }

    private bool CanDie(string id, EntityView view)
    {
        if (view.Kind == "npc" || view.Kind == "portal")
        {
            return false;
        }

        if (!string.IsNullOrEmpty(id) && (id.StartsWith("npc_") || id.StartsWith("portal_")))
        {
            return false;
        }

        return true;
    }

    private void MaybeDieFromHp(string id)
    {
        if (!_entities.TryGetValue(id, out var view) || view.Dying || view.Hp > 0)
        {
            return;
        }

        if (!CanDie(id, view))
        {
            return;
        }

        BeginDeath(id);
    }

    public void Revive(string id)
    {
        if (!_entities.TryGetValue(id, out var view))
        {
            return;
        }

        view.Dying = false;
        view.DeathRemoveAt = 0f;
        view.AnimOneShot = false;
        view.AnimClip = SpriteCatalog.Clip.Idle;
        view.AnimLockUntil = 0f;
        _entities[id] = view;
    }

    private void SpawnGroundDisc(Vector3 centerXy, float diameter, Color color, float life)
    {
        SpawnRingFx(centerXy, diameter * 0.35f, diameter, color,
            new Color(color.r, color.g, color.b, 0f), Mathf.Max(0.12f, life), 0f);
    }

    public void Despawn(string id, bool playDeath = true)
    {
        if (string.IsNullOrEmpty(id) || !_entities.TryGetValue(id, out var view))
        {
            return;
        }

        if (view.Dying)
        {
            return;
        }

        if (playDeath && view.UsesArt)
        {
            BeginDeath(id);
            return;
        }

        FinishDespawn(id);
    }

    private void BeginDeath(string id)
    {
        if (!_entities.TryGetValue(id, out var view) || view.Dying)
        {
            return;
        }

        view.Dying = true;
        view.Moving = false;
        view.Hp = 0;
        view.AnimClip = SpriteCatalog.Clip.Death;
        view.AnimOneShot = false;
        view.AnimLockUntil = Time.time + 2f;
        view.AnimFrame = 0;
        view.AnimTime = 0f;
        view.AnimFacingRow = -1;
        view.DeathRemoveAt = Time.time + 1.1f;
        _entities[id] = view;
        if (view.Transform != null)
        {
            SpawnBurstFx(view.Transform.position + Vector3.up * 0.35f,
                new Color(0.85f, 0.9f, 1f, 0.8f), 1.15f, 0.45f);
        }

        SoundCatalog.Play(SoundCatalog.Id.Death);
    }

    private void FinishDespawn(string id)
    {
        if (!_entities.TryGetValue(id, out var view))
        {
            return;
        }

        ClearStatusMarkers(id);
        if (view.Transform != null)
        {
            Destroy(view.Transform.gameObject);
        }

        _entities.Remove(id);
        if (_lockTargetId == id)
        {
            CycleLockTarget();
        }
    }

    public void SpawnLocalSkillshot(string skillId, Vector3 from, float dx, float dy, float speed)
    {
        var len = Mathf.Sqrt(dx * dx + dy * dy);
        if (len < 1e-4f)
        {
            dx = 1f;
            dy = 0f;
        }
        else
        {
            dx /= len;
            dy /= len;
        }

        var id = "local_" + skillId;
        UpsertBolt(id, from.x, from.z, dx, dy, speed > 0.1f ? speed : 16f, skillId, "", _selfId);
    }

    private void TickBolts()
    {
        if (_projectiles.Count == 0)
        {
            return;
        }

        var keys = new List<string>(_projectiles.Keys);
        for (var i = 0; i < keys.Count; i++)
        {
            var id = keys[i];
            if (!_projectiles.TryGetValue(id, out var bolt) || bolt.Transform == null)
            {
                continue;
            }

            var vel = bolt.Vel;
            if (!string.IsNullOrEmpty(bolt.TargetId) &&
                _entities.TryGetValue(bolt.TargetId, out var tgt) &&
                tgt.Transform != null && tgt.Hp > 0)
            {
                var to = tgt.Transform.position - bolt.Transform.position;
                var d = WorldCoords.MapXZ(to);
                if (d.sqrMagnitude > 0.0001f)
                {
                    vel = d.normalized;
                    bolt.Vel = vel;
                }
            }

            if (vel.sqrMagnitude < 0.0001f)
            {
                continue;
            }

            var step = vel.normalized * (bolt.Speed * Time.deltaTime);
            var p = bolt.Transform.position;
            p.x += step.x;
            p.z += step.y;
            p.y = 0.45f;
            bolt.Transform.position = p;
            FaceBolt(bolt.Transform, vel);
            if (bolt.Frames != null && bolt.Frames.Length > 0 && bolt.Renderer != null)
            {
                bolt.AnimT += Time.deltaTime * VfxCatalog.DefaultSheetFps;
                var idx = Mathf.FloorToInt(bolt.AnimT) % bolt.Frames.Length;
                bolt.Renderer.sprite = bolt.Frames[idx];
            }

            _projectiles[id] = bolt;
            if (TryStopBoltOnSprite(id, ref bolt))
            {
                continue;
            }
        }
    }

    private bool TryStopBoltOnSprite(string id, ref BoltView bolt)
    {
        if (bolt.Transform == null)
        {
            return false;
        }

        var p = bolt.Transform.position;
        var caster = MapCasterId(bolt.CasterId);
        var hitId = "";
        var best = float.MaxValue;
        Vector3 snap = p;
        foreach (var pair in _entities)
        {
            if (pair.Key == bolt.CasterId || pair.Key == caster)
            {
                continue;
            }

            var view = pair.Value;
            if (view.Transform == null || view.Hp <= 0 || view.Dying || !IsCombatTargetKind(view.Kind, pair.Key))
            {
                continue;
            }

            Vector3 center;
            float reach;
            if (view.Renderer != null)
            {
                var b = view.Renderer.bounds;
                center = b.center;
                reach = Mathf.Max(b.extents.x, b.extents.y) * 0.92f;
            }
            else
            {
                center = view.Transform.position;
                var scale = Mathf.Max(0.5f, view.Transform.localScale.x);
                reach = Mathf.Max(view.HitRadius, 0.45f) + 0.35f * scale;
            }

            var d = WorldCoords.MapDistance(p, center);
            if (d > reach || d >= best)
            {
                continue;
            }

            best = d;
            hitId = pair.Key;
            if (view.Renderer != null)
            {
                var closest = view.Renderer.bounds.ClosestPoint(p);
                snap = WorldCoords.Lift(closest, 0.45f);
            }
            else
            {
                var dir = center - p;
                if (dir.sqrMagnitude > 1e-6f)
                {
                    dir.Normalize();
                    snap = center - dir * Mathf.Max(0.05f, view.HitRadius);
                    snap.y = 0.45f;
                }
            }
        }

        if (string.IsNullOrEmpty(hitId))
        {
            return false;
        }

        bolt.Transform.position = snap;
        _projectiles[id] = bolt;
        DespawnProjectile(id);
        return true;
    }

    private void ApplyProjectileSpawn(string json)
    {
        var slice = JsonUtil.SliceAround(json, "\"projectile\"", 0, 900);
        var src = slice.Length > 0 ? slice : json;
        var id = JsonUtil.ExtractString(src, "id");
        if (string.IsNullOrEmpty(id) || !JsonUtil.TryNumber(src, "x", out var x) || !JsonUtil.TryNumber(src, "y", out var y))
        {
            return;
        }

        var skillId = JsonUtil.ExtractString(src, "skillId") ?? "";
        var casterId = JsonUtil.ExtractString(src, "casterId");
        var targetId = JsonUtil.ExtractString(src, "targetId");
        PlayCastAttack(casterId, targetId, skillId);

        JsonUtil.TryNumber(src, "vx", out var vx);
        JsonUtil.TryNumber(src, "vy", out var vy);
        JsonUtil.TryNumber(src, "speed", out var speed);
        if (speed < 0.1f)
        {
            speed = 16f;
        }

        var localId = "local_" + skillId;
        if (!string.IsNullOrEmpty(casterId) && (casterId == _selfId || MapCasterId(casterId) == _selfId) &&
            _projectiles.TryGetValue(localId, out var local) && local.Transform != null)
        {
            _projectiles.Remove(localId);
            if (vx * vx + vy * vy > 0.0001f)
            {
                local.Vel = new Vector2(vx, vy).normalized;
            }

            local.Speed = speed;
            local.SkillId = skillId;
            local.TargetId = targetId;
            local.CasterId = casterId;
            _projectiles[id] = local;
            return;
        }

        UpsertBolt(id, x, y, vx, vy, speed, skillId, targetId, casterId);
    }

    private void ApplyProjectileMove(string json)
    {
        var id = JsonUtil.ExtractString(json, "id");
        if (string.IsNullOrEmpty(id) || !_projectiles.TryGetValue(id, out var bolt) || bolt.Transform == null)
        {
            return;
        }

        if (!JsonUtil.TryNumber(json, "x", out var x) || !JsonUtil.TryNumber(json, "y", out var y))
        {
            return;
        }

        var cur = bolt.Transform.position;
        var nx = Mathf.Lerp(cur.x, x, 0.45f);
        var nz = Mathf.Lerp(cur.z, y, 0.45f);
        var dx = x - cur.x;
        var dy = y - cur.z;
        if (dx * dx + dy * dy > 0.0004f)
        {
            bolt.Vel = new Vector2(dx, dy).normalized;
            FaceBolt(bolt.Transform, bolt.Vel);
        }

        bolt.Transform.position = WorldCoords.TileToWorld(nx, nz, 0.45f);
        _projectiles[id] = bolt;
        TryStopBoltOnSprite(id, ref bolt);
    }

    private void DespawnProjectile(string id, bool withImpact = true)
    {
        if (string.IsNullOrEmpty(id) || !_projectiles.TryGetValue(id, out var bolt))
        {
            return;
        }

        if (withImpact && bolt.Transform != null)
        {
            SpawnImpactSpark(bolt.Transform.position, bolt.Color);
        }

        if (bolt.Transform != null)
        {
            Destroy(bolt.Transform.gameObject);
        }

        _projectiles.Remove(id);
    }

    private void UpsertBolt(
        string id,
        float x,
        float y,
        float vx,
        float vy,
        float speed,
        string skillId,
        string targetId,
        string casterId)
    {
        DespawnProjectile(id, false);
        var vel = new Vector2(vx, vy);
        if (vel.sqrMagnitude < 0.0001f && !string.IsNullOrEmpty(targetId) &&
            _entities.TryGetValue(targetId, out var tgt) && tgt.Transform != null)
        {
            vel = WorldCoords.MapXZ(tgt.Transform.position) - new Vector2(x, y);
        }

        if (vel.sqrMagnitude < 0.0001f)
        {
            vel = Vector2.right;
        }

        vel.Normalize();
        var color = BoltColor(skillId);
        var frames = BoltFrames(skillId);
        var go = CreateBoltObject(id, color, frames);
        go.transform.SetParent(_root, false);
        go.transform.position = WorldCoords.TileToWorld(x, y, 0.45f);
        FaceBolt(go.transform, vel);
        _projectiles[id] = new BoltView
        {
            Transform = go.transform,
            Renderer = go.GetComponent<SpriteRenderer>(),
            Vel = vel,
            Speed = speed > 0.1f ? speed : 16f,
            SkillId = skillId,
            TargetId = targetId ?? "",
            CasterId = casterId ?? "",
            Color = color,
            Frames = frames,
            AnimT = 0f,
        };
        GameLog.Info(GameLog.Channel.Gfx, "bolt spawn " + skillId + " @" + x.ToString("0.0") + "," + y.ToString("0.0"));
    }

    private void FaceBolt(Transform t, Vector2 vel)
    {
        if (t == null)
        {
            return;
        }

        if (_cam != null)
        {
            var look = _cam.transform.rotation;
            if (vel.sqrMagnitude > 0.0001f)
            {
                var worldVel = WorldCoords.MapDir3(vel);
                var local = Quaternion.Inverse(look) * worldVel;
                var ang = Mathf.Atan2(local.y, local.x) * Mathf.Rad2Deg;
                t.rotation = look * Quaternion.Euler(0f, 0f, ang);
            }
            else
            {
                t.rotation = look;
            }

            return;
        }

        if (vel.sqrMagnitude < 0.0001f)
        {
            return;
        }

        var fallback = Mathf.Atan2(vel.y, vel.x) * Mathf.Rad2Deg;
        t.rotation = Quaternion.Euler(0f, 0f, fallback);
    }

    private static Color BoltColor(string skillId)
    {
        if (skillId == "shot")
        {
            return new Color(0.55f, 1f, 0.4f, 1f);
        }

        if (skillId == "stun_bolt")
        {
            return new Color(1f, 0.88f, 0.35f, 1f);
        }

        if (skillId != null && skillId.Contains("ember"))
        {
            return new Color(1f, 0.45f, 0.18f, 1f);
        }

        return new Color(1f, 0.92f, 0.45f, 1f);
    }

    private static Sprite[] BoltFrames(string skillId)
    {
        if (skillId != null && skillId.Contains("ember"))
        {
            return VfxCatalog.EmberBolt();
        }

        return VfxCatalog.MagicBolt();
    }

    private static GameObject CreateBoltObject(string id, Color color, Sprite[] frames)
    {
        var go = new GameObject("bolt_" + id);
        var sr = go.AddComponent<SpriteRenderer>();
        if (frames != null && frames.Length > 0 && frames[0] != null)
        {
            sr.sprite = frames[0];
            sr.color = Color.white;
            go.transform.localScale = new Vector3(1.15f, 1.15f, 1f);
        }
        else
        {
            sr.sprite = MakeBoltSprite();
            sr.color = color;
            go.transform.localScale = new Vector3(1.65f, 0.7f, 1f);
        }

        sr.sortingOrder = 40;
        ApplyUnlit(sr);
        return go;
    }

    private static Sprite _boltSprite;
    private static Sprite _fxRingSprite;
    private static Sprite _glowSprite;
    private static Sprite _slashSprite;
    private static Sprite _sparkSprite;

    private static Sprite MakeBoltSprite()
    {
        if (_boltSprite != null)
        {
            return _boltSprite;
        }

        const int w = 48;
        const int h = 16;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        var pixels = new Color[w * h];
        var cx = (w - 1) * 0.5f;
        var cy = (h - 1) * 0.5f;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var nx = (x - cx) / cx;
                var ny = (y - cy) / cy;
                var taper = 1f - Mathf.Max(0f, nx) * 0.85f;
                var inBody = Mathf.Abs(ny) < 0.42f * Mathf.Max(0.15f, taper) && nx > -0.92f && nx < 0.98f;
                var inHead = nx > 0.35f && Mathf.Abs(ny) < (0.95f - nx);
                pixels[y * w + x] = (inBody || inHead) ? Color.white : Color.clear;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        _boltSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.35f, 0.5f), 28f);
        return _boltSprite;
    }

    private static Sprite MakeFxRingSprite()
    {
        if (_fxRingSprite != null)
        {
            return _fxRingSprite;
        }

        const int s = 64;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        var pixels = new Color[s * s];
        var c = (s - 1) * 0.5f;
        for (var y = 0; y < s; y++)
        {
            for (var x = 0; x < s; x++)
            {
                var nx = (x - c) / c;
                var ny = (y - c) / c;
                var d = Mathf.Sqrt(nx * nx + ny * ny);
                var a = 1f - Mathf.Abs(d - 0.78f) / 0.22f;
                pixels[y * s + x] = a > 0f ? new Color(1f, 1f, 1f, a * a) : Color.clear;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        _fxRingSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), s);
        return _fxRingSprite;
    }

    private static Sprite MakeGlowSprite()
    {
        if (_glowSprite != null)
        {
            return _glowSprite;
        }

        const int s = 48;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        var pixels = new Color[s * s];
        var c = (s - 1) * 0.5f;
        for (var y = 0; y < s; y++)
        {
            for (var x = 0; x < s; x++)
            {
                var nx = (x - c) / c;
                var ny = (y - c) / c;
                var d = Mathf.Sqrt(nx * nx + ny * ny);
                var a = Mathf.Clamp01(1f - d);
                pixels[y * s + x] = a > 0.02f ? new Color(1f, 1f, 1f, a * a) : Color.clear;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        _glowSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), s);
        return _glowSprite;
    }

    private static Sprite MakeSlashSprite()
    {
        if (_slashSprite != null)
        {
            return _slashSprite;
        }

        const int w = 48;
        const int h = 32;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        var pixels = new Color[w * h];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var nx = x / (float)(w - 1);
                var ny = (y - (h - 1) * 0.5f) / ((h - 1) * 0.5f);
                var arc = ny - (nx * nx * 1.4f - 0.7f);
                var a = 1f - Mathf.Abs(arc) / 0.28f;
                a *= 1f - Mathf.Abs(nx - 0.55f) * 1.1f;
                pixels[y * w + x] = a > 0f ? new Color(1f, 1f, 1f, Mathf.Clamp01(a)) : Color.clear;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        _slashSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.2f, 0.5f), 28f);
        return _slashSprite;
    }

    private static Sprite MakeSparkSprite()
    {
        if (_sparkSprite != null)
        {
            return _sparkSprite;
        }

        const int s = 24;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        var pixels = new Color[s * s];
        var c = (s - 1) * 0.5f;
        for (var y = 0; y < s; y++)
        {
            for (var x = 0; x < s; x++)
            {
                var nx = Mathf.Abs(x - c) / c;
                var ny = Mathf.Abs(y - c) / c;
                var cross = Mathf.Min(nx, ny);
                var a = 1f - cross / 0.22f;
                a *= 1f - Mathf.Max(nx, ny) * 0.35f;
                pixels[y * s + x] = a > 0f ? new Color(1f, 1f, 1f, Mathf.Clamp01(a)) : Color.clear;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        _sparkSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), s);
        return _sparkSprite;
    }

    private void SpawnFx(Sprite sprite, Vector3 pos, Vector3 scale0, Vector3 scale1,
        Color color0, Color color1, float life, float spinDeg, float faceZ,
        string followId, Vector3 followOff, int order)
    {
        var go = new GameObject("fx");
        if (_root != null)
        {
            go.transform.SetParent(_root, false);
        }

        go.transform.position = pos;
        go.transform.localScale = scale0;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color0;
        sr.sortingOrder = order;
        ApplyUnlit(sr);
        var born = Time.time;
        _tempFx.Add(new TempFx
        {
            Go = go,
            Sr = sr,
            Born = born,
            Until = born + Mathf.Max(0.05f, life),
            Scale0 = scale0,
            Scale1 = scale1,
            Color0 = color0,
            Color1 = color1,
            SpinDeg = spinDeg,
            FaceZ = faceZ,
            FollowId = followId ?? "",
            FollowOff = followOff,
            Frames = null,
            Fps = 0f,
            Loop = false,
        });
    }

    private void SpawnClipFx(Sprite[] frames, Vector3 pos, float worldSize, float fps)
    {
        if (frames == null || frames.Length == 0)
        {
            return;
        }

        var life = frames.Length / Mathf.Max(8f, fps);
        var p = WorldCoords.Lift(pos, 0.55f);
        var scale = Vector3.one * worldSize;
        var go = new GameObject("fx_clip");
        if (_root != null)
        {
            go.transform.SetParent(_root, false);
        }

        go.transform.position = p;
        go.transform.localScale = scale;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = frames[0];
        sr.color = Color.white;
        sr.sortingOrder = 48;
        ApplyUnlit(sr);
        var born = Time.time;
        _tempFx.Add(new TempFx
        {
            Go = go,
            Sr = sr,
            Born = born,
            Until = born + life,
            Scale0 = scale,
            Scale1 = scale,
            Color0 = Color.white,
            Color1 = Color.white,
            SpinDeg = 0f,
            FaceZ = 0f,
            FollowId = "",
            FollowOff = Vector3.zero,
            Frames = frames,
            Fps = fps,
            Loop = false,
        });
    }

    private void SpawnThunderstormFx(Vector3 center)
    {
        var bolt = VfxCatalog.LightningBurst();
        if (bolt == null || bolt.Length == 0)
        {
            SpawnBurstFx(center, new Color(0.7f, 0.85f, 1f, 0.9f), 1.2f, 0.4f);
            return;
        }

        var right = _cam != null ? WorldCoords.MapXZ(_cam.transform.right) : Vector2.right;
        if (right.sqrMagnitude < 1e-6f)
        {
            right = Vector2.right;
        }

        right.Normalize();
        for (var i = 0; i < 5; i++)
        {
            var along = (i - 2) * 0.85f;
            var p = center + new Vector3(right.x * along, 0f, right.y * along);
            SpawnClipFx(bolt, p, 1.55f, VfxCatalog.DefaultClipFps);
        }
    }

    private void SpawnNuclearRingFx(Vector3 center)
    {
        var boom = VfxCatalog.NuclearBurst();
        if (boom == null || boom.Length == 0)
        {
            SpawnBurstFx(center, new Color(1f, 0.55f, 0.15f, 0.9f), 1.4f, 0.45f);
            return;
        }

        for (var i = 0; i < 5; i++)
        {
            var rad = i * (Mathf.PI * 2f / 5f) - Mathf.PI * 0.5f;
            var p = center + new Vector3(Mathf.Cos(rad) * 1.35f, 0f, Mathf.Sin(rad) * 1.35f);
            SpawnClipFx(boom, p, 1.7f, 14f);
        }
    }

    private void SpawnRingFx(Vector3 pos, float from, float to, Color c0, Color c1, float life, float spin)
    {
        var p = WorldCoords.Lift(pos, 0.05f);
        SpawnFx(MakeFxRingSprite(), p, Vector3.one * from, Vector3.one * to, c0, c1, life, spin, 0f, "", Vector3.zero, 42);
    }

    private void SpawnGlowFx(string entityId, Color color, float life)
    {
        SpawnFx(MakeGlowSprite(), Vector3.zero, Vector3.one * 0.7f, Vector3.one * 1.65f,
            color, new Color(color.r, color.g, color.b, 0f), life, 40f, 0f,
            entityId, new Vector3(0f, 0.45f, 0f), 44);
    }

    private void SpawnBurstFx(Vector3 pos, Color color, float size, float life)
    {
        SpawnFx(MakeGlowSprite(), WorldCoords.Lift(pos, 0.4f),
            Vector3.one * (size * 0.35f), Vector3.one * size,
            color, new Color(color.r, color.g, color.b, 0f), life, 0f, 0f, "", Vector3.zero, 46);
    }

    private void SpawnSlashFx(Vector3 pos, float faceZ, Color color)
    {
        SpawnFx(MakeSlashSprite(), WorldCoords.Lift(pos, 0.5f),
            new Vector3(0.55f, 0.55f, 1f), new Vector3(1.15f, 0.85f, 1f),
            color, new Color(color.r, color.g, color.b, 0f), 0.2f, 0f, faceZ, "", Vector3.zero, 47);
    }

    private void SpawnDashTrail(string casterId, Vector2 dir)
    {
        var origin = GetEntityWorldPos(casterId);
        if (!origin.HasValue)
        {
            return;
        }

        var c0 = new Color(0.55f, 0.9f, 1f, 0.7f);
        var c1 = new Color(0.35f, 0.7f, 1f, 0f);
        for (var i = 0; i < 3; i++)
        {
            var along = WorldCoords.AlongMap(origin.Value, -dir, 0.28f * (i + 1));
            along.y = 0.35f;
            var delayScale = 0.55f - i * 0.1f;
            SpawnFx(MakeGlowSprite(), along, Vector3.one * delayScale, Vector3.one * (delayScale + 0.45f),
                c0, c1, 0.22f + i * 0.05f, 0f, 0f, "", Vector3.zero, 41);
        }

        SpawnBurstFx(origin.Value, new Color(0.65f, 0.95f, 1f, 0.85f), 0.85f, 0.2f);
    }

    private void SpawnHookLine(Vector3 from, Vector3 to)
    {
        var mid = (from + to) * 0.5f;
        mid.y = 0.45f;
        var delta = to - from;
        var map = WorldCoords.MapXZ(delta);
        var len = map.magnitude;
        var faceZ = Mathf.Atan2(map.y, map.x) * Mathf.Rad2Deg;
        var scale0 = new Vector3(Mathf.Max(0.4f, len), 0.18f, 1f);
        SpawnFx(MakeBoltSprite(), mid, scale0, scale0 * 1.05f,
            new Color(0.95f, 0.85f, 0.45f, 0.95f), new Color(1f, 0.7f, 0.2f, 0f),
            0.22f, 0f, faceZ, "", Vector3.zero, 48);
    }

    private void SpawnImpactSpark(Vector3 pos, Color color)
    {
        SpawnFx(MakeSparkSprite(), WorldCoords.Lift(pos, 0.55f),
            Vector3.one * 0.35f, Vector3.one * 0.85f,
            color, new Color(color.r, color.g, color.b, 0f), 0.16f, 180f, 0f, "", Vector3.zero, 49);
    }

    private void ApplySpawn(string json)
    {
        var entitySlice = JsonUtil.SliceAround(json, "\"entity\"", 0, 420);
        var id = JsonUtil.ExtractString(entitySlice, "id");
        if (string.IsNullOrEmpty(id))
        {
            id = JsonUtil.ExtractString(json, "id");
        }

        var src = entitySlice.Length > 0 ? entitySlice : json;
        if (!JsonUtil.TryNumber(src, "x", out var x) || !JsonUtil.TryNumber(src, "y", out var y))
        {
            return;
        }

        var hasHp = JsonUtil.TryInt(src, "hp", out var hp);
        JsonUtil.TryInt(src, "maxHp", out var maxHp);
        JsonUtil.TryInt(src, "mp", out var mp);
        JsonUtil.TryInt(src, "maxMp", out var maxMp);
        JsonUtil.TryNumber(src, "hitRadius", out var hr);
        var name = JsonUtil.ExtractString(src, "name");
        var kind = JsonUtil.ExtractString(src, "kind");
        var label = !string.IsNullOrEmpty(name) ? name : (id.Contains("slime") ? "Slime" : id);
        var inferredKind = !string.IsNullOrEmpty(kind)
            ? kind
            : (id.StartsWith("npc_")
                ? "npc"
                : (id.StartsWith("monster") || id.StartsWith("lab_") || id.Contains("slime") ||
                   id.Contains("dummy") || id.Contains("ragdoll") || id.Contains("cannon")
                    ? "monster"
                    : "player"));
        var color = inferredKind == "player" && id != _selfId
            ? new Color(0.35f, 0.75f, 1f)
            : ColorForEntity(id, inferredKind);
        var fallbackHp = inferredKind == "npc" ? 1 : 40;
        Upsert(id, x, y, color, label, hasHp ? hp : fallbackHp, maxHp > 0 ? maxHp : fallbackHp, hr > 0 ? hr : 0.4f, true);
        SetEntityMeta(id, inferredKind, mp, maxMp);
        if (inferredKind != "player" &&
            (string.IsNullOrEmpty(_lockTargetId) || !_entities.ContainsKey(_lockTargetId)))
        {
            SetLockTarget(id);
        }
    }

    private void RemapSelf(string newId)
    {
        if (newId == _selfId)
        {
            return;
        }

        if (_entities.TryGetValue(_selfId, out var view))
        {
            _entities.Remove(_selfId);
            _entities[newId] = view;
            if (view.Transform != null)
            {
                view.Transform.name = "You";
            }

            if (_statusMarkers.TryGetValue(_selfId, out var markers))
            {
                _statusMarkers.Remove(_selfId);
                _statusMarkers[newId] = markers;
            }
        }

        _selfId = newId;
    }

    private void MoveTo(string id, float x, float y, Color color, string label, int hp, int maxHp, float hitRadius,
        bool instant = false, float moveSpeed = -1f)
    {
        Upsert(id, x, y, color, label, hp, maxHp, hitRadius, instant, moveSpeed);
    }

    private void Upsert(string id, float x, float y, Color color, string label, int hp, int maxHp, float hitRadius,
        bool instant, float moveSpeed = -1f)
    {
        if (!_entities.TryGetValue(id, out var view))
        {
            var kindGuess = id == _selfId || (!string.IsNullOrEmpty(id) && id.StartsWith("player_"))
                ? "player"
                : (!string.IsNullOrEmpty(id) && id.StartsWith("npc_"))
                    ? "npc"
                    : (!string.IsNullOrEmpty(id) && id.StartsWith("portal_"))
                        ? "portal"
                        : "monster";
            var go = CreateMarker(color, label, id, kindGuess);
            go.transform.SetParent(_root, false);
            var ring = CreateHitRing();
            ring.transform.SetParent(go.transform, false);
            view = new EntityView
            {
                Transform = go.transform,
                HitRing = ring.transform,
                Renderer = go.GetComponent<SpriteRenderer>(),
                BaseColor = color,
                Label = label,
                Kind = kindGuess,
                ThreatTopId = "",
                StatusKinds = new List<string>(),
                StatusUntil = new List<float>(),
                HitRadius = hitRadius,
                From = WorldCoords.TileToWorld(x, y),
                To = WorldCoords.TileToWorld(x, y),
                UsesArt = SpriteCatalog.HasArtSprite(id, kindGuess),
                AnimClip = SpriteCatalog.Clip.Idle,
                AnimFrame = 0,
                AnimTime = 0f,
            };
            view.Transform.position = view.To;
            EnsureGroundShadow(ref view);
        }

        EnsureStatusLists(ref view);
        if (view.Dying && hp <= 0)
        {
            return;
        }

        if (view.Dying && hp > 0)
        {
            _entities[id] = view;
            Revive(id);
            if (!_entities.TryGetValue(id, out view))
            {
                return;
            }
        }

        view.BaseColor = color;
        view.Hp = hp;
        view.MaxHp = maxHp;
        view.Label = label;
        // Keep art sprites untinted; only recolor procedural markers
        if (view.Renderer != null && !SpriteCatalog.HasArtSprite(id, view.Kind))
        {
            view.Renderer.color = color;
            view.UsesArt = false;
        }
        else if (view.Renderer != null)
        {
            view.Renderer.color = id == _selfId
                ? SpriteCatalog.ClassTint(_classId)
                : SpriteCatalog.MonsterTint(id);
            view.UsesArt = true;
        }
        view.HitRadius = hitRadius > 0 ? hitRadius : 0.4f;
        if (view.HitRing != null)
        {
            var d = view.HitRadius * 2f;
            view.HitRing.localScale = new Vector3(d, d, 1f);
            view.HitRing.localPosition = new Vector3(0f, 0.02f, 0f);
            view.HitRing.localRotation = Quaternion.Euler(90f, 0f, 0f);
            // Only show rings for procedural markers (no sheet art).
            view.HitRing.gameObject.SetActive(!view.UsesArt);
        }

        var target = WorldCoords.TileToWorld(x, y);
        if (moveSpeed > 0f)
        {
            view.MoveSpeed = moveSpeed;
        }

        if (instant || view.Transform == null)
        {
            view.From = target;
            view.To = target;
            view.Moving = false;
            view.MoveT = 1f;
            view.MoveDur = MoveDuration;
            if (view.Transform != null)
            {
                view.Transform.position = target;
            }
        }
        else
        {
            view.From = view.Transform.position;
            view.To = target;
            view.MoveT = 0f;
            view.Moving = true;
            var dist = WorldCoords.MapDistance(view.From, view.To);
            var speed = view.MoveSpeed > 0.01f ? view.MoveSpeed : 0f;
            view.MoveDur = speed > 0.01f
                ? Mathf.Clamp(dist / speed, MinMoveDur, MaxMoveDur)
                : MoveDuration;
            if (id != _selfId)
            {
                UpdateFacingFromDelta(ref view, view.To - view.From);
            }
        }

        _entities[id] = view;
        UpdateStatusMarkerPositions(id);
        if (hp <= 0)
        {
            MaybeDieFromHp(id);
        }
    }

    public void SetClassLook(string classId)
    {
        _classId = string.IsNullOrEmpty(classId) ? "adventurer" : classId;
        SpriteCatalog.SetPlayerSheet(_classId);
        if (!_entities.TryGetValue(_selfId, out var view) || view.Renderer == null || !view.UsesArt)
        {
            return;
        }

        view.Renderer.color = SpriteCatalog.ClassTint(_classId);
        _entities[_selfId] = view;
    }

    public void SetLocalFacing(int facing)
    {
        if (!_entities.TryGetValue(_selfId, out var view))
        {
            return;
        }

        view.Facing = ((facing % 8) + 8) % 8;
        GetCameraBasisXY(out var right, out var up);
        var rad = view.Facing * 45f * Mathf.Deg2Rad;
        var sx = Mathf.Sin(rad);
        var sy = -Mathf.Cos(rad);
        // Display flips screen-X; stored world facing is the true map direction.
        view.FaceX = -sx * right.x + sy * up.x;
        view.FaceZ = -sx * right.y + sy * up.y;
        _entities[_selfId] = view;
    }

    public void SetLocalFacingFromWorld(float dx, float dz)
    {
        if (!_entities.TryGetValue(_selfId, out var view))
        {
            return;
        }

        if (dx * dx + dz * dz < 1e-8f)
        {
            return;
        }

        view.FaceX = dx;
        view.FaceZ = dz;
        RefreshViewFacing(ref view);
        _entities[_selfId] = view;
    }

    public void SetLocalFacingFromStick(float sx, float sy)
    {
        if (sx * sx + sy * sy < 1e-8f)
        {
            return;
        }

        GetCameraBasisXY(out var right, out var up);
        SetLocalFacingFromWorld(sx * right.x + sy * up.x, sx * right.y + sy * up.y);
    }

    /// <summary>
    /// Project stored world facing into the current camera view (8-dir).
    /// Screen X is negated so left/right clips match the viewpoint (pack vs camera).
    /// </summary>
    private void RefreshViewFacing(ref EntityView view)
    {
        GetCameraBasisXY(out var right, out var up);
        var fx = view.FaceX;
        var fz = view.FaceZ;
        if (fx * fx + fz * fz < 1e-8f)
        {
            fx = -up.x;
            fz = -up.y;
            view.FaceX = fx;
            view.FaceZ = fz;
        }

        var sx = -(fx * right.x + fz * right.y);
        var sy = fx * up.x + fz * up.y;
        var sticky = view.Moving || Time.time < view.JustMovedUntil;
        view.Facing = SpriteCatalog.FacingFromScreen(sx, sy, true, sticky ? view.Facing : -1);
    }

    /// <summary>Camera right/forward projected onto the XZ ground plane (normalized).</summary>
    public void GetCameraBasisXY(out Vector2 right, out Vector2 up)
    {
        right = Vector2.right;
        up = Vector2.up;
        if (_cam == null)
        {
            return;
        }

        var r = _cam.transform.right;
        var u = _cam.transform.up;
        right = new Vector2(r.x, r.z);
        up = new Vector2(u.x, u.z);
        if (right.sqrMagnitude > 1e-6f)
        {
            right.Normalize();
        }

        if (up.sqrMagnitude > 1e-6f)
        {
            up.Normalize();
        }
        else
        {
            var f = _cam.transform.forward;
            up = new Vector2(-f.x, -f.z);
            if (up.sqrMagnitude > 1e-6f)
            {
                up.Normalize();
            }
        }
    }

    private void UpdateFacingFromDelta(ref EntityView view, Vector3 delta)
    {
        if (delta.sqrMagnitude < 1e-8f)
        {
            return;
        }

        view.FaceX = delta.x;
        view.FaceZ = delta.z;
    }

    private void SetEntityMeta(string id, string kind, int mp, int maxMp)
    {
        if (!_entities.TryGetValue(id, out var view))
        {
            return;
        }

        if (!string.IsNullOrEmpty(kind))
        {
            view.Kind = kind;
            if (view.Renderer != null)
            {
                view.Renderer.sprite = SpriteCatalog.ForEntity(id, kind);
                view.Renderer.color = Color.white;
                view.Transform.localScale = Vector3.one * SpriteCatalog.EntityScale(id, kind);
                view.UsesArt = SpriteCatalog.HasArtSprite(id, kind);
                view.AnimClip = SpriteCatalog.Clip.Idle;
                view.AnimFrame = 0;
                view.AnimTime = 0f;
            }
        }

        view.Mp = mp;
        if (maxMp > 0)
        {
            view.MaxMp = maxMp;
        }

        _entities[id] = view;
    }

    private static void EnsureStatusLists(ref EntityView view)
    {
        if (view.StatusKinds == null)
        {
            view.StatusKinds = new List<string>();
        }

        if (view.StatusUntil == null)
        {
            view.StatusUntil = new List<float>();
        }

        if (view.ThreatTopId == null)
        {
            view.ThreatTopId = "";
        }

        if (view.Kind == null)
        {
            view.Kind = "";
        }
    }

    private List<string> BuildLivingFoesSortedByDist()
    {
        var ids = new List<string>();
        var dists = new List<float>();
        if (!_entities.TryGetValue(_selfId, out var self) || self.Transform == null)
        {
            return ids;
        }

        var selfPos = self.Transform.position;
        foreach (var pair in _entities)
        {
            if (pair.Key == _selfId || !IsLivingFoe(pair.Key))
            {
                continue;
            }

            var d = WorldCoords.MapDistance(selfPos, pair.Value.Transform.position);
            ids.Add(pair.Key);
            dists.Add(d);
        }

        SortIdsByDist(ids, dists);
        return ids;
    }

    private List<string> BuildLivingNonSelfSortedByDist()
    {
        var ids = new List<string>();
        var dists = new List<float>();
        if (!_entities.TryGetValue(_selfId, out var self) || self.Transform == null)
        {
            return ids;
        }

        var selfPos = self.Transform.position;
        foreach (var pair in _entities)
        {
            if (pair.Key == _selfId || pair.Value.Transform == null || pair.Value.Hp <= 0)
            {
                continue;
            }

            var d = WorldCoords.MapDistance(selfPos, pair.Value.Transform.position);
            ids.Add(pair.Key);
            dists.Add(d);
        }

        SortIdsByDist(ids, dists);
        return ids;
    }

    private static void SortIdsByDist(List<string> ids, List<float> dists)
    {
        for (var i = 1; i < ids.Count; i++)
        {
            var id = ids[i];
            var dist = dists[i];
            var j = i - 1;
            while (j >= 0 && dists[j] > dist)
            {
                ids[j + 1] = ids[j];
                dists[j + 1] = dists[j];
                j--;
            }

            ids[j + 1] = id;
            dists[j + 1] = dist;
        }
    }

    private bool IsLiving(string id)
    {
        return !string.IsNullOrEmpty(id) && _entities.TryGetValue(id, out var v) && v.Hp > 0 && v.Transform != null;
    }

    private void RebuildStatusMarkers(string entityId)
    {
        ClearStatusMarkers(entityId);
        if (!_entities.TryGetValue(entityId, out var view) || view.Transform == null ||
            view.StatusKinds == null || view.StatusKinds.Count == 0)
        {
            return;
        }

        var list = new List<Transform>();
        for (var i = 0; i < view.StatusKinds.Count; i++)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "status_" + view.StatusKinds[i];
            go.transform.SetParent(_root, false);
            StripCollider(go);
            go.transform.localScale = Vector3.one * 0.22f;
            ApplyUnlitColor(go, StatusColor(view.StatusKinds[i]));
            list.Add(go.transform);
        }

        _statusMarkers[entityId] = list;
        UpdateStatusMarkerPositions(entityId);
    }

    private void UpdateStatusMarkerPositions(string entityId)
    {
        if (!_statusMarkers.TryGetValue(entityId, out var list) || list == null)
        {
            return;
        }

        if (!_entities.TryGetValue(entityId, out var view) || view.Transform == null)
        {
            return;
        }

        var basePos = FxAtWorld(view.Transform.position, 0.7f);
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] == null)
            {
                continue;
            }

            list[i].position = basePos + new Vector3((i - (list.Count - 1) * 0.5f) * 0.28f, 0.15f, 0f);
        }
    }

    private void ClearStatusMarkers(string entityId)
    {
        if (!_statusMarkers.TryGetValue(entityId, out var list))
        {
            return;
        }

        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] != null)
            {
                Destroy(list[i].gameObject);
            }
        }

        _statusMarkers.Remove(entityId);
    }

    private void PlaySyncFx(string json)
    {
        var kind = JsonUtil.ExtractString(json, "kind");
        float x = 0f;
        float y = 0f;
        var hasPos = JsonUtil.TryNumber(json, "x", out x) && JsonUtil.TryNumber(json, "y", out y);
        if (!hasPos && _entities.TryGetValue(_selfId, out var self) && self.Transform != null)
        {
            var p = self.Transform.position;
            x = p.x;
            y = p.z;
        }

        var pos = WorldCoords.TileToWorld(x, y, 0.2f);
        if (kind == "homestone" || kind == "teleport")
        {
            PlayTeleportFx();
            SpawnRingFx(pos, 0.4f, 2.1f, new Color(0.45f, 0.9f, 1f, 0.8f),
                new Color(0.3f, 0.7f, 1f, 0f), 0.5f, 40f);
            return;
        }

        if (kind == "levelup")
        {
            PlayLevelUpFx();
            return;
        }

        if (kind == "gacha")
        {
            PlayGachaRevealFx();
            return;
        }

        if (kind == "portal")
        {
            PlayPortalRipple(pos);
            return;
        }

        if (kind == "food")
        {
            SpawnBurstFx(pos + Vector3.up * 0.2f, new Color(1f, 0.55f, 0.15f, 0.85f), 0.7f, 0.32f);
            return;
        }

        if (kind == "telegraph")
        {
            JsonUtil.TryNumber(json, "radius", out var radius);
            JsonUtil.TryNumber(json, "durationMs", out var durationMs);
            SpawnTelegraphDisc(pos, radius > 0.1f ? radius : 1.7f, durationMs > 1f ? durationMs / 1000f : 0.75f);
        }
    }

    public static bool IsBossEntity(string id, string kind, string label)
    {
        if (kind == "player" || kind == "npc")
        {
            return false;
        }

        var s = (id ?? "") + " " + (label ?? "");
        return s.IndexOf("boss", System.StringComparison.OrdinalIgnoreCase) >= 0
            || s.IndexOf("warden", System.StringComparison.OrdinalIgnoreCase) >= 0
            || s.IndexOf("colossus", System.StringComparison.OrdinalIgnoreCase) >= 0
            || s.IndexOf("apex", System.StringComparison.OrdinalIgnoreCase) >= 0
            || s.IndexOf("crypt_lord", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public bool TryGetBossFrame(out string name, out int hp, out int maxHp)
    {
        name = "";
        hp = 0;
        maxHp = 0;
        if (!string.IsNullOrEmpty(_lockTargetId) && _entities.TryGetValue(_lockTargetId, out var locked)
            && locked.Hp > 0 && IsBossEntity(_lockTargetId, locked.Kind, locked.Label))
        {
            name = string.IsNullOrEmpty(locked.Label) ? _lockTargetId : locked.Label;
            hp = locked.Hp;
            maxHp = locked.MaxHp;
            return true;
        }

        foreach (var pair in _entities)
        {
            if (pair.Value.Hp <= 0 || !IsBossEntity(pair.Key, pair.Value.Kind, pair.Value.Label))
            {
                continue;
            }

            name = string.IsNullOrEmpty(pair.Value.Label) ? pair.Key : pair.Value.Label;
            hp = pair.Value.Hp;
            maxHp = pair.Value.MaxHp;
            return true;
        }

        return false;
    }

    private void SpawnTelegraphDisc(Vector3 pos, float radius, float life)
    {
        var d = Mathf.Max(1.2f, radius * 2f);
        SpawnRingFx(pos, d * 0.55f, d, new Color(1f, 0.18f, 0.12f, 0.85f),
            new Color(1f, 0.08f, 0.05f, 0.15f), Mathf.Max(0.25f, life), 25f);
        SpawnFx(MakeGlowSprite(), WorldCoords.Lift(pos, 0.08f),
            Vector3.one * (d * 0.2f), Vector3.one * (d * 0.45f),
            new Color(1f, 0.2f, 0.1f, 0.35f), new Color(1f, 0.1f, 0.05f, 0f),
            Mathf.Max(0.25f, life), 0f, 0f, "", Vector3.zero, 40);
    }

    private void SpawnPopSphere(Vector3 pos, Color color, float life)
    {
        SpawnBurstFx(pos, color, 0.7f, Mathf.Max(0.08f, life));
    }

    private void SpawnTempPrimitive(PrimitiveType type, Vector3 pos, Vector3 scale, Color color, float life)
    {
        SpawnBurstFx(pos, color, Mathf.Max(0.35f, scale.x), Mathf.Max(0.08f, life));
    }

    private static void StripCollider(GameObject go)
    {
        var col = go.GetComponent<Collider>();
        if (col != null)
        {
            Object.Destroy(col);
        }
    }

    private static void ApplyUnlitColor(GameObject go, Color color)
    {
        var rend = go.GetComponent<Renderer>();
        if (rend == null)
        {
            return;
        }

        var shader = Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Unlit/Color")
            ?? Shader.Find("Sprites/Default");
        if (shader != null)
        {
            rend.material = new Material(shader);
        }

        rend.material.color = color;
    }

    private static Color StatusColor(string kind)
    {
        if (kind == "stun")
        {
            return Color.yellow;
        }

        if (kind == "dot" || kind == "burn" || (kind != null && kind.Contains("burn")))
        {
            return new Color(1f, 0.55f, 0.1f);
        }

        if (kind == "blind")
        {
            return Color.gray;
        }

        if (kind != null && kind.Contains("shield"))
        {
            return Color.cyan;
        }

        if (kind == "haste" || kind == "speed_mult")
        {
            return new Color(0.6f, 1f, 0.2f);
        }

        if (kind == "attr_up")
        {
            return new Color(1f, 0.55f, 0.15f);
        }

        if (kind == "dmg_taken_mult")
        {
            return new Color(0.45f, 0.85f, 1f);
        }

        return Color.magenta;
    }

    private int GetHp(string id, int fallback) => _entities.TryGetValue(id, out var v) ? v.Hp : fallback;
    private int GetMaxHp(string id, int fallback) => _entities.TryGetValue(id, out var v) ? v.MaxHp : fallback;
    private float GetHitRadius(string id, float fallback) => _entities.TryGetValue(id, out var v) ? v.HitRadius : fallback;
    private string GetLabel(string id, string fallback) => _entities.TryGetValue(id, out var v) && !string.IsNullOrEmpty(v.Label) ? v.Label : fallback;

    private static GameObject CreateHitRing()
    {
        var go = new GameObject("HitRing");
        var sr = go.AddComponent<SpriteRenderer>();
        // Thin transparent outline only — opaque white quads read as "white squares" under URP.
        sr.sprite = MakeRingSprite();
        sr.color = new Color(1f, 1f, 1f, 0.35f);
        sr.sortingOrder = 5;
        ApplyUnlit(sr);
        go.SetActive(false); // art sprites don't need footprint rings; enable only for procedural
        return go;
    }

    private static Sprite MakeRingSprite()
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color[size * size];
        var cx = (size - 1) * 0.5f;
        var cy = (size - 1) * 0.5f;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var nx = (x - cx) / cx;
                var ny = (y - cy) / cy;
                var d = Mathf.Sqrt(nx * nx + ny * ny);
                pixels[y * size + x] = (d > 0.72f && d < 0.92f)
                    ? new Color(1f, 1f, 1f, 1f)
                    : Color.clear;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        tex.filterMode = FilterMode.Point;
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    private void EnsureLockRing()
    {
        var go = new GameObject("LockOutline");
        go.transform.SetParent(_root, false);
        var sr = go.AddComponent<SpriteRenderer>();
        _lockFrames = VfxCatalog.LockIcon();
        sr.sprite = _lockFrames != null && _lockFrames.Length > 0 ? _lockFrames[0] : MakeRedOutlineSprite();
        sr.color = Color.white;
        sr.sortingOrder = 25;
        ApplyUnlit(sr);
        go.transform.localScale = Vector3.one * 1.4f;
        _lockRing = go.transform;
        _lockRingSr = sr;
    }

    private void UpdateLockRing()
    {
        if (_lockRing == null)
        {
            return;
        }

        if (!_entities.TryGetValue(_lockTargetId, out var view) || view.Transform == null || view.Hp <= 0)
        {
            _lockRing.gameObject.SetActive(false);
            return;
        }

        _lockRing.gameObject.SetActive(true);
        // Outline tightly around the rendered sprite (+2px), not the tile footprint.
        var worldW = 1.1f;
        var worldH = 1.1f;
        if (view.Renderer != null && view.Renderer.sprite != null)
        {
            var b = view.Renderer.sprite.bounds;
            var sx = Mathf.Abs(view.Transform.lossyScale.x);
            var sy = Mathf.Abs(view.Transform.lossyScale.y);
            worldW = Mathf.Max(0.35f, b.size.x * sx);
            worldH = Mathf.Max(0.35f, b.size.y * sy);
            var ppu = view.Renderer.sprite.pixelsPerUnit;
            if (ppu < 1f)
            {
                ppu = 64f;
            }

            var pad = 2f / ppu * Mathf.Max(sx, sy);
            worldW += pad * 2f;
            worldH += pad * 2f;
        }
        else
        {
            var scale = Mathf.Max(0.5f, view.Transform.localScale.x);
            worldW = worldH = scale * 1.05f;
        }

        var ringH = worldH * 0.5f;
        _lockRing.position = view.Transform.position + Vector3.up * ringH;
        YBillboard(_lockRing);

        // Outline sprite is authored as ~1×1 world unit.
        _lockRing.localScale = new Vector3(worldW, worldH, 1f);

        if (_lockRingSr == null)
        {
            _lockRingSr = _lockRing.GetComponent<SpriteRenderer>();
        }

        if (_lockRingSr != null)
        {
            if (_lockFrames != null && _lockFrames.Length > 0)
            {
                _lockAnimT += Time.deltaTime * VfxCatalog.DefaultSheetFps;
                var idx = Mathf.FloorToInt(_lockAnimT) % _lockFrames.Length;
                _lockRingSr.sprite = _lockFrames[idx];
            }

            var a = view.ThreatSelfPct >= 35f ? 1f : 0.92f;
            var friendly = IsPlayerEntity(_lockTargetId);
            _lockRingSr.color = friendly
                ? new Color(0.55f, 0.75f, 1f, a)
                : new Color(1f, 0.85f, 0.95f, a);
        }
    }

    private static Sprite MakeRedOutlineSprite()
    {
        const int size = 64;
        const int thick = 3;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color[size * size];
        var clear = new Color(0f, 0f, 0f, 0f);
        var red = new Color(1f, 1f, 1f, 1f);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var edge = x < thick || x >= size - thick || y < thick || y >= size - thick;
                pixels[y * size + x] = edge ? red : clear;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    private void SetupCamera()
    {
        if (_cam == null)
        {
            return;
        }

        _cam.orthographic = true;
        ApplyIsoOrthoSize();
        _cam.clearFlags = CameraClearFlags.SolidColor;
        _cam.backgroundColor = new Color(0.08f, 0.09f, 0.12f);
        _cam.nearClipPlane = 0.1f;
        _cam.farClipPlane = 120f;
        _cam.transparencySortMode = TransparencySortMode.Perspective;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.42f, 0.45f, 0.52f);
        EnsureSun();
    }

    private void ApplyIsoOrthoSize()
    {
        if (_cam == null)
        {
            return;
        }

        _cam.orthographicSize = WorldCoords.OrthoSizeForTilePixels(Screen.height);
    }

    private void EnsureSun()
    {
        if (_isoSun != null)
        {
            return;
        }

        var go = GameObject.Find("IsoSun");
        if (go == null)
        {
            go = new GameObject("IsoSun");
        }

        _isoSun = go.GetComponent<Light>();
        if (_isoSun == null)
        {
            _isoSun = go.AddComponent<Light>();
        }

        _isoSun.type = LightType.Directional;
        _isoSun.intensity = 1.15f;
        _isoSun.color = new Color(1f, 0.97f, 0.9f);
        _isoSun.shadows = LightShadows.Soft;
        go.transform.rotation = Quaternion.Euler(50f, 45f, 0f);
    }

    private void CenterCamera(float x, float y)
    {
        UpdateOrbitCamera(WorldCoords.TileToWorld(x, y));
    }

    private void UpdateOrbitCamera(Vector3 playerPos)
    {
        if (_cam == null)
        {
            return;
        }

        ApplyIsoOrthoSize();
        _cam.transform.rotation = Quaternion.Euler(CamPitchFromHorizontal, _camYaw, 0f);
        _cam.transform.position = playerPos - _cam.transform.forward * CamDistance;
    }

    public void ClearSessionEntities()
    {
        ClearForeignEntities();
        if (_entities.TryGetValue(_selfId, out var self) && self.Transform != null)
        {
            self.Transform.position = WorldCoords.TileToWorld(3f, 6f);
            self.Moving = false;
            _entities[_selfId] = self;
        }

        CenterCamera(3f, 6f);
    }

    private static void DrawBar(Vector2 center, float ratio, Color fill)
    {
        const float w = 70f;
        const float h = 8f;
        var rect = new Rect(center.x - w * 0.5f, center.y - 8f, w, h);
        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = fill;
        GUI.DrawTexture(new Rect(rect.x, rect.y, w * Mathf.Clamp01(ratio), h), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    private static GameObject CreateMarker(Color color, string label, string id = null, string kind = null)
    {
        var go = new GameObject(label);
        var sr = go.AddComponent<SpriteRenderer>();
        var useKind = kind ?? "monster";
        var useId = id ?? label;
        sr.sprite = SpriteCatalog.ForEntity(useId, useKind);
        sr.color = SpriteCatalog.HasArtSprite(useId, useKind)
            ? (SpriteCatalog.IsPlayerKind(useId, useKind) ? Color.white : SpriteCatalog.MonsterTint(useId))
            : color;
        sr.sortingOrder = 20;
        ApplyUnlit(sr);
        go.transform.localScale = Vector3.one * SpriteCatalog.EntityScale(useId, useKind);
        return go;
    }

    private static Material _sharedSpriteMat;
    private static MaterialPropertyBlock _spriteTexBlock;

    private static MaterialPropertyBlock SpriteTexBlock
    {
        get
        {
            if (_spriteTexBlock == null)
            {
                _spriteTexBlock = new MaterialPropertyBlock();
            }

            return _spriteTexBlock;
        }
    }

    private static void BindSpriteTexture(SpriteRenderer sr)
    {
        if (sr == null || sr.sprite == null)
        {
            return;
        }

        var tex = sr.sprite.texture;
        if (tex == null)
        {
            return;
        }

        var block = SpriteTexBlock;
        sr.GetPropertyBlock(block);
        block.SetTexture("_MainTex", tex);
        block.SetTexture("_BaseMap", tex);
        block.SetColor("_BaseColor", Color.white);
        block.SetColor("_Color", Color.white);
        sr.SetPropertyBlock(block);
    }

    private static void MakeMaterialTransparent(Material mat)
    {
        if (mat == null)
        {
            return;
        }

        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_ZTest", (float)CompareFunction.LessEqual);
        mat.SetFloat("_Cull", 0f);
        if (mat.HasProperty("_BaseColor"))
        {
            mat.SetColor("_BaseColor", Color.white);
        }

        if (mat.HasProperty("_Color"))
        {
            mat.SetColor("_Color", Color.white);
        }

        if (mat.HasProperty("_UnlitColor"))
        {
            mat.SetColor("_UnlitColor", Color.white);
        }
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.renderQueue = 3000;
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        if (mat.HasProperty("_Cutoff"))
        {
            mat.SetFloat("_Cutoff", 0.1f);
        }
    }

    private static void ApplyUnlit(SpriteRenderer sr)
    {
        // Billboarded units need a 3D-capable unlit shader. Force alpha blending so
        // PNG transparency is not drawn as a black halo around characters/enemies.
        if (sr == null)
        {
            return;
        }

        if (_sharedSpriteMat == null)
        {
            string[] names =
            {
                "Universal Render Pipeline/Unlit",
                "Unlit/Transparent",
                "Universal Render Pipeline/2D/Sprite-Unlit-Default",
                "Sprites/Default",
                "Unlit/Texture",
            };
            for (var i = 0; i < names.Length; i++)
            {
                var shader = Shader.Find(names[i]);
                if (shader == null)
                {
                    continue;
                }

                _sharedSpriteMat = new Material(shader);
                MakeMaterialTransparent(_sharedSpriteMat);
                GameLog.Info(GameLog.Channel.Gfx, "sprite shader=" + names[i] + "  alpha=blend");
                break;
            }

            if (_sharedSpriteMat == null)
            {
                GameLog.WarnOnce(GameLog.Channel.Gfx, "sprite-shader",
                    "reason=no_sprite_shader  fallback=renderer_default");
            }
        }

        if (_sharedSpriteMat != null)
        {
            sr.sharedMaterial = _sharedSpriteMat;
        }

        BindSpriteTexture(sr);
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

    private void SpawnMapProps(List<JsonUtil.MapProp> props)
    {
        if (props == null || props.Count == 0 || _root == null)
        {
            return;
        }

        for (var i = 0; i < props.Count; i++)
        {
            var p = props[i];
            var go = new GameObject("prop");
            go.transform.SetParent(_root, false);
            go.transform.position = WorldCoords.TileToWorld(p.X, p.Y, 0.4f);
            var scale = SpriteCatalog.PropScale(p.Kind);
            go.transform.localScale = new Vector3(scale, scale, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteCatalog.ForProp(p.Kind);
            sr.color = Color.white;
            sr.sortingOrder = 1;
            ApplyUnlit(sr);
        }
    }

    private void SpawnTileHints(HashSet<Vector2Int> blocked = null, HashSet<Vector2Int> hazards = null)
    {
        blocked ??= new HashSet<Vector2Int>
        {
            new Vector2Int(8, 4), new Vector2Int(8, 5), new Vector2Int(8, 6),
        };
        hazards ??= new HashSet<Vector2Int>();

        for (var x = 0; x < _mapW; x++)
        {
            for (var y = 0; y < _mapH; y++)
            {
                var cell = new Vector2Int(x, y);
                var isWall = blocked.Contains(cell);
                var isHazard = !isWall && hazards.Contains(cell);
                var even = (x + y) % 2 == 0;
                if (isWall)
                {
                    SpawnWallCube(x, y);
                    continue;
                }

                SpawnFloorTile(x, y, even, isHazard);
            }
        }

        SpawnMapBorderWalls();
    }

    /// <summary>Visible cubes for the out-of-bounds ring (server already rejects those coords).</summary>
    private void SpawnMapBorderWalls()
    {
        for (var x = -1; x <= _mapW; x++)
        {
            SpawnWallCube(x, -1);
            SpawnWallCube(x, _mapH);
        }

        for (var y = 0; y < _mapH; y++)
        {
            SpawnWallCube(-1, y);
            SpawnWallCube(_mapW, y);
        }
    }

    private void SpawnFloorTile(int x, int y, bool even, bool hazard)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = hazard ? "hazard" : "tile";
        go.transform.SetParent(_root, false);
        go.transform.position = WorldCoords.TileToWorld(x, y, -0.02f);
        go.transform.localScale = new Vector3(1f, 0.04f, 1f);
        StripCollider(go);
        var tint = hazard
            ? new Color(1f, 0.35f, 0.18f, 1f)
            : even ? Color.white : new Color(0.88f, 0.88f, 0.88f, 1f);
        ApplyTileMaterial(go, SpriteCatalog.ForFloor(_mapId, even), tint, false);
    }

    private void SpawnWallCube(int x, int y)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "wall";
        go.transform.SetParent(_root, false);
        const float h = 2f;
        go.transform.position = WorldCoords.TileToWorld(x, y, h * 0.5f);
        go.transform.localScale = new Vector3(1f, h, 1f);
        StripCollider(go);
        // Solid cube — biome wall textures read as floor from dimetric pitch.
        ApplyTileMaterial(go, null, new Color(0.28f, 0.30f, 0.36f, 1f), true);
    }

    private void ApplyTileMaterial(GameObject go, Sprite sprite, Color tint, bool castShadows)
    {
        var rend = go.GetComponent<MeshRenderer>();
        if (rend == null)
        {
            return;
        }

        var key = (sprite != null ? sprite.name : "flat") + tint.ToString() + (castShadows ? "_w" : "_f");
        if (!_tileMats.TryGetValue(key, out var mat) || mat == null)
        {
            var shader = sprite == null
                ? Shader.Find("Universal Render Pipeline/Unlit")
                    ?? Shader.Find("Unlit/Color")
                    ?? Shader.Find("Universal Render Pipeline/Lit")
                    ?? Shader.Find("Standard")
                : Shader.Find("Universal Render Pipeline/Lit")
                    ?? Shader.Find("Universal Render Pipeline/Unlit")
                    ?? Shader.Find("Unlit/Texture")
                    ?? Shader.Find("Standard");
            mat = shader != null ? new Material(shader) : new Material(rend.sharedMaterial);
            if (sprite != null && sprite.texture != null)
            {
                if (mat.HasProperty("_BaseMap"))
                {
                    mat.SetTexture("_BaseMap", sprite.texture);
                }

                mat.mainTexture = sprite.texture;
            }

            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", tint);
            }

            if (mat.HasProperty("_Color"))
            {
                mat.SetColor("_Color", tint);
            }

            _tileMats[key] = mat;
        }

        rend.sharedMaterial = mat;
        rend.shadowCastingMode = castShadows
            ? UnityEngine.Rendering.ShadowCastingMode.On
            : UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = true;
    }
}
