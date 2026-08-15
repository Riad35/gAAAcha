using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Gray-box harness: continuous move + joystick + skill bar CD/status HUD.
/// </summary>
public sealed class NetworkBootstrap : MonoBehaviour
{
    private static readonly string[] ClassSkills =
    {
        "auto_attack", "slash", "shot", "mend", "dash", "stun_bolt", "ember_dot", "war_cry",
        "shove", "pull", "blind_dust", "iron_stance", "shockwave",
        "power_chant", "haste", "barrier", "ward", "elemental_focus",
    };

    private static readonly HashSet<string> NoTargetSkills = new HashSet<string>
    {
        "mend", "dash", "war_cry", "iron_stance",
        "power_chant", "haste", "barrier", "ward", "elemental_focus",
    };

    private enum AimKind
    {
        Linear,
        Cone,
        Ground,
    }

    private struct AimDef
    {
        public AimKind Kind;
        public float Range;
        public float Width;
        public float AngleDeg;
        public float AoeRadius;
    }

    private static readonly Dictionary<string, AimDef> IndicatorSkills = new Dictionary<string, AimDef>
    {
        { "shot", new AimDef { Kind = AimKind.Linear, Range = 5f, Width = 0.7f } },
        { "stun_bolt", new AimDef { Kind = AimKind.Linear, Range = 4f, Width = 0.7f } },
        { "blind_dust", new AimDef { Kind = AimKind.Cone, Range = 3.5f, AngleDeg = 60f } },
        { "shockwave", new AimDef { Kind = AimKind.Ground, Range = 3.5f, AoeRadius = 2.5f } },
    };

    private static readonly string[] ChatTabs = { "world", "server", "guild", "map" };

    private NetClient _net;
    private InputSender _input;
    private GrayBoxWorld _world;
    private VirtualJoystick _joystick;
    private float _x = 3f;
    private float _y = 10f;
    private float _moveSpeed = 6f;
    private float _moveSpeedMult = 1f;
    private float _lastGoodX = 3f;
    private float _lastGoodY = 10f;
    private float _moveSendAcc;
    private string _weapon = "sword_iron";
    private readonly string[] _weapons =
    {
        "sword_iron", "dagger_twin", "staff_arcane", "bow_hunter", "gun_spark",
    };
    private readonly string[] _spirits =
    {
        "spirit_ember", "spirit_tide", "spirit_gale",
    };
    private int _spiritIndex = -1;
    private string _spirit = "none";
    private float _weaponRange = 1.5f;
    private string _element = "earth";
    private float _moveLockUntil;
    private int _hp = 100;
    private int _maxHp = 100;
    private int _mp = 50;
    private int _maxMp = 50;
    private int _pity;
    private int _hardPity = 80;
    private float _nextSsr = 0.02f;
    private string _lastDrop = "-";
    private string _status = "starting…";
    private string _guestToken = "";
    private readonly ConcurrentQueue<string> _inbox = new ConcurrentQueue<string>();
    private readonly Dictionary<string, float> _readyAtLocal = new Dictionary<string, float>();
    private readonly Dictionary<string, float> _cooldownMs = new Dictionary<string, float>();
    private readonly List<BuffView> _buffs = new List<BuffView>();
    private double _serverSkewMs;
    private string[] _skillIds = ClassSkills;
    private bool _showInventory = true;
    private readonly string[] _invSlots = new string[20];
    private readonly int[] _invQty = new int[20];

    // Context menu
    private bool _showCtxMenu;
    private Vector2 _ctxMenuScreen;
    private string _ctxTargetId = "";
    private string _ctxTargetKind = "";
    private string _ctxTargetLabel = "";

    // Inspect sheet
    private bool _showInspect;
    private string _inspectName = "";
    private string _inspectKind = "";
    private string _inspectStats = "";
    private string _inspectWeaponId = "";
    private string _inspectSpiritId = "";
    private string _inspectResists = "";
    private string _inspectStatuses = "";
    private string _inspectMonsterType = "";

    // Item tooltip
    private string _itemTooltip = "";

    // Chat
    private readonly Dictionary<string, List<string>> _chatLogs = new Dictionary<string, List<string>>();
    private string _chatTab = "world";
    private string _chatInput = "";
    private bool _chatFocused;
    private bool _chatJustFocused;
    private string _whisperTargetName = "";

    // Coming soon toast
    private string _comingSoonToast = "";
    private float _comingSoonUntil;

    // Indicator-cast aim (empty = idle)
    private string _aimSkillId = "";
    private bool _aimFromBar;
    private int _aimStartFrame;
    private float _aimDx = 1f;
    private float _aimDy;
    private float _aimX;
    private float _aimY;

    private struct BuffView
    {
        public string Id;
        public string Kind;
        public float UntilLocal;
    }

    private void Awake()
    {
        _guestToken = PlayerPrefs.GetString("gaaacha_guest", "");
        if (string.IsNullOrEmpty(_guestToken))
        {
            _guestToken = System.Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString("gaaacha_guest", _guestToken);
            PlayerPrefs.Save();
        }

        _world = gameObject.AddComponent<GrayBoxWorld>();
        _world.Boot(36, 20);
        _joystick = new VirtualJoystick(new Rect(0, 0, Screen.width * 0.45f, Screen.height * 0.7f));
        foreach (var id in ClassSkills)
        {
            _cooldownMs[id] = 1000f;
            _readyAtLocal[id] = 0f;
        }

        for (var i = 0; i < ChatTabs.Length; i++)
        {
            _chatLogs[ChatTabs[i]] = new List<string>();
        }

        _status = "Awake";
    }

    private async void Start()
    {
        _status = "connecting…";
        _net = new NetClient();
        _net.MessageReceived += msg => _inbox.Enqueue(msg);
        _input = new InputSender(_net);

        try
        {
            await _net.ConnectAsync(NetClient.DefaultUrl);
            await _net.SendRawAsync(
                "{\"type\":\"request_hello\",\"guestToken\":\"" + _guestToken + "\"}");
            _status = "CONNECTED";
        }
        catch (System.Exception ex)
        {
            _status = "CONNECT FAILED: " + ex.Message;
            Debug.LogError("gAAAcha: " + ex.Message);
        }
    }

    private void Update()
    {
        while (_inbox.TryDequeue(out var json))
        {
            HandlePacket(json);
        }

        _joystick?.Tick();
        TickAiming();
        HandleContinuousMove();
        HandleActionKeys();
        TrySkillBarClicks();
        TryInventoryClicks();
        TryContextMenuOpen();
        TryContextMenuClicks();
        TryChat();
    }

    private void OnGUI()
    {
        DrawHud();
        DrawInventory();
        DrawSkillBar();
        DrawBuffRow();
        DrawTargetFrame();
        DrawContextMenu();
        DrawInspectSheet();
        DrawChatBox();
        DrawToast();
        DrawItemTooltip();
        _joystick?.Draw();
        if (_world != null)
        {
            _world.DrawOverlays();
        }
    }

    private void HandlePacket(string json)
    {
        Debug.Log("gAAAcha recv: " + json);
        _world.HandleMessage(json);

        if (json.Contains("\"type\":\"sync_state\""))
        {
            var you = JsonUtil.SliceAround(json, "\"you\"", 0, 400);
            if (JsonUtil.TryInt(you, "hp", out var hp))
            {
                _hp = hp;
            }
            if (JsonUtil.TryInt(you, "maxHp", out var maxHp) && maxHp > 0)
            {
                _maxHp = maxHp;
            }
            if (JsonUtil.TryInt(you, "mp", out var mp))
            {
                _mp = mp;
            }
            if (JsonUtil.TryInt(you, "maxMp", out var maxMp) && maxMp > 0)
            {
                _maxMp = maxMp;
            }
            if (JsonUtil.TryNumber(you, "x", out var x))
            {
                _x = x;
                _lastGoodX = x;
            }
            if (JsonUtil.TryNumber(you, "y", out var y))
            {
                _y = y;
                _lastGoodY = y;
            }
            if (JsonUtil.TryNumber(you, "moveSpeed", out var ms) && ms > 0)
            {
                _moveSpeed = ms;
            }

            var token = JsonUtil.ExtractString(json, "guestToken");
            if (!string.IsNullOrEmpty(token))
            {
                _guestToken = token;
                PlayerPrefs.SetString("gaaacha_guest", token);
                PlayerPrefs.Save();
            }

            var pitySlice = JsonUtil.SliceAround(json, "\"pity\"", 0, 220);
            if (pitySlice.Length > 0)
            {
                if (JsonUtil.TryInt(pitySlice, "count", out var pity))
                {
                    _pity = pity;
                }
                if (JsonUtil.TryInt(pitySlice, "hardPity", out var hard))
                {
                    _hardPity = hard;
                }
                if (JsonUtil.TryNumber(pitySlice, "nextSsrChance", out var chance))
                {
                    _nextSsr = chance;
                }
            }

            ApplyCooldownsFromJson(json);
            ApplyInventoryFromJson(json);
            RefreshWeaponMeta();
            var spirit = JsonUtil.ExtractString(json, "equippedSpiritId");
            if (!string.IsNullOrEmpty(spirit) && spirit != "null")
            {
                _spirit = spirit;
                _spiritIndex = System.Array.IndexOf(_spirits, spirit);
            }

            _status = "sync_state";
            return;
        }

        if (json.Contains("\"type\":\"sync_inventory\"") || json.Contains("\"type\":\"sync_loot\"") || json.Contains("\"type\":\"sync_gacha\""))
        {
            ApplyInventoryFromJson(json);
        }

        if (json.Contains("\"type\":\"sync_cooldowns\""))
        {
            ApplyCooldownsFromJson(json);
            return;
        }

        if (json.Contains("\"type\":\"sync_threat\""))
        {
            if (_world != null && _world.TryAutoLockFromThreat(_world.SelfId))
            {
                _input?.SetTarget(_world.LockTargetId);
                _status = "auto-lock " + _world.LockTargetId;
            }

            return;
        }

        if (json.Contains("\"type\":\"sync_status\""))
        {
            var entityId = JsonUtil.ExtractString(json, "entityId");
            if (entityId == _world.SelfId)
            {
                ApplyStatusesFromJson(json);
            }

            return;
        }

        if (json.Contains("\"type\":\"sync_inspect\""))
        {
            ApplyInspectFromJson(json);
            _showInspect = true;
            _status = "inspect " + _inspectName;
            return;
        }

        if (json.Contains("\"type\":\"sync_chat\""))
        {
            ApplyChatFromJson(json);
            return;
        }

        if (json.Contains("\"code\":\"coming_soon\""))
        {
            var msg = JsonUtil.ExtractString(json, "message");
            _comingSoonToast = string.IsNullOrEmpty(msg) ? "Coming soon" : msg;
            _comingSoonUntil = Time.time + 2.5f;
            _status = _comingSoonToast;
            return;
        }

        if (json.Contains("\"type\":\"sync_move\""))
        {
            var id = JsonUtil.ExtractString(json, "entityId");
            if (id == _world.SelfId && JsonUtil.TryNumber(json, "x", out var x) && JsonUtil.TryNumber(json, "y", out var y))
            {
                _x = x;
                _y = y;
                _lastGoodX = x;
                _lastGoodY = y;
                _moveLockUntil = Time.time + 0.25f;
                _status = "sync_move " + x.ToString("0.0") + "," + y.ToString("0.0");
            }

            return;
        }

        if (json.Contains("\"code\":\"move_locked\"") || json.Contains("\"code\":\"too_fast\"") || json.Contains("\"code\":\"blocked_entity\"") || json.Contains("\"code\":\"blocked\""))
        {
            _x = _lastGoodX;
            _y = _lastGoodY;
            _world.SetLocalPos(_x, _y);
            if (json.Contains("move_locked"))
            {
                _moveLockUntil = Time.time + 0.25f;
            }

            _status = json.Contains("blocked_entity") ? "blocked entity"
                : json.Contains("move_locked") ? "move_locked"
                : json.Contains("blocked") ? "blocked tile"
                : "too_fast snap";
            return;
        }

        if (json.Contains("\"type\":\"sync_skill\"") || json.Contains("\"type\":\"sync_aoe\""))
        {
            if (JsonUtil.TryInt(json, "mpAfter", out var mpAfter))
            {
                _mp = mpAfter;
            }

            var target = JsonUtil.ExtractString(json, "targetId");
            if (target == _world.SelfId && JsonUtil.TryInt(json, "hpAfter", out var hpAfter))
            {
                _hp = hpAfter;
            }

            _status = "skill " + JsonUtil.ExtractString(json, "skillId");
            return;
        }

        if (json.Contains("\"type\":\"sync_vitals\""))
        {
            var id = JsonUtil.ExtractString(json, "entityId");
            if (id == _world.SelfId)
            {
                if (JsonUtil.TryInt(json, "hp", out var hp))
                {
                    _hp = hp;
                }
                if (JsonUtil.TryInt(json, "mp", out var mp))
                {
                    _mp = mp;
                }
            }

            return;
        }

        if (json.Contains("\"type\":\"sync_gacha\""))
        {
            var pitySlice = JsonUtil.SliceAround(json, "\"pity\"", 0, 220);
            if (pitySlice.Length > 0)
            {
                if (JsonUtil.TryInt(pitySlice, "count", out var pity))
                {
                    _pity = pity;
                }
                if (JsonUtil.TryInt(pitySlice, "hardPity", out var hard))
                {
                    _hardPity = hard;
                }
                if (JsonUtil.TryNumber(pitySlice, "nextSsrChance", out var chance))
                {
                    _nextSsr = chance;
                }
            }

            var results = JsonUtil.SliceAround(json, "\"results\"", 0, 260);
            _lastDrop = JsonUtil.ExtractString(results, "itemId");
            var rarity = JsonUtil.ExtractString(results, "rarity");
            if (!string.IsNullOrEmpty(rarity))
            {
                _lastDrop = _lastDrop + " (" + rarity + ")";
            }

            _status = "gacha pity " + _pity + "/" + _hardPity;
            return;
        }

        if (json.Contains("\"type\":\"sync_equip\""))
        {
            var wid = JsonUtil.ExtractString(json, "weaponId");
            if (!string.IsNullOrEmpty(wid))
            {
                _weapon = wid;
                RefreshWeaponMeta();
            }

            if (json.Contains("\"spiritId\":null"))
            {
                _spirit = "none";
                _spiritIndex = -1;
            }
            else
            {
                var sid = JsonUtil.ExtractString(json, "spiritId");
                if (!string.IsNullOrEmpty(sid))
                {
                    _spirit = sid;
                    _spiritIndex = System.Array.IndexOf(_spirits, sid);
                }
            }

            _status = "equipped " + _weapon + " / " + _spirit;
            return;
        }

        if (json.Contains("\"type\":\"sync_loot\""))
        {
            _lastDrop = JsonUtil.ExtractString(json, "itemId") + " x" +
                (JsonUtil.TryInt(json, "quantity", out var qty) ? qty.ToString() : "1");
            _status = "loot " + _lastDrop;
            return;
        }

        if (json.Contains("\"type\":\"sync_despawn\""))
        {
            _status = "killed " + JsonUtil.ExtractString(json, "entityId");
            return;
        }

        if (json.Contains("\"type\":\"sync_spawn\""))
        {
            _status = "respawned " + JsonUtil.ExtractString(json, "id");
        }
    }

    private void ApplyInspectFromJson(string json)
    {
        _inspectName = JsonUtil.ExtractString(json, "name");
        _inspectKind = JsonUtil.ExtractString(json, "kind");
        JsonUtil.TryInt(json, "hp", out var hp);
        JsonUtil.TryInt(json, "maxHp", out var maxHp);
        JsonUtil.TryInt(json, "mp", out var mp);
        JsonUtil.TryInt(json, "maxMp", out var maxMp);
        JsonUtil.TryInt(json, "atk", out var atk);
        JsonUtil.TryInt(json, "magicAtk", out var matk);
        JsonUtil.TryInt(json, "def", out var def);
        JsonUtil.TryInt(json, "magicResist", out var mdef);
        JsonUtil.TryNumber(json, "attackSpeed", out var aspd);
        JsonUtil.TryNumber(json, "moveSpeed", out var mspd);
        JsonUtil.TryNumber(json, "critChance", out var crit);
        JsonUtil.TryNumber(json, "critDamage", out var critDmg);
        _inspectStats =
            "HP " + hp + "/" + maxHp + "  MP " + mp + "/" + maxMp + "\n" +
            "ATK " + atk + "  MATK " + matk + "  DEF " + def + "  MDEF " + mdef + "\n" +
            "ASPD " + aspd.ToString("0.00") + "  SPD " + mspd.ToString("0.0") +
            "  Crit " + (crit * 100f).ToString("0") + "% x" + critDmg.ToString("0.00");

        _inspectWeaponId = JsonUtil.ExtractString(json, "weaponId");
        if (json.Contains("\"spiritId\":null"))
        {
            _inspectSpiritId = "";
        }
        else
        {
            _inspectSpiritId = JsonUtil.ExtractString(json, "spiritId");
        }

        _inspectMonsterType = JsonUtil.ExtractString(json, "monsterType");
        _inspectResists = FormatResists(json);
        _inspectStatuses = FormatStatusKinds(json);
    }

    private static string FormatResists(string json)
    {
        var slice = JsonUtil.SliceAround(json, "\"resist\"", 0, 220);
        if (slice.Length == 0)
        {
            return "";
        }

        var keys = new[] { "fire", "water", "wind", "earth", "holy", "dark" };
        var parts = new List<string>();
        for (var i = 0; i < keys.Length; i++)
        {
            if (JsonUtil.TryNumber(slice, keys[i], out var v))
            {
                parts.Add(keys[i] + " " + v.ToString("0"));
            }
        }

        if (parts.Count == 0)
        {
            return "";
        }

        var sb = parts[0];
        for (var i = 1; i < parts.Count; i++)
        {
            sb += "  " + parts[i];
        }

        return sb;
    }

    private static string FormatStatusKinds(string json)
    {
        var statusesIdx = json.IndexOf("\"statuses\"");
        if (statusesIdx < 0)
        {
            return "";
        }

        var part = json.Substring(statusesIdx);
        var kinds = new List<string>();
        var cursor = 0;
        while (cursor < part.Length)
        {
            var kIdx = part.IndexOf("\"kind\"", cursor);
            if (kIdx < 0)
            {
                break;
            }

            var slice = part.Substring(kIdx, Mathf.Min(120, part.Length - kIdx));
            var kind = JsonUtil.ExtractString(slice, "kind");
            if (!string.IsNullOrEmpty(kind))
            {
                kinds.Add(kind);
            }

            cursor = kIdx + 6;
        }

        if (kinds.Count == 0)
        {
            return "";
        }

        var sb = kinds[0];
        for (var i = 1; i < kinds.Count; i++)
        {
            sb += ", " + kinds[i];
        }

        return sb;
    }

    private void ApplyChatFromJson(string json)
    {
        var channel = JsonUtil.ExtractString(json, "channel");
        var fromName = JsonUtil.ExtractString(json, "fromName");
        var text = JsonUtil.ExtractString(json, "text");
        var line = fromName + ": " + text;
        var tab = "world";

        if (channel == "whisper")
        {
            line = "[w] " + line;
            tab = "world";
        }
        else if (channel == "guild")
        {
            tab = "guild";
        }
        else if (channel == "server")
        {
            tab = "server";
        }
        else if (channel == "map")
        {
            tab = "map";
        }
        else
        {
            tab = "world";
        }

        if (!_chatLogs.TryGetValue(tab, out var log))
        {
            log = new List<string>();
            _chatLogs[tab] = log;
        }

        log.Add(line);
        while (log.Count > 40)
        {
            log.RemoveAt(0);
        }
    }

    private void ApplyCooldownsFromJson(string json)
    {
        if (JsonUtil.TryNumber(json, "serverTime", out var serverTime))
        {
            _serverSkewMs = serverTime - (Time.realtimeSinceStartupAsDouble * 1000.0);
        }

        var cursor = 0;
        while (true)
        {
            var idx = json.IndexOf("\"id\":", cursor);
            if (idx < 0)
            {
                break;
            }

            var slice = json.Substring(idx, Mathf.Min(160, json.Length - idx));
            var id = JsonUtil.ExtractString(slice, "id");
            if (string.IsNullOrEmpty(id) || id.StartsWith("monster_") || id.StartsWith("player_"))
            {
                cursor = idx + 5;
                continue;
            }

            if (JsonUtil.TryNumber(slice, "readyAt", out var readyAt))
            {
                var localReady = (float)((readyAt - _serverSkewMs) / 1000.0);
                _readyAtLocal[id] = localReady;
            }

            if (JsonUtil.TryNumber(slice, "cooldownMs", out var cd))
            {
                _cooldownMs[id] = cd;
            }

            cursor = idx + 5;
        }
    }

    private void ApplyStatusesFromJson(string json)
    {
        if (JsonUtil.TryNumber(json, "serverTime", out var serverTime))
        {
            _serverSkewMs = serverTime - (Time.realtimeSinceStartupAsDouble * 1000.0);
        }

        _buffs.Clear();
        _moveSpeedMult = 1f;
        var cursor = 0;
        while (true)
        {
            var idx = json.IndexOf("\"kind\":", cursor);
            if (idx < 0)
            {
                break;
            }

            var slice = json.Substring(Mathf.Max(0, idx - 40), Mathf.Min(220, json.Length - Mathf.Max(0, idx - 40)));
            var kind = JsonUtil.ExtractString(slice, "kind");
            var id = JsonUtil.ExtractString(slice, "id");
            if (string.IsNullOrEmpty(kind))
            {
                cursor = idx + 7;
                continue;
            }

            JsonUtil.TryNumber(slice, "until", out var until);
            var untilLocal = (float)((until - _serverSkewMs) / 1000.0);
            if (untilLocal > Time.realtimeSinceStartup)
            {
                _buffs.Add(new BuffView { Id = id, Kind = kind, UntilLocal = untilLocal });
                if (kind == "speed_mult" && JsonUtil.TryNumber(slice, "moveSpeedMult", out var msm))
                {
                    _moveSpeedMult *= msm;
                }
            }

            cursor = idx + 7;
        }
    }

    private void HandleContinuousMove()
    {
        if (Time.time < _moveLockUntil)
        {
            return;
        }

        var dir = ReadMoveIntent();
        if (dir.sqrMagnitude < 0.01f)
        {
            return;
        }

        dir.Normalize();
        var speed = _moveSpeed * _moveSpeedMult;
        var dt = Time.deltaTime;
        _x += dir.x * speed * dt;
        _y += dir.y * speed * dt;
        _world.SetLocalPos(_x, _y);

        _moveSendAcc += dt;
        if (_moveSendAcc < 0.05f)
        {
            return;
        }

        _moveSendAcc = 0f;
        if (_input != null && _net != null && _net.IsConnected)
        {
            _input.RequestMove(_x, _y);
            _status = "move " + _x.ToString("0.0") + "," + _y.ToString("0.0");
        }
    }

    private Vector2 ReadMoveIntent()
    {
        var v = _joystick != null ? _joystick.Axis : Vector2.zero;
        if (_chatFocused)
        {
            return v;
        }

        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed)
            {
                v.y += 1f;
            }
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed)
            {
                v.y -= 1f;
            }
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)
            {
                v.x -= 1f;
            }
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed)
            {
                v.x += 1f;
            }
        }

        return v;
    }

    private void HandleActionKeys()
    {
        var kb = Keyboard.current;
        if (kb == null)
        {
            return;
        }

        if (kb.escapeKey.wasPressedThisFrame)
        {
            if (!string.IsNullOrEmpty(_aimSkillId))
            {
                CancelAim();
                return;
            }

            _showCtxMenu = false;
            _showInspect = false;
            _chatFocused = false;
            _itemTooltip = "";
            return;
        }

        if (kb.tabKey.wasPressedThisFrame && !_chatFocused)
        {
            var target = _world.CycleLockTarget();
            _input?.SetTarget(target);
            _status = "lock-on " + target;
        }

        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
        {
            // send / unfocus handled in TryChat when focused; otherwise open focus
            if (!_chatFocused)
            {
                _chatFocused = true;
                _chatJustFocused = true;
            }

            return;
        }

        if (_chatFocused)
        {
            return;
        }

        if (_input == null || _net == null || !_net.IsConnected)
        {
            return;
        }

        // While aiming: allow cancel/confirm via hotkeys; block other skill casts.
        if (!string.IsNullOrEmpty(_aimSkillId))
        {
            if (TryConfirmAimHotkey(kb))
            {
                return;
            }

            if (kb.spaceKey.wasPressedThisFrame || kb.digit1Key.wasPressedThisFrame ||
                kb.digit3Key.wasPressedThisFrame || kb.qKey.wasPressedThisFrame ||
                kb.rKey.wasPressedThisFrame || kb.digit4Key.wasPressedThisFrame ||
                kb.digit5Key.wasPressedThisFrame || kb.digit7Key.wasPressedThisFrame ||
                kb.uKey.wasPressedThisFrame || kb.iKey.wasPressedThisFrame ||
                kb.oKey.wasPressedThisFrame || kb.pKey.wasPressedThisFrame ||
                kb.yKey.wasPressedThisFrame)
            {
                return;
            }
        }

        if (kb.spaceKey.wasPressedThisFrame)
        {
            CastSkill("auto_attack");
        }
        if (kb.digit1Key.wasPressedThisFrame)
        {
            CastSkill("slash");
        }
        if (kb.digit2Key.wasPressedThisFrame)
        {
            BeginOrConfirmAim("shot");
        }
        if (kb.digit3Key.wasPressedThisFrame)
        {
            CastSkill("mend");
        }
        if (kb.qKey.wasPressedThisFrame)
        {
            CastSkill("dash");
        }
        if (kb.eKey.wasPressedThisFrame)
        {
            BeginOrConfirmAim("stun_bolt");
        }
        if (kb.rKey.wasPressedThisFrame)
        {
            CastSkill("ember_dot");
        }
        if (kb.fKey.wasPressedThisFrame)
        {
            ToggleInspectOnLock();
        }
        if (kb.digit4Key.wasPressedThisFrame)
        {
            CastSkill("shove");
        }
        if (kb.digit5Key.wasPressedThisFrame)
        {
            CastSkill("pull");
        }
        if (kb.digit6Key.wasPressedThisFrame)
        {
            BeginOrConfirmAim("blind_dust");
        }
        if (kb.digit7Key.wasPressedThisFrame)
        {
            CastSkill("iron_stance");
        }
        if (kb.digit8Key.wasPressedThisFrame)
        {
            BeginOrConfirmAim("shockwave");
        }
        if (kb.uKey.wasPressedThisFrame)
        {
            CastSkill("power_chant");
        }
        if (kb.bKey.wasPressedThisFrame)
        {
            _showInventory = !_showInventory;
            _status = _showInventory ? "inventory on" : "inventory off";
        }
        if (kb.iKey.wasPressedThisFrame)
        {
            CastSkill("haste");
        }
        if (kb.oKey.wasPressedThisFrame)
        {
            CastSkill("barrier");
        }
        if (kb.pKey.wasPressedThisFrame)
        {
            CastSkill("ward");
        }
        if (kb.yKey.wasPressedThisFrame)
        {
            CastSkill("elemental_focus");
        }
        if (kb.mKey.wasPressedThisFrame)
        {
            CycleSpirit();
        }
        if (kb.gKey.wasPressedThisFrame)
        {
            _input.RequestGacha(1);
            _status = "sent gacha";
        }
        if (kb.tKey.wasPressedThisFrame)
        {
            _input.RequestGacha(10);
            _status = "sent 10-pull";
        }

        TryEquipKey(kb);
    }

    private void ToggleInspectOnLock()
    {
        if (_showInspect)
        {
            _showInspect = false;
            _status = "inspect closed";
            return;
        }

        var id = _world != null ? _world.LockTargetId : "";
        if (string.IsNullOrEmpty(id))
        {
            _status = "inspect — no lock";
            return;
        }

        _input?.RequestInspect(id);
        _status = "inspect req " + id;
    }

    private void TryChat()
    {
        if (!_chatFocused)
        {
            return;
        }

        var kb = Keyboard.current;
        if (kb == null)
        {
            return;
        }

        if (!(kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
        {
            return;
        }

        // Same-frame open: HandleActionKeys just focused with empty input — ignore.
        // Later empty Enter toggles focus off.
        if (string.IsNullOrEmpty(_chatInput))
        {
            if (!_chatJustFocused)
            {
                _chatFocused = false;
            }

            _chatJustFocused = false;
            return;
        }

        _chatJustFocused = false;
        SendChatLine();
    }

    private void SendChatLine()
    {
        if (_input == null || string.IsNullOrEmpty(_chatInput))
        {
            return;
        }

        var text = _chatInput;
        _chatInput = "";
        if (!string.IsNullOrEmpty(_whisperTargetName))
        {
            _input.RequestChat("whisper", text, _whisperTargetName);
            _status = "whisper → " + _whisperTargetName;
            return;
        }

        _input.RequestChat(_chatTab, text, null);
        _status = "chat " + _chatTab;
    }

    private void CastSkill(string skillId)
    {
        if (_input == null || _world == null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(_aimSkillId))
        {
            return;
        }

        if (IndicatorSkills.ContainsKey(skillId))
        {
            BeginAim(skillId, fromBar: false);
            return;
        }

        if (NoTargetSkills.Contains(skillId))
        {
            _input.SetTarget(_world.SelfId);
            _input.Cast(skillId);
            _status = skillId;
            return;
        }

        if (skillId == "auto_attack")
        {
            var closest = _world.FindClosestEnemyInRange(_weaponRange);
            if (string.IsNullOrEmpty(closest))
            {
                _status = "AA — no enemy in range";
                return;
            }

            _world.SetLockTarget(closest);
            _input.SetTarget(closest);
            _input.Cast(skillId);
            _status = "AA → " + closest;
            return;
        }

        var lockId = _world.LockTargetId;
        if (string.IsNullOrEmpty(lockId) || lockId == _world.SelfId)
        {
            lockId = _world.FindClosestEnemyInRange(10f);
            if (!string.IsNullOrEmpty(lockId))
            {
                _world.SetLockTarget(lockId);
            }
        }

        if (string.IsNullOrEmpty(lockId))
        {
            _status = skillId + " — need target";
            return;
        }

        _input.SetTarget(lockId);
        _input.Cast(skillId);
        _status = skillId + " → " + lockId;
    }

    private void BeginOrConfirmAim(string skillId)
    {
        if (!string.IsNullOrEmpty(_aimSkillId))
        {
            if (_aimSkillId == skillId && Time.frameCount > _aimStartFrame)
            {
                ConfirmAim();
            }

            return;
        }

        BeginAim(skillId, fromBar: false);
    }

    private void BeginAim(string skillId, bool fromBar)
    {
        if (_world == null || _input == null || !IndicatorSkills.ContainsKey(skillId))
        {
            return;
        }

        _aimSkillId = skillId;
        _aimFromBar = fromBar;
        _aimStartFrame = Time.frameCount;
        _aimDx = 1f;
        _aimDy = 0f;
        var selfPos = _world.GetEntityWorldPos(_world.SelfId);
        if (selfPos.HasValue)
        {
            _aimX = selfPos.Value.x + 1f;
            _aimY = selfPos.Value.y;
        }
        else
        {
            _aimX = _x + 1f;
            _aimY = _y;
        }

        UpdateAimVisuals();
        _status = "aim " + skillId;
    }

    private void CancelAim()
    {
        if (string.IsNullOrEmpty(_aimSkillId))
        {
            return;
        }

        _aimSkillId = "";
        _aimFromBar = false;
        _world?.ClearAimIndicator();
        _status = "aim cancel";
    }

    private void ConfirmAim()
    {
        if (string.IsNullOrEmpty(_aimSkillId) || _input == null || _world == null)
        {
            return;
        }

        if (!IndicatorSkills.TryGetValue(_aimSkillId, out var def))
        {
            CancelAim();
            return;
        }

        var skillId = _aimSkillId;
        var lockId = _world.LockTargetId;
        if (string.IsNullOrEmpty(lockId) || lockId == _world.SelfId)
        {
            lockId = "";
        }

        _input.SetTarget(lockId);
        if (def.Kind == AimKind.Ground)
        {
            _input.Cast(skillId, lockId, null, null, _aimX, _aimY);
        }
        else
        {
            _input.Cast(skillId, lockId, _aimDx, _aimDy);
        }

        _aimSkillId = "";
        _aimFromBar = false;
        _world.ClearAimIndicator();
        _status = skillId + " aim cast";
    }

    private void TickAiming()
    {
        if (string.IsNullOrEmpty(_aimSkillId) || _world == null)
        {
            return;
        }

        UpdateAimVisuals();

        var mouse = Mouse.current;
        if (mouse != null)
        {
            if (!_aimFromBar && mouse.leftButton.wasPressedThisFrame && Time.frameCount > _aimStartFrame)
            {
                ConfirmAim();
                return;
            }

            if (_aimFromBar && mouse.leftButton.wasReleasedThisFrame && Time.frameCount > _aimStartFrame)
            {
                ConfirmAim();
                return;
            }
        }

        var kb = Keyboard.current;
        if (!_aimFromBar && kb != null && Time.frameCount > _aimStartFrame)
        {
            if (IsAimHotkeyReleased(kb, _aimSkillId) || !IsAimHotkeyDown(kb, _aimSkillId))
            {
                ConfirmAim();
            }
        }
    }

    private void UpdateAimVisuals()
    {
        if (string.IsNullOrEmpty(_aimSkillId) || _world == null ||
            !IndicatorSkills.TryGetValue(_aimSkillId, out var def))
        {
            return;
        }

        var casterPos = _world.GetEntityWorldPos(_world.SelfId) ?? new Vector3(_x, _y, 0f);
        var mouseWorld = ReadMouseWorld();
        var dir = new Vector2(mouseWorld.x - casterPos.x, mouseWorld.y - casterPos.y);
        if (dir.sqrMagnitude < 0.0001f)
        {
            dir = new Vector2(_aimDx, _aimDy);
            if (dir.sqrMagnitude < 0.0001f)
            {
                dir = Vector2.right;
            }
        }

        dir.Normalize();
        _aimDx = dir.x;
        _aimDy = dir.y;

        if (def.Kind == AimKind.Linear)
        {
            _world.UpdateLinearAim(casterPos, dir, def.Range, def.Width);
        }
        else if (def.Kind == AimKind.Cone)
        {
            _world.UpdateConeAim(casterPos, dir, def.Range, def.AngleDeg);
        }
        else
        {
            var point = new Vector3(mouseWorld.x, mouseWorld.y, casterPos.z);
            var flat = new Vector2(point.x - casterPos.x, point.y - casterPos.y);
            if (flat.magnitude > def.Range)
            {
                flat = flat.normalized * def.Range;
            }

            _aimX = casterPos.x + flat.x;
            _aimY = casterPos.y + flat.y;
            _world.UpdateGroundAim(casterPos, new Vector3(_aimX, _aimY, casterPos.z), def.Range, def.AoeRadius);
        }
    }

    private Vector3 ReadMouseWorld()
    {
        var cam = Camera.main;
        var mouse = Mouse.current;
        if (cam == null || mouse == null)
        {
            return new Vector3(_x + _aimDx, _y + _aimDy, 0f);
        }

        var screen = mouse.position.ReadValue();
        var world = cam.ScreenToWorldPoint(screen);
        world.z = 0f;
        return world;
    }

    private bool TryConfirmAimHotkey(Keyboard kb)
    {
        if (string.IsNullOrEmpty(_aimSkillId) || Time.frameCount <= _aimStartFrame)
        {
            return false;
        }

        if (IsAimHotkeyPressed(kb, _aimSkillId))
        {
            ConfirmAim();
            return true;
        }

        return false;
    }

    private static bool IsAimHotkeyPressed(Keyboard kb, string skillId)
    {
        if (skillId == "shot")
        {
            return kb.digit2Key.wasPressedThisFrame;
        }

        if (skillId == "stun_bolt")
        {
            return kb.eKey.wasPressedThisFrame;
        }

        if (skillId == "blind_dust")
        {
            return kb.digit6Key.wasPressedThisFrame;
        }

        if (skillId == "shockwave")
        {
            return kb.digit8Key.wasPressedThisFrame;
        }

        return false;
    }

    private static bool IsAimHotkeyDown(Keyboard kb, string skillId)
    {
        if (skillId == "shot")
        {
            return kb.digit2Key.isPressed;
        }

        if (skillId == "stun_bolt")
        {
            return kb.eKey.isPressed;
        }

        if (skillId == "blind_dust")
        {
            return kb.digit6Key.isPressed;
        }

        if (skillId == "shockwave")
        {
            return kb.digit8Key.isPressed;
        }

        return false;
    }

    private static bool IsAimHotkeyReleased(Keyboard kb, string skillId)
    {
        if (skillId == "shot")
        {
            return kb.digit2Key.wasReleasedThisFrame;
        }

        if (skillId == "stun_bolt")
        {
            return kb.eKey.wasReleasedThisFrame;
        }

        if (skillId == "blind_dust")
        {
            return kb.digit6Key.wasReleasedThisFrame;
        }

        if (skillId == "shockwave")
        {
            return kb.digit8Key.wasReleasedThisFrame;
        }

        return false;
    }

    private void TrySkillBarClicks()
    {
        if (_chatFocused)
        {
            return;
        }

        var mouse = Mouse.current;
        if (mouse == null)
        {
            return;
        }

        if (_aimFromBar && !string.IsNullOrEmpty(_aimSkillId) &&
            mouse.leftButton.wasReleasedThisFrame && Time.frameCount > _aimStartFrame)
        {
            ConfirmAim();
            return;
        }

        if (!mouse.leftButton.wasPressedThisFrame)
        {
            return;
        }

        if (!string.IsNullOrEmpty(_aimSkillId) && !_aimFromBar)
        {
            // LMB confirm handled in TickAiming; don't start other bar skills.
            return;
        }

        var p = mouse.position.ReadValue();
        var guiY = Screen.height - p.y;
        var slotW = 56f;
        var slotH = 56f;
        var startX = 20f;
        var y = Screen.height - 120f;
        for (var i = 0; i < _skillIds.Length; i++)
        {
            var row = i / 9;
            var col = i % 9;
            var rect = new Rect(startX + col * (slotW + 6), y - row * (slotH + 8), slotW, slotH);
            if (!rect.Contains(new Vector2(p.x, guiY)))
            {
                continue;
            }

            var skillId = _skillIds[i];
            if (!string.IsNullOrEmpty(_aimSkillId))
            {
                return;
            }

            if (IndicatorSkills.ContainsKey(skillId))
            {
                BeginAim(skillId, fromBar: true);
            }
            else
            {
                CastSkill(skillId);
            }

            break;
        }
    }

    private void TryContextMenuOpen()
    {
        var mouse = Mouse.current;
        if (mouse == null || !mouse.rightButton.wasPressedThisFrame)
        {
            return;
        }

        var cam = Camera.main;
        if (cam == null || _world == null)
        {
            return;
        }

        var screen = mouse.position.ReadValue();
        var world = cam.ScreenToWorldPoint(screen);
        var id = _world.PickEntityNear(world.x, world.y, 0.9f);
        if (string.IsNullOrEmpty(id) || id == _world.SelfId)
        {
            _showCtxMenu = false;
            return;
        }

        _ctxTargetId = id;
        _ctxTargetKind = InferKind(id);
        _ctxTargetLabel = id;
        // Prefer live label via temporary lock query without changing lock permanently —
        // use current lock info if it matches, else id.
        if (_world.TryGetTargetInfo(out var info) && info.Id == id && !string.IsNullOrEmpty(info.Label))
        {
            _ctxTargetLabel = info.Label;
            if (!string.IsNullOrEmpty(info.Kind))
            {
                _ctxTargetKind = info.Kind;
            }
        }
        else
        {
            // Peek label by locking briefly then restoring previous lock
            var prev = _world.LockTargetId;
            _world.SetLockTarget(id);
            if (_world.TryGetTargetInfo(out var peek) && !string.IsNullOrEmpty(peek.Label))
            {
                _ctxTargetLabel = peek.Label;
                if (!string.IsNullOrEmpty(peek.Kind))
                {
                    _ctxTargetKind = peek.Kind;
                }
            }

            if (!string.IsNullOrEmpty(prev) && prev != id)
            {
                _world.SetLockTarget(prev);
            }
        }

        _ctxMenuScreen = new Vector2(screen.x, Screen.height - screen.y);
        _showCtxMenu = true;
        _status = "menu " + _ctxTargetLabel;
    }

    private static string InferKind(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return "monster";
        }

        if (id.StartsWith("monster") || id.Contains("monster_"))
        {
            return "monster";
        }

        return "player";
    }

    private void TryContextMenuClicks()
    {
        if (!_showCtxMenu)
        {
            return;
        }

        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
        {
            return;
        }

        var p = mouse.position.ReadValue();
        var gui = new Vector2(p.x, Screen.height - p.y);
        var items = BuildCtxMenuItems();
        var menu = CtxMenuRect(items.Length);
        if (!menu.Contains(gui))
        {
            _showCtxMenu = false;
            return;
        }

        for (var i = 0; i < items.Length; i++)
        {
            var row = new Rect(menu.x, menu.y + 4 + i * 24f, menu.width, 22f);
            if (!row.Contains(gui))
            {
                continue;
            }

            ApplyCtxMenuAction(items[i]);
            _showCtxMenu = false;
            return;
        }
    }

    private string[] BuildCtxMenuItems()
    {
        if (_ctxTargetKind == "player")
        {
            return new[] { "Target", "Attack", "Inspect", "Whisper", "Invite party" };
        }

        return new[] { "Target", "Attack", "Inspect" };
    }

    private Rect CtxMenuRect(int itemCount)
    {
        var w = 140f;
        var h = 8f + itemCount * 24f;
        var x = Mathf.Clamp(_ctxMenuScreen.x, 4f, Screen.width - w - 4f);
        var y = Mathf.Clamp(_ctxMenuScreen.y, 4f, Screen.height - h - 4f);
        return new Rect(x, y, w, h);
    }

    private void ApplyCtxMenuAction(string action)
    {
        if (string.IsNullOrEmpty(_ctxTargetId) || _world == null)
        {
            return;
        }

        if (action == "Target")
        {
            _world.SetLockTarget(_ctxTargetId);
            _input?.SetTarget(_ctxTargetId);
            _status = "lock-on " + _ctxTargetId;
            return;
        }

        if (action == "Attack")
        {
            _world.SetLockTarget(_ctxTargetId);
            _input?.SetTarget(_ctxTargetId);
            _input?.Cast("auto_attack");
            _status = "AA → " + _ctxTargetId;
            return;
        }

        if (action == "Inspect")
        {
            _world.SetLockTarget(_ctxTargetId);
            _input?.SetTarget(_ctxTargetId);
            _input?.RequestInspect(_ctxTargetId);
            _status = "inspect req " + _ctxTargetId;
            return;
        }

        if (action == "Whisper")
        {
            _whisperTargetName = string.IsNullOrEmpty(_ctxTargetLabel) ? _ctxTargetId : _ctxTargetLabel;
            _chatTab = "world";
            _chatFocused = true;
            _chatJustFocused = true;
            _status = "whisper target " + _whisperTargetName;
            return;
        }

        if (action == "Invite party")
        {
            _input?.RequestPartyInvite(_ctxTargetId);
            _status = "party invite " + _ctxTargetId;
        }
    }

    private void CycleSpirit()
    {
        if (_input == null)
        {
            return;
        }

        _spiritIndex += 1;
        if (_spiritIndex >= _spirits.Length)
        {
            _spiritIndex = -1;
            _spirit = "none";
            _input.EquipSpirit(null);
            RefreshWeaponMeta();
            _status = "spirit unequipped";
            return;
        }

        _spirit = _spirits[_spiritIndex];
        _input.EquipSpirit(_spirit);
        _status = "spirit " + _spirit;
    }

    private void RefreshWeaponMeta()
    {
        _weaponRange = _weapon.Contains("bow") ? 5f
            : _weapon.Contains("staff") ? 4.5f
            : _weapon.Contains("gun") ? 4f
            : _weapon.Contains("dagger") ? 1.2f
            : 1.5f;
        if (_spirit == "none" || string.IsNullOrEmpty(_spirit))
        {
            _element = _weapon.Contains("staff") ? "holy"
                : _weapon.Contains("bow") ? "water"
                : _weapon.Contains("gun") ? "fire"
                : _weapon.Contains("dagger") ? "wind"
                : "earth";
        }
    }

    private void TryEquipKey(Keyboard kb)
    {
        var keys = new[] { kb.zKey, kb.xKey, kb.cKey, kb.vKey, kb.bKey, kb.nKey };
        for (var i = 0; i < keys.Length && i < _weapons.Length; i++)
        {
            if (!keys[i].wasPressedThisFrame)
            {
                continue;
            }

            _weapon = _weapons[i];
            _input.Equip(_weapon);
            RefreshWeaponMeta();
            _status = "equip " + _weapon;
            return;
        }
    }

    private void ApplyInventoryFromJson(string json)
    {
        var invIdx = json.IndexOf("\"inventory\"");
        if (invIdx < 0)
        {
            return;
        }

        for (var i = 0; i < _invSlots.Length; i++)
        {
            _invSlots[i] = null;
            _invQty[i] = 0;
        }

        var part = json.Substring(invIdx);
        var cursor = 0;
        var slot = 0;
        while (slot < 20 && cursor < part.Length)
        {
            var idToken = part.IndexOf("\"itemId\":", cursor);
            if (idToken < 0)
            {
                break;
            }

            var slice = part.Substring(idToken, Mathf.Min(80, part.Length - idToken));
            string itemId = null;
            if (!slice.Contains("\"itemId\":null"))
            {
                itemId = JsonUtil.ExtractString(slice, "itemId");
            }

            JsonUtil.TryInt(slice, "quantity", out var qty);
            JsonUtil.TryInt(slice, "slotIndex", out var si);
            var index = si >= 0 && si < 20 ? si : slot;
            _invSlots[index] = string.IsNullOrEmpty(itemId) ? null : itemId;
            _invQty[index] = qty;
            cursor = idToken + 9;
            slot += 1;
        }
    }

    private void DrawInventory()
    {
        if (!_showInventory)
        {
            return;
        }

        const float slotW = 52f;
        const float slotH = 40f;
        var startX = Screen.width - 20f - 4 * (slotW + 4);
        var startY = 120f;
        GUI.Label(new Rect(startX, startY - 18, 220, 18), "Inventory (B) — click equip");
        for (var i = 0; i < 20; i++)
        {
            var col = i % 4;
            var row = i / 4;
            var rect = new Rect(startX + col * (slotW + 4), startY + row * (slotH + 4), slotW, slotH);
            var id = _invSlots[i];
            GUI.color = InventoryColor(id);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            if (!string.IsNullOrEmpty(id))
            {
                var label = ShortInv(id);
                if (_invQty[i] > 1)
                {
                    label += " x" + _invQty[i];
                }

                GUI.Label(new Rect(rect.x + 2, rect.y + 2, rect.width - 4, rect.height - 4), label);
            }
        }
    }

    private void TryInventoryClicks()
    {
        if (!_showInventory)
        {
            return;
        }

        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame || _input == null)
        {
            return;
        }

        var p = mouse.position.ReadValue();
        var guiY = Screen.height - p.y;
        const float slotW = 52f;
        const float slotH = 40f;
        var startX = Screen.width - 20f - 4 * (slotW + 4);
        var startY = 120f;
        for (var i = 0; i < 20; i++)
        {
            var col = i % 4;
            var row = i / 4;
            var rect = new Rect(startX + col * (slotW + 4), startY + row * (slotH + 4), slotW, slotH);
            if (!rect.Contains(new Vector2(p.x, guiY)))
            {
                continue;
            }

            var id = _invSlots[i];
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            var kind = id.StartsWith("spirit_") ? "spirit"
                : id.StartsWith("char_") ? "character"
                : id.StartsWith("item_") ? "item"
                : "equipment";
            _itemTooltip = id + "\nkind: " + kind + "\nqty: " + _invQty[i];

            if (id.StartsWith("spirit_"))
            {
                _input.EquipSpirit(id);
                _spirit = id;
                _status = "equip spirit " + id;
            }
            else if (id.Contains("sword") || id.Contains("dagger") || id.Contains("staff") || id.Contains("bow") || id.Contains("gun"))
            {
                _input.EquipWeapon(id);
                _weapon = id;
                RefreshWeaponMeta();
                _status = "equip " + id;
            }
            else
            {
                _status = "item " + id;
            }

            return;
        }
    }

    private static Color InventoryColor(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return new Color(0.15f, 0.15f, 0.18f, 0.85f);
        }
        if (id.StartsWith("spirit_"))
        {
            return new Color(0.2f, 0.65f, 0.75f, 0.9f);
        }
        if (id.StartsWith("char_"))
        {
            return new Color(0.55f, 0.3f, 0.7f, 0.9f);
        }
        if (id.StartsWith("item_"))
        {
            return new Color(0.4f, 0.4f, 0.4f, 0.9f);
        }

        return new Color(0.75f, 0.45f, 0.2f, 0.9f);
    }

    private static string ShortInv(string id)
    {
        if (id.StartsWith("spirit_"))
        {
            return id.Substring(7);
        }
        if (id.StartsWith("char_"))
        {
            return id.Substring(5);
        }
        if (id.StartsWith("item_"))
        {
            return id.Substring(5);
        }

        var u = id.IndexOf('_');
        return u > 0 ? id.Substring(0, u) : id;
    }

    private void DrawHud()
    {
        var box = new GUIStyle(GUI.skin.box)
        {
            fontSize = 14,
            alignment = TextAnchor.UpperLeft,
            wordWrap = true,
            padding = new RectOffset(10, 10, 10, 10),
        };
        GUI.Box(new Rect(10, 10, Screen.width - 20, 100),
            "gAAAcha  |  " + _status + "\n" +
            "HP " + _hp + "/" + _maxHp + "  MP " + _mp + "/" + _maxMp +
            "  spd " + (_moveSpeed * _moveSpeedMult).ToString("0.0") +
            "  " + _weapon + " r" + _weaponRange + "  spirit " + _spirit + "\n" +
            "Pity " + _pity + "/" + _hardPity + "  Lock: " + _world.LockTargetId +
            "  Tab·Space AA·2/E/6/8 aim-cast·Esc cancel aim·F inspect·Enter chat·B inv",
            box);

        DrawResourceBar(new Rect(20, Screen.height - 54, 180, 12), (float)_hp / _maxHp, Color.red, "HP");
        DrawResourceBar(new Rect(20, Screen.height - 36, 180, 12), (float)_mp / _maxMp, new Color(0.3f, 0.55f, 1f), "MP");
    }

    private void DrawTargetFrame()
    {
        if (_world == null || !_world.TryGetTargetInfo(out var info) || string.IsNullOrEmpty(info.Id))
        {
            return;
        }

        var frame = new Rect(Screen.width - 280f, 110f, 260f, 118f);
        var border = new Color(0.45f, 0.45f, 0.5f, 0.95f);
        if (!string.IsNullOrEmpty(info.ThreatTopId) && info.ThreatTopId == _world.SelfId)
        {
            border = new Color(0.95f, 0.25f, 0.2f, 1f);
        }
        else if (info.ThreatSelf >= 35f)
        {
            border = new Color(0.95f, 0.85f, 0.2f, 1f);
        }

        GUI.color = border;
        GUI.DrawTexture(frame, Texture2D.whiteTexture);
        GUI.color = new Color(0.08f, 0.09f, 0.12f, 0.92f);
        GUI.DrawTexture(new Rect(frame.x + 2, frame.y + 2, frame.width - 4, frame.height - 4), Texture2D.whiteTexture);
        GUI.color = Color.white;

        var portrait = new Rect(frame.x + 10, frame.y + 12, 48, 48);
        GUI.color = info.Kind == "player"
            ? new Color(0.15f, 0.85f, 1f, 1f)
            : new Color(0.2f, 1f, 0.25f, 1f);
        GUI.DrawTexture(portrait, Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUI.Label(new Rect(frame.x + 68, frame.y + 8, 180, 20),
            string.IsNullOrEmpty(info.Label) ? info.Id : info.Label);

        var hpRatio = info.MaxHp <= 0 ? 0f : (float)info.Hp / info.MaxHp;
        var mpRatio = info.MaxMp <= 0 ? 0f : (float)info.Mp / info.MaxMp;
        DrawResourceBar(new Rect(frame.x + 68, frame.y + 32, 170, 10), hpRatio, Color.red, "");
        GUI.Label(new Rect(frame.x + 68, frame.y + 30, 170, 14), info.Hp + "/" + info.MaxHp);
        DrawResourceBar(new Rect(frame.x + 68, frame.y + 50, 170, 10), mpRatio, new Color(0.3f, 0.55f, 1f), "");
        GUI.Label(new Rect(frame.x + 68, frame.y + 48, 170, 14), info.Mp + "/" + info.MaxMp);

        if (info.Kind == "monster")
        {
            var threat = Mathf.Clamp01(info.ThreatSelf / 100f);
            DrawResourceBar(new Rect(frame.x + 10, frame.y + 70, 240, 8), threat, new Color(1f, 0.45f, 0.15f), "");
            GUI.Label(new Rect(frame.x + 10, frame.y + 68, 240, 14),
                "Aggro " + info.ThreatSelf.ToString("0") + "%");
        }

        if (info.StatusKinds != null && info.StatusKinds.Length > 0)
        {
            var chipX = frame.x + 10f;
            var chipY = frame.y + 90f;
            for (var i = 0; i < info.StatusKinds.Length && i < 6; i++)
            {
                var chip = new Rect(chipX, chipY, 38f, 18f);
                GUI.color = new Color(0.25f, 0.3f, 0.4f, 0.9f);
                GUI.DrawTexture(chip, Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(chip, ShortName(info.StatusKinds[i]));
                chipX += 42f;
            }
        }
    }

    private void DrawContextMenu()
    {
        if (!_showCtxMenu)
        {
            return;
        }

        var items = BuildCtxMenuItems();
        var menu = CtxMenuRect(items.Length);
        GUI.color = new Color(0.12f, 0.13f, 0.16f, 0.95f);
        GUI.DrawTexture(menu, Texture2D.whiteTexture);
        GUI.color = Color.white;
        for (var i = 0; i < items.Length; i++)
        {
            var row = new Rect(menu.x + 4, menu.y + 4 + i * 24f, menu.width - 8, 22f);
            if (GUI.Button(row, items[i]))
            {
                ApplyCtxMenuAction(items[i]);
                _showCtxMenu = false;
            }
        }
    }

    private void DrawInspectSheet()
    {
        if (!_showInspect)
        {
            return;
        }

        var sheet = new Rect(40f, 120f, 420f, 280f);
        GUI.color = new Color(0.1f, 0.11f, 0.14f, 0.94f);
        GUI.DrawTexture(sheet, Texture2D.whiteTexture);
        GUI.color = Color.white;

        var model = new Rect(sheet.x + 16, sheet.y + 40, 100, 140);
        GUI.color = _inspectKind == "player"
            ? new Color(0.15f, 0.85f, 1f, 1f)
            : new Color(0.2f, 1f, 0.25f, 1f);
        GUI.DrawTexture(model, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(model.x, model.y + model.height + 4, model.width, 20), "model");

        GUI.Label(new Rect(sheet.x + 130, sheet.y + 12, 260, 22),
            _inspectName + " (" + _inspectKind + ")");
        GUI.Label(new Rect(sheet.x + 130, sheet.y + 40, 270, 70), _inspectStats);

        if (_inspectKind == "player")
        {
            var weaponRect = new Rect(sheet.x + 130, sheet.y + 120, 200, 22);
            var spiritRect = new Rect(sheet.x + 130, sheet.y + 146, 200, 22);
            if (GUI.Button(weaponRect, "Weapon: " + (string.IsNullOrEmpty(_inspectWeaponId) ? "-" : _inspectWeaponId)))
            {
                _itemTooltip = string.IsNullOrEmpty(_inspectWeaponId)
                    ? "no weapon"
                    : _inspectWeaponId + "\nkind: equipment";
            }

            if (GUI.Button(spiritRect, "Spirit: " + (string.IsNullOrEmpty(_inspectSpiritId) ? "-" : _inspectSpiritId)))
            {
                _itemTooltip = string.IsNullOrEmpty(_inspectSpiritId)
                    ? "no spirit"
                    : _inspectSpiritId + "\nkind: spirit";
            }
        }
        else
        {
            GUI.Label(new Rect(sheet.x + 130, sheet.y + 120, 270, 40),
                "Type: " + (string.IsNullOrEmpty(_inspectMonsterType) ? "-" : _inspectMonsterType) +
                "\nResists: " + (string.IsNullOrEmpty(_inspectResists) ? "-" : _inspectResists));
        }

        GUI.Label(new Rect(sheet.x + 130, sheet.y + 180, 270, 40),
            "Buffs: " + (string.IsNullOrEmpty(_inspectStatuses) ? "-" : _inspectStatuses));

        if (GUI.Button(new Rect(sheet.x + sheet.width - 90, sheet.y + sheet.height - 36, 70, 26), "Close"))
        {
            _showInspect = false;
        }
    }

    private void DrawChatBox()
    {
        var box = new Rect(Screen.width - 340f, Screen.height - 210f, 320f, 190f);
        GUI.color = new Color(0.08f, 0.09f, 0.12f, 0.88f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = Color.white;

        var tabW = box.width / ChatTabs.Length;
        for (var i = 0; i < ChatTabs.Length; i++)
        {
            var tab = ChatTabs[i];
            var rect = new Rect(box.x + i * tabW, box.y, tabW, 22f);
            var label = tab == _chatTab ? "[" + tab + "]" : tab;
            if (GUI.Button(rect, label))
            {
                _chatTab = tab;
            }
        }

        if (!_chatLogs.TryGetValue(_chatTab, out var log))
        {
            log = new List<string>();
        }

        var start = Mathf.Max(0, log.Count - 8);
        var y = box.y + 28f;
        for (var i = start; i < log.Count; i++)
        {
            GUI.Label(new Rect(box.x + 8, y, box.width - 16, 16), log[i]);
            y += 16f;
        }

        if (_chatFocused)
        {
            var hint = string.IsNullOrEmpty(_whisperTargetName)
                ? _chatTab
                : "w → " + _whisperTargetName;
            GUI.Label(new Rect(box.x + 8, box.y + box.height - 48, box.width - 16, 16), hint);
            var e = Event.current;
            var prev = _chatInput;
            _chatInput = GUI.TextField(new Rect(box.x + 8, box.y + box.height - 28, box.width - 16, 22), _chatInput ?? "");
            if (e != null && e.type == EventType.KeyDown &&
                (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                if (!string.IsNullOrEmpty(_chatInput))
                {
                    SendChatLine();
                }
                else
                {
                    _chatFocused = false;
                }

                e.Use();
            }
            else if (_chatInput != prev)
            {
                // keep focus while typing
            }
        }
        else
        {
            GUI.Label(new Rect(box.x + 8, box.y + box.height - 28, box.width - 16, 22),
                "Enter to chat");
        }
    }

    private void DrawToast()
    {
        if (string.IsNullOrEmpty(_comingSoonToast) || Time.time > _comingSoonUntil)
        {
            return;
        }

        var rect = new Rect(Screen.width * 0.5f - 140f, Screen.height * 0.35f, 280f, 40f);
        GUI.color = new Color(0.15f, 0.12f, 0.08f, 0.92f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(rect, _comingSoonToast);
    }

    private void DrawItemTooltip()
    {
        if (string.IsNullOrEmpty(_itemTooltip))
        {
            return;
        }

        var mouse = Mouse.current;
        var pos = mouse != null ? mouse.position.ReadValue() : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        var gui = new Vector2(pos.x, Screen.height - pos.y);
        var rect = new Rect(gui.x + 12f, gui.y + 12f, 180f, 54f);
        GUI.color = new Color(0.1f, 0.1f, 0.12f, 0.95f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(rect.x + 6, rect.y + 4, rect.width - 12, rect.height - 8), _itemTooltip);
    }

    private void DrawBuffRow()
    {
        var x = 220f;
        var y = Screen.height - 52f;
        for (var i = 0; i < _buffs.Count; i++)
        {
            var b = _buffs[i];
            var rem = Mathf.Max(0f, b.UntilLocal - Time.realtimeSinceStartup);
            if (rem <= 0f)
            {
                continue;
            }

            var rect = new Rect(x + i * 86f, y, 80f, 28f);
            GUI.color = new Color(0.2f, 0.25f, 0.35f, 0.85f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(rect, b.Kind + "\n" + rem.ToString("0.0") + "s");
        }
    }

    private void DrawSkillBar()
    {
        var slotW = 56f;
        var slotH = 56f;
        var startX = 20f;
        var y = Screen.height - 120f;
        for (var i = 0; i < _skillIds.Length; i++)
        {
            var id = _skillIds[i];
            var row = i / 9;
            var col = i % 9;
            var rect = new Rect(startX + col * (slotW + 6), y - row * (slotH + 8), slotW, slotH);
            var readyAt = _readyAtLocal.TryGetValue(id, out var ra) ? ra : 0f;
            var cdMs = _cooldownMs.TryGetValue(id, out var c) ? c : 1000f;
            var rem = Mathf.Max(0f, readyAt - Time.realtimeSinceStartup);
            var fill = cdMs <= 0 ? 0f : Mathf.Clamp01(rem / (cdMs / 1000f));

            GUI.color = SkillColor(id);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            if (fill > 0f)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, rect.height * fill), Texture2D.whiteTexture);
            }

            GUI.color = Color.white;
            var label = ShortName(id);
            GUI.Label(new Rect(rect.x + 2, rect.y + 2, rect.width - 4, rect.height - 4),
                label + (rem > 0 ? "\n" + rem.ToString("0.0") : ""));
        }
    }

    private static Color SkillColor(string id)
    {
        if (id == "auto_attack")
        {
            return new Color(0.75f, 0.75f, 0.35f, 0.9f);
        }
        if (NoTargetSkills.Contains(id))
        {
            return new Color(0.35f, 0.55f, 0.85f, 0.9f);
        }
        if (IndicatorSkills.ContainsKey(id))
        {
            return new Color(0.35f, 0.8f, 0.55f, 0.9f);
        }
        if (id == "dash" || id == "shove" || id == "pull")
        {
            return new Color(0.45f, 0.75f, 0.45f, 0.9f);
        }

        return new Color(0.75f, 0.4f, 0.35f, 0.9f);
    }

    private static string ShortName(string id)
    {
        if (id == "auto_attack")
        {
            return "AA";
        }
        if (id.Length <= 6)
        {
            return id;
        }

        return id.Substring(0, 6);
    }

    private static void DrawResourceBar(Rect rect, float ratio, Color fill, string label)
    {
        GUI.color = new Color(0f, 0f, 0f, 0.7f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = fill;
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(ratio), rect.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
        if (!string.IsNullOrEmpty(label))
        {
            GUI.Label(new Rect(rect.x, rect.y - 16, rect.width, 16), label);
        }
    }

    private async void OnDestroy()
    {
        if (_net == null)
        {
            return;
        }

        await _net.DisconnectAsync();
    }
}

public static class NetworkBootstrapLoader
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureBootstrap()
    {
        if (Object.FindAnyObjectByType<NetworkBootstrap>() != null)
        {
            return;
        }

        new GameObject("NetworkBootstrap").AddComponent<NetworkBootstrap>();
    }
}
