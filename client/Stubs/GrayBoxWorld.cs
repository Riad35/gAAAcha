using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Map + entities with soft tile lerp and edge-only camera.
/// </summary>
public sealed class GrayBoxWorld : MonoBehaviour
{
    private const float MoveDuration = 0.15f;
    private const float LongRange = 999f;
    private const float CamPitchFromVertical = 32f;
    private const float CamDistance = 14f;
    private const float CamYawSpeed = 90f;
    private const float JustMovedSec = 0.4f;

    private readonly Dictionary<string, EntityView> _entities = new Dictionary<string, EntityView>();
    private Transform _root;
    private Transform _lockRing;
    private SpriteRenderer _lockRingSr;
    private Camera _cam;
    private float _camYaw;
    private float _rmbLastX;
    private bool _rmbDragging;
    private string _selfId = "local_you";
    private string _lockTargetId = "monster_slime_1";
    private readonly List<FloatText> _floats = new List<FloatText>();
    private readonly Dictionary<string, Transform> _projectiles = new Dictionary<string, Transform>();
    private readonly Dictionary<string, string> _projectileSkills = new Dictionary<string, string>();
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
        public bool Moving;
        public SpriteCatalog.Clip AnimClip;
        public int AnimFrame;
        public float AnimTime;
        public bool UsesArt;
        public int Facing;
        public float JustMovedUntil;
        public float AnimLockUntil;
        public bool AnimOneShot;
        public bool Dying;
        public float DeathRemoveAt;
        public int AnimFacingRow;
    }

    private struct FloatText
    {
        public Vector3 World;
        public string Text;
        public float Until;
        public Color Color;
    }

    private struct TempFx
    {
        public GameObject Go;
        public float Until;
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
            Debug.LogWarning("[GrayBoxWorld] Player sheets failed to load — check StreamingAssets/Sprites + Resources/Sprites.");
        }
        else
        {
            Debug.Log("[GrayBoxWorld] Sprite sheets OK (StreamingAssets/Resources).");
        }
        SetLockTarget("monster_slime_1");
        CenterCamera(3f, 6f);
    }

    public void RebuildMap(int mapW, int mapH, HashSet<Vector2Int> blocked, string mapId = null,
        HashSet<Vector2Int> hazards = null)
    {
        _mapW = mapW;
        _mapH = mapH;
        if (!string.IsNullOrEmpty(mapId))
        {
            _mapId = mapId;
        }

        // Destroy old tiles under root named tile/wall/hazard
        if (_root != null)
        {
            for (var i = _root.childCount - 1; i >= 0; i--)
            {
                var c = _root.GetChild(i);
                if (c.name == "tile" || c.name == "wall" || c.name == "hazard")
                {
                    Destroy(c.gameObject);
                }
            }
        }

        SpawnTileHints(blocked, hazards);
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
            Despawn(JsonUtil.ExtractString(json, "entityId"));
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

        view.Hp = hp;
        _entities[entityId] = view;
        if (hard && hp <= 0)
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

            var pos = pair.Value.Transform.position;
            var d = Vector2.Distance(new Vector2(worldX, worldY), new Vector2(pos.x, pos.y));
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

            var pos = pair.Value.Transform.position;
            var d = Vector2.Distance(new Vector2(worldX, worldY), new Vector2(pos.x, pos.y));
            var reach = Mathf.Max(0.7f, pair.Value.HitRadius) + tolerance;
            if (d <= reach && d < bestDist)
            {
                bestDist = d;
                best = pair.Key;
            }
        }

        return best;
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

            var pos = pair.Value.Transform.position;
            var d = Vector2.Distance(new Vector2(worldX, worldY), new Vector2(pos.x, pos.y));
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
            (id.StartsWith("monster") || id.Contains("slime") || id.Contains("dummy") ||
             id.Contains("ragdoll") || id.Contains("cannon") || id.StartsWith("player_")))
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
            origin = new Vector2(self.Transform.position.x, self.Transform.position.y);
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

            var otherR = pair.Value.HitRadius > 0f ? pair.Value.HitRadius : 0.4f;
            var center = Vector2.Distance(
                origin,
                new Vector2(pair.Value.Transform.position.x, pair.Value.Transform.position.y));
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
        Vector2 origin = Vector2.zero;
        var hasOrigin = false;
        if (_entities.TryGetValue(_selfId, out var self) && self.Transform != null)
        {
            origin = new Vector2(self.Transform.position.x, self.Transform.position.y);
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

            var pos = new Vector2(pair.Value.Transform.position.x, pair.Value.Transform.position.y);
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

    public string CycleLockTarget()
    {
        return LockClosestEnemy(LongRange);
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
        return Vector2.Distance(new Vector2(a.x, a.y), new Vector2(b.x, b.y));
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
        y = view.Transform.position.y;
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

            var d = Vector2.Distance(
                new Vector2(selfPos.x, selfPos.y),
                new Vector2(pair.Value.Transform.position.x, pair.Value.Transform.position.y));
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
    /// Place FX exactly on the enemy sprite (XY), slightly toward camera so it draws in front of the body.
    /// </summary>
    private Vector3 FxAtEntity(string id, float headLift = 0f)
    {
        if (!string.IsNullOrEmpty(id) && _entities.TryGetValue(id, out var view) && view.Transform != null)
        {
            var p = view.Transform.position;
            p.z = -0.55f;
            if (_cam != null && Mathf.Abs(headLift) > 0.001f)
            {
                p += _cam.transform.up * headLift;
            }

            return p;
        }

        var pos = GetEntityWorldPos(id) ?? Vector3.zero;
        return FxAtWorld(pos, headLift);
    }

    private Vector3 FxAtWorld(Vector3 entityPos, float headLift = 0f)
    {
        var p = new Vector3(entityPos.x, entityPos.y, -0.55f);
        if (_cam != null && Mathf.Abs(headLift) > 0.001f)
        {
            p += _cam.transform.up * headLift;
        }

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

        var flat = new Vector2(point.x - caster.x, point.y - caster.y);
        if (flat.magnitude > _aimCastRange)
        {
            flat = flat.normalized * _aimCastRange;
        }

        _aimPoint = new Vector3(caster.x + flat.x, caster.y + flat.y, caster.z);
        EnsureAimPartCount(2);

        var reticle = _aimParts[0];
        var d = _aimAoeRadius * 2f;
        reticle.position = new Vector3(_aimPoint.x, _aimPoint.y, -0.08f);
        reticle.localScale = new Vector3(d, 0.025f, d);
        reticle.rotation = Quaternion.FromToRotation(Vector3.up, Vector3.forward);
        SetAimPartColor(reticle, new Color(0.4f, 1f, 0.55f, 0.4f));

        var ring = _aimParts[1];
        var rd = _aimCastRange * 2f;
        ring.position = new Vector3(caster.x, caster.y, -0.06f);
        ring.localScale = new Vector3(rd, 0.02f, rd);
        ring.rotation = Quaternion.FromToRotation(Vector3.up, Vector3.forward);
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

            var gui = new Vector2(screen.x, Screen.height - screen.y);
            var ratio = view.MaxHp <= 0 ? 0f : (float)view.Hp / view.MaxHp;
            DrawBar(gui, ratio, pair.Key == _selfId ? Color.cyan : Color.green);
            GUI.Label(new Rect(gui.x - 40, gui.y - 28, 80, 20), view.Hp + "/" + view.MaxHp);
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

            var gui = new Vector2(screen.x, Screen.height - screen.y);
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            style.normal.textColor = ft.Color;
            GUI.Label(new Rect(gui.x - 40, gui.y - 20, 80, 30), ft.Text, style);
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
                FinishDespawn(key);
                continue;
            }

            var wasMoving = view.Moving;
            if (view.Moving && view.Transform != null && !view.Dying)
            {
                view.MoveT += Time.deltaTime / MoveDuration;
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

            TickEntityAnim(ref view);
            BillboardEntity(ref view);
            _entities[key] = view;
        }

        if (_entities.TryGetValue(_selfId, out var self) && self.Transform != null)
        {
            UpdateOrbitCamera(self.Transform.position);
        }

        for (var i = _tempFx.Count - 1; i >= 0; i--)
        {
            if (Time.time <= _tempFx[i].Until)
            {
                continue;
            }

            if (_tempFx[i].Go != null)
            {
                Destroy(_tempFx[i].Go);
            }

            _tempFx.RemoveAt(i);
        }
    }

    private void HandleCameraInput()
    {
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.qKey.isPressed || kb.zKey.isPressed)
            {
                _camYaw -= CamYawSpeed * Time.deltaTime;
            }

            if (kb.eKey.isPressed || kb.cKey.isPressed)
            {
                _camYaw += CamYawSpeed * Time.deltaTime;
            }
        }

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

    private static void TickEntityAnim(ref EntityView view)
    {
        if (!view.UsesArt || view.Renderer == null)
        {
            return;
        }

        var showAngled = view.Moving || Time.time < view.JustMovedUntil
            || view.AnimClip == SpriteCatalog.Clip.Attack
            || view.AnimClip == SpriteCatalog.Clip.WalkAttack
            || view.AnimClip == SpriteCatalog.Clip.RunAttack
            || view.AnimClip == SpriteCatalog.Clip.Hurt
            || view.AnimClip == SpriteCatalog.Clip.Death
            || view.Dying;
        var facingRow = showAngled ? Mathf.Clamp(view.Facing, 0, 3) : 0;

        SpriteCatalog.Clip want;
        if (view.Dying || view.AnimClip == SpriteCatalog.Clip.Death)
        {
            want = SpriteCatalog.Clip.Death;
        }
        else if (view.AnimOneShot && Time.time < view.AnimLockUntil)
        {
            want = view.AnimClip;
        }
        else
        {
            view.AnimOneShot = false;
            if (view.Moving)
            {
                // Fast lerp / continuous move → run clip when available.
                want = view.MoveT < 0.85f ? SpriteCatalog.Clip.Run : SpriteCatalog.Clip.Walk;
            }
            else
            {
                want = SpriteCatalog.Clip.Idle;
            }
        }

        if (want != view.AnimClip || facingRow != view.AnimFacingRow)
        {
            view.AnimClip = want;
            view.AnimFacingRow = facingRow;
            view.AnimFrame = 0;
            view.AnimTime = 0f;
        }

        var idHint = view.Kind == "player" ? "player_local" : "monster_slime_1";
        var frames = ResolveAnimFrames(idHint, view.Kind, view.AnimClip, facingRow);
        if (frames == null || frames.Length == 0)
        {
            // Last resort: keep a visible silhouette so characters never go blank.
            if (view.Renderer.sprite == null)
            {
                view.Renderer.sprite = SpriteCatalog.ForEntity(idHint, view.Kind);
                view.Renderer.color = Color.white;
            }

            return;
        }

        var fps = view.AnimClip switch
        {
            SpriteCatalog.Clip.Walk => 8f,
            SpriteCatalog.Clip.Run => 10f,
            SpriteCatalog.Clip.Attack => 12f,
            SpriteCatalog.Clip.WalkAttack => 12f,
            SpriteCatalog.Clip.RunAttack => 14f,
            SpriteCatalog.Clip.Hurt => 10f,
            SpriteCatalog.Clip.Death => 8f,
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
            else if (view.AnimOneShot)
            {
                if (view.AnimFrame + 1 >= frames.Length)
                {
                    view.AnimOneShot = false;
                    view.AnimLockUntil = 0f;
                    view.AnimClip = SpriteCatalog.Clip.Idle;
                    view.AnimFrame = 0;
                    frames = ResolveAnimFrames(idHint, view.Kind, SpriteCatalog.Clip.Idle, facingRow);
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

        if (frames == null || frames.Length == 0)
        {
            if (view.Renderer.sprite == null)
            {
                view.Renderer.sprite = SpriteCatalog.ForEntity(idHint, view.Kind);
            }

            return;
        }

        if (view.AnimFrame >= frames.Length)
        {
            view.AnimFrame = 0;
        }

        view.Renderer.sprite = frames[view.AnimFrame];
        view.Renderer.color = Color.white;
    }

    private static Sprite[] ResolveAnimFrames(string idHint, string kind, SpriteCatalog.Clip clip, int facingRow)
    {
        var frames = SpriteCatalog.GetClip(idHint, kind, clip, facingRow);
        if (frames != null && frames.Length > 0)
        {
            return frames;
        }

        if (clip == SpriteCatalog.Clip.Run || clip == SpriteCatalog.Clip.WalkAttack || clip == SpriteCatalog.Clip.RunAttack)
        {
            var alt = clip == SpriteCatalog.Clip.Run ? SpriteCatalog.Clip.Walk : SpriteCatalog.Clip.Attack;
            frames = SpriteCatalog.GetClip(idHint, kind, alt, facingRow);
            if (frames != null && frames.Length > 0)
            {
                return frames;
            }
        }

        if (clip == SpriteCatalog.Clip.Idle || clip == SpriteCatalog.Clip.Hurt || clip == SpriteCatalog.Clip.Death)
        {
            frames = SpriteCatalog.GetClip(idHint, kind, SpriteCatalog.Clip.Walk, facingRow);
            if (frames != null && frames.Length > 0)
            {
                return frames;
            }
        }

        return SpriteCatalog.GetClip(idHint, kind, SpriteCatalog.Clip.Walk, 0);
    }

    public void PlayAttackAnim(string entityId)
    {
        if (!_entities.TryGetValue(entityId, out var view))
        {
            return;
        }

        var clip = view.Moving
            ? (view.MoveT < 0.85f ? SpriteCatalog.Clip.RunAttack : SpriteCatalog.Clip.WalkAttack)
            : SpriteCatalog.Clip.Attack;
        PlayOneShot(entityId, clip, clip == SpriteCatalog.Clip.RunAttack ? 0.45f : 0.55f);
    }

    public void PlayHurtAnim(string entityId)
    {
        PlayOneShot(entityId, SpriteCatalog.Clip.Hurt, 0.35f);
    }

    private void PlayOneShot(string entityId, SpriteCatalog.Clip clip, float lockSec)
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
        _entities[entityId] = view;
    }

    private void BillboardEntity(ref EntityView view)
    {
        if (!view.UsesArt || view.Transform == null || _cam == null)
        {
            return;
        }

        view.Transform.rotation = _cam.transform.rotation;
    }

    private void LateUpdate()
    {
        UpdateLockRing();
        RefreshAimFromSelf();
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

        var d3 = new Vector3(dir.x, dir.y, 0f);
        t.position = caster + d3 * (range * 0.5f) + Vector3.forward * -0.15f;
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
            Upsert(_selfId, youX, youY, new Color(0.15f, 0.85f, 1f),
                string.IsNullOrEmpty(name) ? "You" : name,
                hp > 0 ? hp : 100, maxHp > 0 ? maxHp : 100, hr > 0 ? hr : 0.4f, true);
            SetEntityMeta(_selfId, string.IsNullOrEmpty(kind) ? "player" : kind, mp, maxMp > 0 ? maxMp : mp);
            CenterCamera(youX, youY);
        }

        ApplyMapFromState(json);
        ClearForeignEntities();
        ApplyEntitiesByIdPrefix(json, "monster_");
        ApplyEntitiesByIdPrefix(json, "npc_");
        // Fallback: some payloads space after colon ("id": "monster_…")
        ApplyEntitiesByIdPrefix(json, "monster_", allowSpacedColon: true);
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
        if (_entities.TryGetValue(id, out var view) && view.Transform != null)
        {
            view.Transform.localScale = Vector3.one * 1.6f;
            if (view.Renderer != null)
            {
                view.Renderer.color = new Color(1f, 0.55f, 0.12f, 1f);
            }

            _entities[id] = view;
        }
    }

    private void ApplyMapFromState(string json)
    {
        var mapSlice = JsonUtil.SliceAround(json, "\"map\"", 0, 2500);
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
        var mapId = JsonUtil.ExtractString(mapSlice, "id");
        RebuildMap(w, h, blocked, mapId, hazards);
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

            JsonUtil.TryInt(slice, "hp", out var hp);
            JsonUtil.TryInt(slice, "maxHp", out var maxHp);
            JsonUtil.TryInt(slice, "mp", out var mp);
            JsonUtil.TryInt(slice, "maxMp", out var maxMp);
            JsonUtil.TryNumber(slice, "hitRadius", out var hr);
            var name = JsonUtil.ExtractString(slice, "name");
            var kind = JsonUtil.ExtractString(slice, "kind");
            var inferredKind = !string.IsNullOrEmpty(kind)
                ? kind
                : (prefix.StartsWith("npc") ? "npc" : "monster");
            var label = !string.IsNullOrEmpty(name)
                ? name
                : id.Replace("monster_", "").Replace("npc_", "");
            var color = ColorForEntity(id, inferredKind);
            var fallbackHp = inferredKind == "npc" ? 1 : 40;
            Upsert(id, mx, my, color, label,
                hp > 0 ? hp : fallbackHp,
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
        var label = GetLabel(id, isSelf ? "You" : id);
        MoveTo(id, x, y,
            isSelf ? new Color(0.15f, 0.85f, 1f) : new Color(0.2f, 1f, 0.25f),
            label,
            GetHp(id, isSelf ? 100 : 40),
            GetMaxHp(id, isSelf ? 100 : 40),
            GetHitRadius(id, 0.4f));
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
        JsonUtil.TryInt(json, "hpAfter", out var hpAfter);
        var skillId = JsonUtil.ExtractString(json, "skillId");
        FaceEntityToward(casterId, targetId);
        if (skillId == "auto_attack" || skillId == "slash" || skillId == "shot"
            || skillId == "stun_bolt" || skillId == "shove" || skillId == "pull"
            || skillId == "cannon_flame" || (skillId != null && skillId.EndsWith("_hit")))
        {
            PlayAttackAnim(MapCasterId(casterId));
        }

        ApplyHitFx(targetId, damage, hpAfter, skillId);
        // Placeholder primitive skill VFX removed — keep sprite attack anims + float text only.
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
        var radius = 2.5f;
        if (JsonUtil.TryNumber(json, "aoeRadius", out var r) && r > 0)
        {
            radius = r;
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
            JsonUtil.TryInt(slice, "hpAfter", out var hpAfter);
            ApplyHitFx(targetId, damage, hpAfter, skillId);

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
        SpawnGroundDisc(pos.Value, d, new Color(1f, 0.75f, 0.2f, 0.45f), life);
    }

    private void ApplyHitFx(string targetId, int damage, int hpAfter, string skillId)
    {
        if (string.IsNullOrEmpty(targetId) || !_entities.TryGetValue(targetId, out var view))
        {
            return;
        }

        view.Hp = hpAfter;
        if (view.Renderer != null)
        {
            view.Renderer.color = Color.white;
        }

        _entities[targetId] = view;
        var healish = skillId == "mend" || (skillId != null && skillId.Contains("heal"));
        if (!healish && damage > 0 && hpAfter > 0)
        {
            PlayHurtAnim(targetId);
        }
        else if (!healish && hpAfter <= 0)
        {
            BeginDeath(targetId);
        }

        var text = healish ? "+" + Mathf.Max(1, damage > 0 ? damage : 20) : "-" + damage;
        var color = healish ? new Color(0.4f, 1f, 0.5f) : new Color(1f, 0.35f, 0.25f);
        if ((damage > 0 || healish) && view.Transform != null)
        {
            _floats.Add(new FloatText
            {
                World = FxAtEntity(targetId, 0.35f),
                Text = text,
                Until = Time.time + 0.9f,
                Color = color,
            });
        }
    }

    private void SpawnGroundDisc(Vector3 centerXy, float diameter, Color color, float life)
    {
        // Placeholder ground FX removed (was cylinder discs).
    }

    public void Despawn(string id)
    {
        if (string.IsNullOrEmpty(id) || !_entities.TryGetValue(id, out var view))
        {
            return;
        }

        if (view.Dying)
        {
            return;
        }

        if (view.UsesArt)
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

    private void ApplyProjectileSpawn(string json)
    {
        var slice = JsonUtil.SliceAround(json, "\"projectile\"", 0, 320);
        var src = slice.Length > 0 ? slice : json;
        var id = JsonUtil.ExtractString(src, "id");
        if (string.IsNullOrEmpty(id) || !JsonUtil.TryNumber(src, "x", out var x) || !JsonUtil.TryNumber(src, "y", out var y))
        {
            return;
        }

        var skillId = JsonUtil.ExtractString(src, "skillId") ?? "";
        DespawnProjectile(id, false);
        // Invisible tracker only — no placeholder cube/sphere projectiles.
        var go = new GameObject("proj_" + id);
        go.transform.SetParent(_root, false);
        go.transform.position = new Vector3(x, y, -0.5f);
        _projectiles[id] = go.transform;
        _projectileSkills[id] = skillId;
    }

    private void ApplyProjectileMove(string json)
    {
        var id = JsonUtil.ExtractString(json, "id");
        if (string.IsNullOrEmpty(id) || !_projectiles.TryGetValue(id, out var t) || t == null)
        {
            return;
        }

        if (!JsonUtil.TryNumber(json, "x", out var x) || !JsonUtil.TryNumber(json, "y", out var y))
        {
            return;
        }

        t.position = new Vector3(x, y, -0.5f);
    }

    private void DespawnProjectile(string id, bool withImpact = true)
    {
        if (string.IsNullOrEmpty(id) || !_projectiles.TryGetValue(id, out var t))
        {
            _projectileSkills.Remove(id);
            return;
        }

        if (withImpact && t != null)
        {
            // Placeholder impact FX removed.
        }

        if (t != null)
        {
            Destroy(t.gameObject);
        }

        _projectiles.Remove(id);
        _projectileSkills.Remove(id);
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

        JsonUtil.TryInt(src, "hp", out var hp);
        JsonUtil.TryInt(src, "maxHp", out var maxHp);
        JsonUtil.TryInt(src, "mp", out var mp);
        JsonUtil.TryInt(src, "maxMp", out var maxMp);
        JsonUtil.TryNumber(src, "hitRadius", out var hr);
        var name = JsonUtil.ExtractString(src, "name");
        var kind = JsonUtil.ExtractString(src, "kind");
        var label = !string.IsNullOrEmpty(name) ? name : (id.Contains("slime") ? "Slime" : id);
        var inferredKind = !string.IsNullOrEmpty(kind)
            ? kind
            : (id.Contains("monster") ? "monster" : (id.StartsWith("npc_") ? "npc" : "player"));
        var color = inferredKind == "player" && id != _selfId
            ? new Color(0.35f, 0.75f, 1f)
            : ColorForEntity(id, inferredKind);
        var fallbackHp = inferredKind == "npc" ? 1 : 40;
        Upsert(id, x, y, color, label, hp > 0 ? hp : fallbackHp, maxHp > 0 ? maxHp : fallbackHp, hr > 0 ? hr : 0.4f, true);
        SetEntityMeta(id, inferredKind, mp, maxMp);
        if (string.IsNullOrEmpty(_lockTargetId) || !_entities.ContainsKey(_lockTargetId))
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

    private void MoveTo(string id, float x, float y, Color color, string label, int hp, int maxHp, float hitRadius)
    {
        Upsert(id, x, y, color, label, hp, maxHp, hitRadius, false);
    }

    private void Upsert(string id, float x, float y, Color color, string label, int hp, int maxHp, float hitRadius, bool instant)
    {
        if (!_entities.TryGetValue(id, out var view))
        {
            var kindGuess = id == _selfId || (!string.IsNullOrEmpty(id) && id.StartsWith("player_"))
                ? "player"
                : (!string.IsNullOrEmpty(id) && id.StartsWith("npc_"))
                    ? "npc"
                    : (!string.IsNullOrEmpty(id) && id.StartsWith("portal_"))
                        ? "npc"
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
                From = new Vector3(x, y, 0f),
                To = new Vector3(x, y, 0f),
                UsesArt = SpriteCatalog.HasArtSprite(id, kindGuess),
                AnimClip = SpriteCatalog.Clip.Idle,
                AnimFrame = 0,
                AnimTime = 0f,
            };
            view.Transform.position = view.To;
        }

        EnsureStatusLists(ref view);
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
            view.Renderer.color = Color.white;
            view.UsesArt = true;
        }
        view.HitRadius = hitRadius > 0 ? hitRadius : 0.4f;
        if (view.HitRing != null)
        {
            var d = view.HitRadius * 2f;
            view.HitRing.localScale = new Vector3(d, d, 1f);
            view.HitRing.localPosition = new Vector3(0f, 0f, 0.1f);
            // Only show rings for procedural markers (no sheet art).
            view.HitRing.gameObject.SetActive(!view.UsesArt);
        }

        var target = new Vector3(x, y, 0f);
        if (instant || view.Transform == null)
        {
            view.From = target;
            view.To = target;
            view.Moving = false;
            view.MoveT = 1f;
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
            UpdateFacingFromDelta(ref view, view.To - view.From);
        }

        _entities[id] = view;
        UpdateStatusMarkerPositions(id);
    }

    public void SetLocalFacing(int facing)
    {
        if (!_entities.TryGetValue(_selfId, out var view))
        {
            return;
        }

        view.Facing = Mathf.Clamp(facing, 0, 3);
        view.JustMovedUntil = Time.time + JustMovedSec;
        _entities[_selfId] = view;
    }

    /// <summary>Camera right/up projected onto the XY ground plane (normalized).</summary>
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
        right = new Vector2(r.x, r.y);
        up = new Vector2(u.x, u.y);
        if (right.sqrMagnitude > 1e-6f)
        {
            right.Normalize();
        }

        if (up.sqrMagnitude > 1e-6f)
        {
            up.Normalize();
        }
    }

    private void UpdateFacingFromDelta(ref EntityView view, Vector3 delta)
    {
        if (delta.sqrMagnitude < 1e-8f)
        {
            return;
        }

        // Map world delta into camera screen space (same basis as movement).
        GetCameraBasisXY(out var scrRight, out var scrUp);
        var rdx = delta.x * scrRight.x + delta.y * scrRight.y;
        var rdy = delta.x * scrUp.x + delta.y * scrUp.y;
        if (Mathf.Abs(rdx) > Mathf.Abs(rdy))
        {
            view.Facing = rdx < 0f ? 1 : 2;
        }
        else
        {
            view.Facing = rdy < 0f ? 0 : 3;
        }
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

            var d = Vector2.Distance(
                new Vector2(selfPos.x, selfPos.y),
                new Vector2(pair.Value.Transform.position.x, pair.Value.Transform.position.y));
            ids.Add(pair.Key);
            dists.Add(d);
        }

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

        return ids;
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

            list[i].position = basePos + new Vector3((i - (list.Count - 1) * 0.5f) * 0.28f, 0f, -0.2f);
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

    private void SpawnPopSphere(Vector3 pos, Color color, float life)
    {
        SpawnTempPrimitive(PrimitiveType.Sphere, pos, Vector3.one * 0.4f, color, life);
    }

    private void SpawnTempPrimitive(PrimitiveType type, Vector3 pos, Vector3 scale, Color color, float life)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = "fx_temp";
        if (_root != null)
        {
            go.transform.SetParent(_root, false);
        }

        StripCollider(go);
        go.transform.position = pos;
        go.transform.localScale = scale;
        if (_cam != null)
        {
            go.transform.rotation = _cam.transform.rotation;
        }

        ApplyUnlitColor(go, color);
        _tempFx.Add(new TempFx { Go = go, Until = Time.time + Mathf.Max(0.05f, life) });
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
        sr.sprite = MakeRedOutlineSprite();
        sr.color = new Color(1f, 0.12f, 0.1f, 1f);
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

        _lockRing.position = new Vector3(view.Transform.position.x, view.Transform.position.y, -0.45f);
        if (_cam != null)
        {
            _lockRing.rotation = _cam.transform.rotation;
        }

        // Outline sprite is authored as ~1×1 world unit.
        _lockRing.localScale = new Vector3(worldW, worldH, 1f);

        if (_lockRingSr == null)
        {
            _lockRingSr = _lockRing.GetComponent<SpriteRenderer>();
        }

        if (_lockRingSr != null)
        {
            var a = view.ThreatSelfPct >= 35f ? 1f : 0.92f;
            _lockRingSr.color = new Color(1f, 0.12f, 0.08f, a);
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
        _cam.orthographicSize = 7f;
        _cam.clearFlags = CameraClearFlags.SolidColor;
        _cam.backgroundColor = new Color(0.08f, 0.09f, 0.12f);
        _cam.nearClipPlane = 0.1f;
        _cam.farClipPlane = 80f;
    }

    private void CenterCamera(float x, float y)
    {
        UpdateOrbitCamera(new Vector3(x, y, 0f));
    }

    private void UpdateOrbitCamera(Vector3 playerPos)
    {
        if (_cam == null)
        {
            return;
        }

        var pitch = CamPitchFromVertical * Mathf.Deg2Rad;
        var yaw = _camYaw * Mathf.Deg2Rad;
        var offset = new Vector3(
            Mathf.Sin(yaw) * Mathf.Sin(pitch),
            -Mathf.Cos(yaw) * Mathf.Sin(pitch),
            -Mathf.Cos(pitch)) * CamDistance;
        _cam.transform.position = playerPos + offset;
        _cam.transform.LookAt(playerPos, Vector3.forward);
    }

    public void ClearSessionEntities()
    {
        ClearForeignEntities();
        if (_entities.TryGetValue(_selfId, out var self) && self.Transform != null)
        {
            self.Transform.position = new Vector3(3f, 6f, 0f);
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
        sr.color = SpriteCatalog.HasArtSprite(useId, useKind) ? Color.white : color;
        sr.sortingOrder = 20;
        ApplyUnlit(sr);
        go.transform.localScale = Vector3.one * SpriteCatalog.EntityScale(useId, useKind);
        return go;
    }

    private static Material _sharedSpriteMat;

    private static void ApplyUnlit(SpriteRenderer sr)
    {
        // Prefer classic Sprites/Default (handles sprite alpha). URP unlit is fine if present.
        if (_sharedSpriteMat == null)
        {
            var shader = Shader.Find("Sprites/Default")
                ?? Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                ?? Shader.Find("Unlit/Transparent");
            if (shader != null)
            {
                _sharedSpriteMat = new Material(shader);
            }
        }

        if (_sharedSpriteMat != null)
        {
            sr.sharedMaterial = _sharedSpriteMat;
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
                var go = new GameObject(isWall ? "wall" : isHazard ? "hazard" : "tile");
                go.transform.SetParent(_root, false);
                go.transform.position = new Vector3(x, y, 1f);
                var sr = go.AddComponent<SpriteRenderer>();
                var even = (x + y) % 2 == 0;
                if (isWall)
                {
                    sr.sprite = SpriteCatalog.ForWall(_mapId);
                    sr.color = Color.white;
                }
                else
                {
                    sr.sprite = SpriteCatalog.ForFloor(_mapId, even);
                    sr.color = isHazard
                        ? new Color(1f, 0.35f, 0.18f, 1f)
                        : even ? Color.white : new Color(0.88f, 0.88f, 0.88f, 1f);
                }

                go.transform.localScale = Vector3.one;
                sr.sortingOrder = 0;
                ApplyUnlit(sr);
            }
        }
    }
}
