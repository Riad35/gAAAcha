using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Map + entities with soft tile lerp and edge-only camera.
/// </summary>
public sealed class GrayBoxWorld : MonoBehaviour
{
    private const float MoveDuration = 0.15f;
    private const float CameraSafe = 0.72f;
    private const float LongRange = 10f;

    private readonly Dictionary<string, EntityView> _entities = new Dictionary<string, EntityView>();
    private Transform _root;
    private Transform _lockRing;
    private SpriteRenderer _lockRingSr;
    private Camera _cam;
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
        SetLockTarget("monster_slime_1");
        CenterCamera(3f, 6f);
    }

    public void RebuildMap(int mapW, int mapH, HashSet<Vector2Int> blocked)
    {
        _mapW = mapW;
        _mapH = mapH;
        foreach (Transform child in _root)
        {
            if (child.name == "tile" || child.name == "wall")
            {
                Destroy(child.gameObject);
            }
        }

        SpawnTileHints(blocked);
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

    public void SetLocalPos(float x, float y)
    {
        MoveTo(_selfId, x, y, new Color(0.15f, 0.85f, 1f), "You", GetHp(_selfId, 100), GetMaxHp(_selfId, 100), GetHitRadius(_selfId, 0.4f));
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

    /// <summary>
    /// Closest living non-self entity whose edge-to-edge gap is within attackRange.
    /// </summary>
    public string FindClosestEnemyInRange(float attackRange)
    {
        if (!_entities.TryGetValue(_selfId, out var self) || self.Transform == null)
        {
            return "";
        }

        string best = "";
        var bestGap = float.MaxValue;
        var selfPos = self.Transform.position;
        var selfR = self.HitRadius > 0f ? self.HitRadius : 0.4f;

        foreach (var pair in _entities)
        {
            if (pair.Key == _selfId || pair.Value.Transform == null || pair.Value.Hp <= 0)
            {
                continue;
            }

            var other = pair.Value;
            var otherR = other.HitRadius > 0f ? other.HitRadius : 0.4f;
            var center = Vector2.Distance(
                new Vector2(selfPos.x, selfPos.y),
                new Vector2(other.Transform.position.x, other.Transform.position.y));
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
        if (!_entities.TryGetValue(_selfId, out var self) || self.Transform == null)
        {
            return "";
        }

        string best = "";
        var bestDist = float.MaxValue;
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
            if (d <= maxRange && d < bestDist)
            {
                bestDist = d;
                best = pair.Key;
            }
        }

        return best;
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

    public string CycleLockTarget()
    {
        var ids = BuildLivingNonSelfSortedByDist();
        if (ids.Count == 0)
        {
            _lockTargetId = "";
            if (_lockRing != null)
            {
                _lockRing.gameObject.SetActive(false);
            }

            return _lockTargetId;
        }

        if (_lockRing != null)
        {
            _lockRing.gameObject.SetActive(true);
        }

        var lockDead = string.IsNullOrEmpty(_lockTargetId) || !IsLiving(_lockTargetId);
        if (lockDead)
        {
            var closest = FindClosestInLongRange(LongRange);
            _lockTargetId = closest;
            UpdateLockRing();
            return _lockTargetId;
        }

        var idx = ids.IndexOf(_lockTargetId);
        if (idx < 0)
        {
            _lockTargetId = ids[0];
        }
        else
        {
            _lockTargetId = ids[(idx + 1) % ids.Count];
        }

        UpdateLockRing();
        return _lockTargetId;
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
        reticle.position = _aimPoint + Vector3.up * 0.05f;
        reticle.localScale = new Vector3(d, 0.04f, d);
        reticle.rotation = Quaternion.identity;
        SetAimPartColor(reticle, new Color(0.4f, 1f, 0.55f, 0.4f));

        var ring = _aimParts[1];
        var rd = _aimCastRange * 2f;
        ring.position = caster + Vector3.up * 0.04f;
        ring.localScale = new Vector3(rd, 0.03f, rd);
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

            var screen = _cam.WorldToScreenPoint(view.Transform.position + Vector3.up * 0.85f);
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

            var screen = _cam.WorldToScreenPoint(ft.World + Vector3.up * (1.2f + (ft.Until - Time.time)));
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
        var keys = new List<string>(_entities.Keys);
        foreach (var key in keys)
        {
            var view = _entities[key];
            if (!view.Moving || view.Transform == null)
            {
                continue;
            }

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

            _entities[key] = view;
            UpdateStatusMarkerPositions(key);
            if (key == _selfId)
            {
                UpdateEdgeCamera(view.Transform.position);
            }
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

    private void LateUpdate()
    {
        UpdateLockRing();
        RefreshAimFromSelf();
        foreach (var key in new List<string>(_entities.Keys))
        {
            var view = _entities[key];
            if (view.Renderer != null && view.Renderer.color == Color.white && Time.frameCount % 8 == 0)
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
        var id = JsonUtil.ExtractString(json, "id");
        if (!string.IsNullOrEmpty(id))
        {
            RemapSelf(id);
        }

        var you = JsonUtil.SliceAround(json, "\"you\"", 0, 420);
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
        ApplyAllMonstersFromState(json);

        if (!_entities.ContainsKey(_lockTargetId))
        {
            CycleLockTarget();
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
        RebuildMap(w, h, blocked);
    }

    private void ApplyAllMonstersFromState(string json)
    {
        // Prefer explicit known ids; also pick up generic monster_ entries when present.
        var markers = new[] { "monster_slime_1", "monster_ember_1", "monster_gust_1", "monster_brute_1" };
        foreach (var marker in markers)
        {
            var slice = JsonUtil.SliceAround(json, marker, 40, 420);
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
            var color = marker.Contains("ember") ? new Color(1f, 0.45f, 0.2f)
                : marker.Contains("gust") ? new Color(0.55f, 0.9f, 1f)
                : marker.Contains("brute") ? new Color(0.7f, 0.45f, 0.85f)
                : new Color(0.2f, 1f, 0.25f);
            var label = !string.IsNullOrEmpty(name) ? name : marker.Replace("monster_", "");
            Upsert(marker, mx, my, color, label, hp > 0 ? hp : 40, maxHp > 0 ? maxHp : 40, hr > 0 ? hr : 0.4f, true);
            SetEntityMeta(marker, string.IsNullOrEmpty(kind) ? "monster" : kind, mp, maxMp);
        }
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
                SpawnPopSphere(view.Transform.position + Vector3.up * 0.9f, StatusColor(kind), 0.35f);
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
        ApplyHitFx(targetId, damage, hpAfter, skillId);
        SpawnSkillVfx(skillId, casterId, targetId, damage);
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

        if (!string.IsNullOrEmpty(centerId))
        {
            SpawnAoeRing(centerId, radius, 0.45f);
        }
        else
        {
            SpawnSkillVfx(skillId, casterId, "", 0);
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
            if (damage > 0)
            {
                SpawnHitFlash(targetId);
            }

            cursor = idx + 10;
        }
    }

    /// <summary>Flat ring indicator around an entity (cast telegraph or confirmed AoE).</summary>
    public void SpawnAoeRing(string centerId, float radius, float life)
    {
        var pos = GetEntityWorldPos(centerId);
        if (!pos.HasValue)
        {
            return;
        }

        var d = Mathf.Max(0.8f, radius * 2f);
        SpawnTempPrimitive(PrimitiveType.Cylinder, pos.Value + Vector3.up * 0.05f,
            new Vector3(d, 0.04f, d), new Color(1f, 0.75f, 0.2f, 0.45f), life);
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
        var text = healish ? "+" + Mathf.Max(1, damage > 0 ? damage : 20) : "-" + damage;
        var color = healish ? new Color(0.4f, 1f, 0.5f) : new Color(1f, 0.35f, 0.25f);
        if ((damage > 0 || healish) && view.Transform != null)
        {
            _floats.Add(new FloatText
            {
                World = view.Transform.position,
                Text = text,
                Until = Time.time + 0.9f,
                Color = color,
            });
        }
    }

    private void SpawnSkillVfx(string skillId, string casterId, string targetId, int damage)
    {
        var sid = skillId ?? "";
        var targetPos = GetEntityWorldPos(targetId);
        var casterPos = GetEntityWorldPos(casterId);

        // Melee beam caster → target (not ranged AA — those use projectiles)
        if (sid == "slash" || sid == "shove" || sid == "pull")
        {
            if (casterPos.HasValue && targetPos.HasValue)
            {
                var a = casterPos.Value + Vector3.up * 0.35f;
                var b = targetPos.Value + Vector3.up * 0.35f;
                var mid = (a + b) * 0.5f;
                var len = Vector3.Distance(a, b);
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "fx_beam";
                go.transform.SetParent(_root, false);
                go.transform.position = mid;
                go.transform.localScale = new Vector3(Mathf.Max(0.3f, len), 0.1f, 0.12f);
                if (len > 0.01f)
                {
                    go.transform.rotation = Quaternion.FromToRotation(Vector3.right, (b - a).normalized);
                }

                StripCollider(go);
                var mr = go.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    mr.material = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color") ?? mr.sharedMaterial.shader);
                    mr.material.color = sid == "slash"
                        ? new Color(1f, 0.2f, 0.1f, 0.85f)
                        : new Color(1f, 0.85f, 0.3f, 0.75f);
                }

                _tempFx.Add(new TempFx { Go = go, Until = Time.time + 0.28f });
            }
        }

        if (sid.Contains("shockwave"))
        {
            if (!string.IsNullOrEmpty(targetId))
            {
                SpawnAoeRing(targetId, 2.5f, 0.45f);
            }
            else if (casterPos.HasValue)
            {
                SpawnTempPrimitive(PrimitiveType.Cylinder, casterPos.Value + Vector3.up * 0.05f,
                    new Vector3(3f, 0.04f, 3f), new Color(0.9f, 0.85f, 0.4f, 0.5f), 0.45f);
            }
        }
        else if (sid == "mend" || sid == "war_cry" || sid == "haste" || sid == "barrier" ||
                 sid == "ward" || sid == "power_chant" || sid == "iron_stance" ||
                 sid == "elemental_focus" || sid == "dash")
        {
            if (casterPos.HasValue)
            {
                SpawnTempPrimitive(PrimitiveType.Sphere, casterPos.Value + Vector3.up * 0.5f,
                    Vector3.one * 0.7f, new Color(0.35f, 0.95f, 1f, 0.8f), 0.5f);
                SpawnTempPrimitive(PrimitiveType.Cylinder, casterPos.Value + Vector3.up * 0.05f,
                    new Vector3(1.6f, 0.03f, 1.6f), new Color(0.4f, 0.9f, 1f, 0.35f), 0.4f);
            }
        }
        else if (sid.Contains("shot") || sid.Contains("stun") || sid.Contains("blind") || sid.Contains("ember"))
        {
            // Impact reserved for projectile despawn; soft pop if instant
            if (targetPos.HasValue && damage > 0)
            {
                SpawnTempPrimitive(PrimitiveType.Cube, targetPos.Value + Vector3.up * 0.3f,
                    Vector3.one * 0.35f, Color.white, 0.25f);
            }
        }

        if (damage > 0 && !string.IsNullOrEmpty(targetId))
        {
            SpawnHitFlash(targetId);
        }
    }

    private void SpawnHitFlash(string targetId)
    {
        var pos = GetEntityWorldPos(targetId);
        if (!pos.HasValue)
        {
            return;
        }

        SpawnTempPrimitive(PrimitiveType.Cube, pos.Value + Vector3.up * 0.2f,
            Vector3.one * 0.55f, new Color(1f, 1f, 1f, 0.75f), 0.25f);
    }

    public void Despawn(string id)
    {
        if (string.IsNullOrEmpty(id) || !_entities.TryGetValue(id, out var view))
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
        var isAa = skillId == "auto_attack";
        var go = GameObject.CreatePrimitive(isAa ? PrimitiveType.Sphere : PrimitiveType.Cube);
        go.name = "proj_" + id;
        go.transform.SetParent(_root, false);
        go.transform.position = new Vector3(x, y, -0.5f);
        go.transform.localScale = Vector3.one * (isAa ? 0.55f : 0.4f);
        StripCollider(go);
        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            mr.material = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color") ?? mr.sharedMaterial.shader);
            mr.material.color = isAa
                ? new Color(1f, 0.95f, 0.35f, 1f)
                : new Color(0.55f, 0.85f, 1f, 1f);
        }

        _projectiles[id] = go.transform;
        _projectileSkills[id] = skillId;

        // Muzzle flash at spawn
        SpawnTempPrimitive(PrimitiveType.Cube, new Vector3(x, y, -0.4f),
            Vector3.one * 0.25f, new Color(1f, 1f, 1f, 0.8f), 0.12f);
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

        var skillId = _projectileSkills.TryGetValue(id, out var s) ? s : "";
        var isAa = skillId == "auto_attack";
        var trailColor = isAa
            ? new Color(1f, 0.9f, 0.25f, 0.4f)
            : new Color(0.5f, 0.8f, 1f, 0.35f);
        var old = t.position;
        var dest = new Vector3(x, y, -0.5f);
        for (var i = 0; i < 3; i++)
        {
            var ghostPos = Vector3.Lerp(old, dest, (i + 1) * 0.28f);
            ghostPos.z = -0.45f;
            var scale = (isAa ? 0.35f : 0.26f) - i * 0.05f;
            SpawnTempPrimitive(isAa ? PrimitiveType.Sphere : PrimitiveType.Cube, ghostPos,
                Vector3.one * scale,
                new Color(trailColor.r, trailColor.g, trailColor.b, trailColor.a - i * 0.08f), 0.2f);
        }

        t.position = dest;
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
            var pos = t.position;
            var skillId = _projectileSkills.TryGetValue(id, out var s) ? s : "";
            var isAa = skillId == "auto_attack";
            SpawnTempPrimitive(PrimitiveType.Cube, pos + Vector3.up * 0.15f,
                Vector3.one * (isAa ? 0.7f : 0.5f), new Color(1f, 1f, 1f, 0.9f), 0.28f);
            SpawnTempPrimitive(PrimitiveType.Sphere, pos + Vector3.up * 0.2f,
                Vector3.one * (isAa ? 0.55f : 0.4f),
                isAa ? new Color(1f, 0.85f, 0.2f, 0.7f) : new Color(0.6f, 0.9f, 1f, 0.7f), 0.22f);
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
        var inferredKind = !string.IsNullOrEmpty(kind) ? kind : (id.Contains("monster") ? "monster" : "player");
        Upsert(id, x, y, new Color(0.2f, 1f, 0.25f), label, hp > 0 ? hp : 40, maxHp > 0 ? maxHp : 40, hr > 0 ? hr : 0.4f, true);
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
            var go = CreateMarker(color, label);
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
                Kind = id == _selfId ? "player" : "monster",
                ThreatTopId = "",
                StatusKinds = new List<string>(),
                StatusUntil = new List<float>(),
                HitRadius = hitRadius,
                From = new Vector3(x, y, 0f),
                To = new Vector3(x, y, 0f),
            };
            view.Transform.position = view.To;
        }

        EnsureStatusLists(ref view);
        view.BaseColor = color;
        view.Hp = hp;
        view.MaxHp = maxHp;
        view.Label = label;
        view.HitRadius = hitRadius > 0 ? hitRadius : 0.4f;
        if (view.HitRing != null)
        {
            var d = view.HitRadius * 2f;
            view.HitRing.localScale = new Vector3(d, d, 1f);
            view.HitRing.localPosition = new Vector3(0f, 0f, 0.1f);
        }

        if (view.Renderer != null && view.Renderer.color != Color.white)
        {
            view.Renderer.color = color;
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
        }

        _entities[id] = view;
        UpdateStatusMarkerPositions(id);
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

        var basePos = view.Transform.position + Vector3.up * 1.05f;
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
        sr.sprite = MakeSprite(Color.white);
        sr.color = new Color(1f, 1f, 1f, 0.2f);
        sr.sortingOrder = 5;
        ApplyUnlit(sr);
        return go;
    }

    private void EnsureLockRing()
    {
        var go = new GameObject("LockRing");
        go.transform.SetParent(_root, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = MakeSprite(Color.white);
        sr.color = new Color(1f, 0.85f, 0.1f, 0.85f);
        sr.sortingOrder = 15;
        ApplyUnlit(sr);
        go.transform.localScale = new Vector3(1.55f, 0.35f, 1f);
        _lockRing = go.transform;
        _lockRingSr = sr;
    }

    private void UpdateLockRing()
    {
        if (_lockRing == null || !_entities.TryGetValue(_lockTargetId, out var view) || view.Transform == null)
        {
            return;
        }

        _lockRing.position = view.Transform.position + new Vector3(0f, -0.55f, 0f);

        if (_lockRingSr == null)
        {
            _lockRingSr = _lockRing.GetComponent<SpriteRenderer>();
        }

        if (_lockRingSr == null)
        {
            return;
        }

        if (view.Kind == "monster" && !string.IsNullOrEmpty(view.ThreatTopId) && view.ThreatTopId == _selfId)
        {
            _lockRingSr.color = new Color(1f, 0.28f, 0.22f, 0.9f);
        }
        else if (view.ThreatSelfPct >= 35f)
        {
            _lockRingSr.color = new Color(1f, 0.92f, 0.15f, 0.9f);
        }
        else
        {
            _lockRingSr.color = new Color(1f, 0.85f, 0.1f, 0.85f);
        }
    }

    private void SetupCamera()
    {
        if (_cam == null)
        {
            return;
        }

        _cam.orthographic = true;
        _cam.orthographicSize = 6f;
        _cam.clearFlags = CameraClearFlags.SolidColor;
        _cam.backgroundColor = new Color(0.08f, 0.09f, 0.12f);
    }

    private void CenterCamera(float x, float y)
    {
        if (_cam == null)
        {
            return;
        }

        _cam.transform.position = new Vector3(x, y, -10f);
    }

    private void UpdateEdgeCamera(Vector3 playerPos)
    {
        if (_cam == null)
        {
            return;
        }

        var camPos = _cam.transform.position;
        var halfH = _cam.orthographicSize;
        var halfW = halfH * _cam.aspect;
        var safeW = halfW * CameraSafe;
        var safeH = halfH * CameraSafe;
        var dx = 0f;
        var dy = 0f;

        if (playerPos.x > camPos.x + safeW)
        {
            dx = playerPos.x - (camPos.x + safeW);
        }
        else if (playerPos.x < camPos.x - safeW)
        {
            dx = playerPos.x - (camPos.x - safeW);
        }

        if (playerPos.y > camPos.y + safeH)
        {
            dy = playerPos.y - (camPos.y + safeH);
        }
        else if (playerPos.y < camPos.y - safeH)
        {
            dy = playerPos.y - (camPos.y - safeH);
        }

        if (dx == 0f && dy == 0f)
        {
            return;
        }

        _cam.transform.position = new Vector3(camPos.x + dx, camPos.y + dy, -10f);
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

    private void SpawnTileHints(HashSet<Vector2Int> blocked = null)
    {
        blocked ??= new HashSet<Vector2Int>
        {
            new Vector2Int(8, 4), new Vector2Int(8, 5), new Vector2Int(8, 6),
        };

        for (var x = 0; x < _mapW; x++)
        {
            for (var y = 0; y < _mapH; y++)
            {
                var isWall = blocked.Contains(new Vector2Int(x, y));
                var go = new GameObject(isWall ? "wall" : "tile");
                go.transform.SetParent(_root, false);
                go.transform.position = new Vector3(x, y, 1f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = MakeSprite(Color.white);
                sr.color = isWall
                    ? new Color(0.55f, 0.2f, 0.2f)
                    : ((x + y) % 2 == 0
                        ? new Color(0.22f, 0.24f, 0.28f)
                        : new Color(0.16f, 0.18f, 0.22f));
                sr.sortingOrder = 0;
                ApplyUnlit(sr);
            }
        }
    }
}
