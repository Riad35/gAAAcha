using System;
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
        "auto_attack", "shot", "shockwave", "dash", "rally", "hook_shot", "mend", "decoy",
    };

    private static readonly string[] DebugClassIds =
    {
        "adventurer", "fighter", "marksman", "mage", "rogue",
    };

    private static readonly string[] DebugClassLabels =
    {
        "Adv", "Fgt", "Arc", "Mage", "Rog",
    };

    private static readonly HashSet<string> NoTargetSkills = new HashSet<string>
    {
        "dash", "decoy", "rest", "powerup", "war_cry", "iron_stance", "haste", "ward", "power_chant",
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
        { "cleave", new AimDef { Kind = AimKind.Ground, Range = 2.2f, AoeRadius = 2.4f } },
        { "arrow_rain", new AimDef { Kind = AimKind.Ground, Range = 5f, AoeRadius = 2.8f } },
        { "arcane_nova", new AimDef { Kind = AimKind.Ground, Range = 4f, AoeRadius = 2.6f } },
        { "thunderstorm", new AimDef { Kind = AimKind.Ground, Range = 5f, AoeRadius = 3.2f } },
        { "explosion", new AimDef { Kind = AimKind.Ground, Range = 4.5f, AoeRadius = 3.4f } },
        { "knife_fan", new AimDef { Kind = AimKind.Cone, Range = 3.2f, AngleDeg = 70f } },
    };

    /// <summary>Client skill cast ranges (must match server skills.json; AA uses weapon).</summary>
    private static readonly Dictionary<string, float> SkillRanges = new Dictionary<string, float>
    {
        { "auto_attack", 0f },
        { "auto_attack_off", 0f },
        { "shot", 5f },
        { "shockwave", 3.5f },
        { "dash", 3f },
        { "rally", 0f },
        { "hook_shot", 4.5f },
        { "mend", 5f },
        { "decoy", 0f },
        { "rest", 0f },
        { "powerup", 0f },
        { "cleave", 2.2f },
        { "arrow_rain", 5f },
        { "arcane_nova", 4f },
        { "thunderstorm", 5f },
        { "explosion", 4.5f },
        { "knife_fan", 3.2f },
        // Legacy / other classes (still castable if unlocked)
        { "slash", 1.5f },
        { "stun_bolt", 4f },
        { "ember_dot", 3.5f },
        { "war_cry", 0f },
        { "shove", 1.5f },
        { "pull", 4.5f },
        { "blind_dust", 3.5f },
        { "iron_stance", 0f },
        { "power_chant", 0f },
        { "haste", 0f },
        { "barrier", 0f },
        { "ward", 0f },
        { "elemental_focus", 0f },
        { "group_chant", 0f },
        { "ally_mend", 5f },
        { "target_burst", 4f },
    };

    private static readonly string[] ChatTabs = { "world", "server", "guild", "party", "map" };

    private NetClient _net;
    private InputSender _input;
    private PredictionReconciler _prediction = new PredictionReconciler();
    private string _lastCastRequestId = "";
    private string _lastCastSkillId = "";
    private GrayBoxWorld _world;
    private VirtualJoystick _joystick;
    private float _x = 3f;
    private float _y = 10f;
    private float _moveSpeed = 3.6f;
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
    private float _weapon2Range = 1.5f;
    private string _element = "earth";
    private float _moveLockUntil;
    private int _hp = 100;
    private int _maxHp = 100;
    private int _mp = 50;
    private int _maxMp = 50;
    private int _pity;
    private int _hardPity = 80;
    private int _softPityStart = 50;
    private float _nextSsr = 0.02f;
    private float _baseSsr = 0.02f;
    private float _baseSr = 0.1f;
    private int _pullCostDust = 10;
    private int _tenPullCostDust = 90;
    private string _bannerName = "Starter Banner";
    private string _lastDrop = "-";
    private string _gachaSummary = "";
    private bool _showGacha;
    private string _skin = "none";
    private bool _showDeath;
    private bool _showClassChange;
    private string _classChangeTitle = "";
    private string _classChangeBody = "";
    private int _tutorialStep = -1;
    private static readonly string[] TutorialTips =
    {
        "Move: WASD or RMB-path the ground. Minimap wheel zooms; RMB on minimap/M routes. MMB-drag yaw.",
        "Tab cycles living foes. Shift+Tab clears the lock. Click a player for an ally (blue).",
        "Space is primary Auto Attack (sticky). Z is offhand AA (sticky). Rest sits and heals.",
        "I is inventory: drag items, paperdoll slots, RMB equip/unequip. Class toggle is there.",
        "J is the quest log. M opens the full map (minimap sits top-right). Sister Mira in town starts the class path.",
        "Homestone is in your bag, or talk to the standing stone in town.",
        "G opens the banner (pity is there). Class cards unlock at level 10.",
    };
    private readonly Dictionary<string, int> _unlockLevels = new Dictionary<string, int>();
    private string _status = "starting…";
    private string _guestToken = "";
    private readonly ConcurrentQueue<string> _inbox = new ConcurrentQueue<string>();
    private readonly Dictionary<string, float> _readyAtLocal = new Dictionary<string, float>();
    private readonly Dictionary<string, float> _cooldownMs = new Dictionary<string, float>();
    private readonly Dictionary<string, SkillHudInfo> _skillHud = new Dictionary<string, SkillHudInfo>();
    private string _castSkillId = "";
    private string _castLabel = "";
    private float _castUntil;
    private float _castCommitUntil;
    private float _castDur = 0.35f;
    /// <summary>Windup + hit. Remainder is recovery and can be cancelled by movement.</summary>
    private const float CastHitFrac = 0.5f;
    private string _hoverSkillId = "";
    private readonly List<BuffView> _buffs = new List<BuffView>();
    private double _serverSkewMs;
    private bool _hasServerCond;
    private bool _canMove = true;
    private bool _canAct = true;
    private float _pingAcc;
    private int _moveAckSeq;
    private string[] _skillIds = ClassSkills;
    private bool _showInventory = false;
    private const int InvSize = 144;
    private const int InvCols = 12;
    private readonly string[] _invSlots = new string[InvSize];
    private readonly int[] _invQty = new int[InvSize];
    private Vector2 _invPanelPos = new Vector2(-1f, -1f);
    private bool _invPosReady;
    private bool _invDragging;
    private Vector2 _invDragOffset;
    private bool _itemDragging;
    private int _itemDragFrom = -1;
    private string _itemDragEquip = "";
    private string _itemDragId = "";
    private int _itemDragQty = 1;
    private bool _stickyAutoAttack;
    private string _stickyAttackSkill = "auto_attack";
    private int _debugClassIndex;
    private Vector2 _eqPanelPos = new Vector2(-1f, -1f);
    private bool _eqPosReady;
    private bool _eqDragging;
    private Vector2 _eqDragOffset;
    private string _hoverItemTip = "";
    private string _armor = "none";
    private string _helm = "none";
    private string _boots = "none";
    private string _gloves = "none";
    private string _accessory = "none";
    private string _amulet = "none";
    private string _ring1 = "none";
    private string _ring2 = "none";
    private string _subclass = "";
    private readonly Dictionary<string, int> _enhanceLevels = new Dictionary<string, int>();
    private bool _transformed;
    private string _classPickPending = "";
    private string _playerLabel = "You";
    private int _resPresetIndex;
    private static readonly Vector2Int[] ResPresets =
    {
        new Vector2Int(1280, 720),
        new Vector2Int(1600, 900),
        new Vector2Int(1920, 1080),
        new Vector2Int(0, 0), // native
    };

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
    private bool _showHelp;
    private bool _showFullMap;

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

    // Party / guild
    private string _partyInviteId = "";
    private string _partyInviteFrom = "";
    private float _partyInviteUntil;
    private string _partyId = "";
    private string _partyMembersLine = "";
    private readonly List<string> _partyMemberIds = new List<string>();
    private readonly List<string> _partyMemberNames = new List<string>();
    private readonly List<int> _partyMemberHp = new List<int>();
    private readonly List<int> _partyMemberMaxHp = new List<int>();
    private readonly List<int> _partyMemberMp = new List<int>();
    private readonly List<int> _partyMemberMaxMp = new List<int>();
    private readonly List<int> _partyMemberLevel = new List<int>();
    private readonly List<string> _partyMemberClass = new List<string>();
    private string _guildName = "Ashen Legion";
    private string _guildInviteId = "";
    private string _guildInviteFrom = "";
    private string _guildInviteName = "";
    private float _guildInviteUntil;
    private string _tradeInviteId = "";
    private string _tradeInviteFrom = "";
    private float _tradeInviteUntil;
    private string _tradeId = "";
    private int _tradeMyGold;
    private int _tradeTheirGold;
    private bool _tradeMyConfirm;
    private bool _tradeTheirConfirm;
    private string _tradeTheirName = "";
    private string _tradeOfferSummary = "";
    private bool _showFriends;
    private bool _showSettings;
    private bool _musicOn = true;
    private readonly List<string> _friendNames = new List<string>();
    private readonly List<string> _friendTokens = new List<string>();
    private readonly List<bool> _friendOnline = new List<bool>();
    private readonly List<string> _friendPlayerIds = new List<string>();
    private string _guildCreateDraft = "My Guild";
    private bool _showNameplates = true;
    private float _uiScale = 1f;
    private int _skillPoints;
    private bool _showSkillTree;
    private readonly List<string> _unlockableSkills = new List<string>();
    private readonly List<string> _unlockedSkills = new List<string>();
    private bool _showAuction;
    private readonly List<string> _auctionIds = new List<string>();
    private readonly List<string> _auctionLabels = new List<string>();
    private long _instanceExpiresAt;
    private int _bossPhase;
    private readonly List<int> _tradeOfferSlots = new List<int>();
    private readonly List<int> _tradeOfferQtys = new List<int>();
    private string _auctionSellItem = "item_dust";

    // Hub loop
    private int _gold = 100;
    private int _level = 1;
    private int _xp;
    private int _xpToLevel = 75;
    private bool _charNameSet = true;
    private string _charNameDraft = "Adventurer";
    private bool _showLogin;
    private string _loginUser = "";
    private string _loginPass = "";
    private string _authStatus = "";
    // Gate: 0 login, 1 server, 2 chars, 3 in-world
    private int _gatePhase;
    private readonly List<string> _serverIds = new List<string>();
    private readonly List<string> _serverNames = new List<string>();
    private string _selectedServerId = "local";
    private readonly bool[] _charEmpty = new bool[8];
    private readonly string[] _charNames = new string[8];
    private readonly string[] _charClasses = new string[8];
    private readonly int[] _charLevels = new int[8];
    private int _selectedSlot;
    private bool _confirmDelete;
    private string _classId = "adventurer";
    private string _weapon2 = "none";
    private float _weaponSwapUntil;
    private int _towerFloor;
    private bool _inWorld = true;
    private bool _showQuestLog;
    private string _questLogText = "";
    private string _questTrackerLine = "";
    private string _loadMapId = "";
    private string _loadMapTitle = "";
    private float _loadUntil;
    private Sprite _loadArt;
    private bool _showInteract;
    private string _interactKind = "";
    private string _interactLine = "";
    private string _interactTargetId = "";
    private string _interactNpcName = "";
    private string _pendingTalkId = "";
    private bool _showNpcDialog;
    private bool _showShopBuy;
    private bool _showShopSell;
    private bool _showClassService;
    private bool _showEnhance;
    private const float NpcTalkRange = 2.2f;
    private string _shopId = "";
    private readonly List<string> _shopItemIds = new List<string>();
    private readonly List<int> _shopBuyPrices = new List<int>();
    private readonly List<string> _questIds = new List<string>();
    private readonly List<string> _questStates = new List<string>();
    private readonly List<string> _questNames = new List<string>();
    private readonly List<string> _portalIds = new List<string>();
    private readonly List<float> _portalXs = new List<float>();
    private readonly List<float> _portalYs = new List<float>();

    // Indicator-cast aim (empty = idle)
    private string _aimSkillId = "";
    private bool _aimFromBar;
    private int _aimStartFrame;
    private float _aimDx = 1f;
    private float _aimDy;
    private float _aimX;
    private float _aimY;

    private struct SkillHudInfo
    {
        public string Name;
        public int ManaCost;
        public int WeaponSlot;
        public float CastSec;
        public string TargetingType;
        public string Affects;
        public string AoeOrigin;
        public float Range;
        public float AoeRadius;
    }

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

        SeedDefaultSkillHud();
        SoundCatalog.Ensure();
        UiChrome.Ensure();

        for (var i = 0; i < ChatTabs.Length; i++)
        {
            _chatLogs[ChatTabs[i]] = new List<string>();
        }

        _status = "Awake";
        _gatePhase = 0;
        _inWorld = false;
        _uiScale = Mathf.Clamp(PlayerPrefs.GetFloat("gaaacha_ui_scale", 1f), 0.8f, 1.4f);
        _resPresetIndex = PlayerPrefs.GetInt("gaaacha_res_preset", 2);
        _musicOn = PlayerPrefs.GetInt("gaaacha_music", 1) == 1;
        SoundCatalog.SetMusicEnabled(_musicOn);
        for (var i = 0; i < 8; i++)
        {
            _charEmpty[i] = true;
            _charNames[i] = "";
            _charClasses[i] = "";
            _charLevels[i] = 1;
        }
    }

    private async void Start()
    {
        ApplyResolutionPreset(_resPresetIndex, savePrefs: false);
        _status = "connecting…";
        _net = new NetClient();
        _net.MessageReceived += msg => _inbox.Enqueue(msg);
        _input = new InputSender(_net);

        try
        {
            await _net.ConnectAsync(NetClient.DefaultUrl);
            await _net.SendRawAsync(
                "{\"type\":\"request_hello\",\"guestToken\":\"" + _guestToken + "\"}");
            _status = "CONNECTED — choose login or guest";
            GameLog.Info(GameLog.Channel.Net, "connected  url=" + NetClient.DefaultUrl);
            _gatePhase = 0;
            _inWorld = false;
        }
        catch (System.Exception ex)
        {
            _status = "CONNECT FAILED: " + ex.Message;
            GameLog.Error(GameLog.Channel.Net, "connect failed  err=" + ex.Message);
        }
    }

    private bool _clickMoveActive;
    private float _clickMoveX;
    private float _clickMoveY;
    private readonly List<Vector2> _route = new List<Vector2>(64);
    private int _routeI;
    private float _repathAt;
    private int _minimapRadius = 24;
    private Rect _minimapArea;
    private float _minimapCell = 5f;
    private Rect _fullMapArea;
    private float _fullMapCell = 4f;
    private readonly List<GrayBoxWorld.MinimapMark> _minimapMarks = new List<GrayBoxWorld.MinimapMark>(64);
    private string _pendingSkillId = "";
    private string _pendingSkillTarget = "";
    private float _pendingSkillRange;
    private float _pendingSkillUntil;
    private string _queuedSkillId = "";

    private bool IsStunned()
    {
        var now = Time.realtimeSinceStartup;
        for (var i = 0; i < _buffs.Count; i++)
        {
            if (_buffs[i].Kind == "stun" && _buffs[i].UntilLocal > now)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsCasting => Time.time < _castUntil && !string.IsNullOrEmpty(_castSkillId);

    private bool CanMoveCancelCast => IsCasting && Time.time >= _castCommitUntil;

    private bool IsActionLocked => _hp <= 0 || _showDeath || ActLockedNow || IsCasting;

    private bool MoveLockedNow =>
        _hasServerCond ? !_canMove : (Time.time < _moveLockUntil || IsStunned());

    private bool ActLockedNow =>
        _hasServerCond ? !_canAct : IsStunned();

    private void FlushQueuedSkill()
    {
        if (IsActionLocked || string.IsNullOrEmpty(_queuedSkillId))
        {
            return;
        }

        var id = _queuedSkillId;
        _queuedSkillId = "";
        CastSkill(id);
    }

    private void Update()
    {
        while (_inbox.TryDequeue(out var json))
        {
            HandlePacket(json);
        }

        TickHeartbeat();

        if (_gatePhase < 3 || !_inWorld)
        {
            return;
        }

        _joystick?.Tick();
        TickAiming();
        TickPendingSkillChase();
        TickStickyAutoAttack();
        FlushQueuedSkill();
        HandleContinuousMove();
        TryAutoPortal();
        HandleActionKeys();
        TrySkillBarClicks();
        TryWorldTargetClicks();
        TryWorldMoveClicks();
        TickMinimapZoom();
        TryContextMenuOpen();
        TryContextMenuClicks();
        TryChat();
    }

    private void OnGUI()
    {
        var scale = UiScaleSafe;
        var prevMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
        try
        {
            OnGuiScaled();
        }
        finally
        {
            GUI.matrix = prevMatrix;
        }
    }

    private float UiScaleSafe => Mathf.Clamp(_uiScale, 0.8f, 1.4f);
    private float GuiW => Screen.width / Mathf.Max(0.1f, UiScaleSafe);
    private float GuiH => Screen.height / Mathf.Max(0.1f, UiScaleSafe);

    private Vector2 ScreenToGui(Vector2 screenPx)
    {
        var s = Mathf.Max(0.1f, UiScaleSafe);
        return new Vector2(screenPx.x / s, (Screen.height - screenPx.y) / s);
    }

    private void PersistUiScale()
    {
        PlayerPrefs.SetFloat("gaaacha_ui_scale", UiScaleSafe);
        PlayerPrefs.Save();
    }

    private void OnGuiScaled()
    {
        UiChrome.ApplyGuiSkin();
        if (_gatePhase < 3 || !_inWorld)
        {
            DrawGate();
            return;
        }

        DrawHud();
        DrawMinimapHud();
        DrawInventory();
        DrawSkillBar();
        DrawCastBar();
        DrawBuffRow();
        DrawTargetFrame();
        DrawContextMenu();
        DrawInspectSheet();
        DrawChatBox();
        DrawPartyInvite();
        DrawGuildInvite();
        DrawTradeInvite();
        DrawTradePanel();
        DrawPartyPanel();
        DrawFriendsPanel();
        DrawSettingsPanel();
        DrawGachaPanel();
        DrawDeathPanel();
        DrawClassChangePanel();
        DrawHelpPanel();
        DrawTutorialTips();
        DrawSkillTreePanel();
        DrawAuctionPanel();
        DrawInstanceHud();
        DrawBossFrame();
        DrawLoginPanel();
        DrawCharCreate();
        DrawInteractPanel();
        DrawNpcDialog();
        DrawShopBuyPanel();
        DrawShopSellPanel();
        DrawClassServicePanel();
        DrawEnhancePanel();
        DrawQuestLog();
        DrawQuestTracker();
        DrawToast();
        DrawItemTooltip();
        _joystick?.Draw();
        if (_world != null)
        {
            _world.DrawOverlays();
        }

        DrawFullMapOverlay();
        DrawLoadingScreen();
    }

    private void HandlePacket(string json)
    {
        GameLog.Packet(json);
        _world.HandleMessage(json);

        if (json.Contains("\"type\":\"error\""))
        {
            var code = JsonUtil.ExtractString(json, "code");
            var msg = JsonUtil.ExtractString(json, "message");
            _status = string.IsNullOrEmpty(msg)
                ? ("error " + code)
                : (code + ": " + msg);
            if (!_inWorld || _gatePhase < 3)
            {
                _authStatus = string.IsNullOrEmpty(msg) ? code : msg;
                if (code == "db_offline")
                {
                    _authStatus = "No DATABASE_URL — continue as Guest. Login is optional.";
                }
            }

            // Snap only when a wall/entity/shove actually stopped us. too_fast is clamped
            // on the server — snapping here is what made walking feel laggy.
            if ((code == "blocked" || code == "blocked_entity" || code == "move_locked") &&
                !IsStaleMoveReject(json))
            {
                _x = _lastGoodX;
                _y = _lastGoodY;
                _world?.SetLocalPos(_x, _y, instant: false);
                if (code == "move_locked" && !_hasServerCond)
                {
                    _moveLockUntil = Time.time + 0.25f;
                }

                GameLog.WarnOnce(GameLog.Channel.Combat, "snap:" + code, "lerp-back  code=" + code);
            }
            else if (code == "too_fast" || code == "rate_limited")
            {
                var dx = _x - _lastGoodX;
                var dy = _y - _lastGoodY;
                if (dx * dx + dy * dy > 2.25f)
                {
                    _x = _lastGoodX;
                    _y = _lastGoodY;
                    _world?.SetLocalPos(_x, _y, instant: false);
                }

                GameLog.WarnOnce(GameLog.Channel.Combat, "snap:" + code, "keep-pred  code=" + code);
            }

            if (code == "no_offhand")
            {
                if (_stickyAttackSkill == "auto_attack_off")
                {
                    _stickyAutoAttack = false;
                }
            }

            if (code == "you_are_dead")
            {
                _showDeath = true;
            }

            if (code == "out_of_range" && _world != null &&
                !string.IsNullOrEmpty(_world.LockTargetId))
            {
                var lockId = _world.LockTargetId;
                var retrySkill = !string.IsNullOrEmpty(_pendingSkillId)
                    ? _pendingSkillId
                    : _lastCastSkillId;
                if (string.IsNullOrEmpty(retrySkill))
                {
                    retrySkill = "auto_attack";
                }

                GameLog.Info(GameLog.Channel.Combat, "out_of_range  retry=" + retrySkill + "  target=" + lockId);

                if (!HasPendingSkillChase())
                {
                    BeginPendingSkill(retrySkill, lockId, ResolveSkillRange(retrySkill));
                }
            }

            return;
        }

        if (json.Contains("\"type\":\"sync_state\""))
        {
            var you = JsonUtil.ExtractObject(json, "you");
            if (string.IsNullOrEmpty(you))
            {
                you = JsonUtil.SliceAround(json, "\"you\"", 0, 2400);
            }
            if (JsonUtil.TryInt(you, "hp", out var hp))
            {
                _hp = hp;
                _showDeath = hp <= 0;
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

            var pitySlice = JsonUtil.SliceAround(json, "\"pity\"", 0, 640);
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
                ApplyPityExtras(pitySlice);
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

            if (JsonUtil.TryInt(json, "gold", out var gold))
            {
                _gold = gold;
            }

            if (JsonUtil.TryInt(json, "level", out var level))
            {
                _level = level;
            }

            if (JsonUtil.TryInt(json, "xp", out var xp))
            {
                _xp = xp;
            }

            if (JsonUtil.TryInt(json, "xpToLevel", out var xpn))
            {
                _xpToLevel = xpn;
            }

            _charNameSet = !json.Contains("\"charNameSet\":false");
            var cid = JsonUtil.ExtractString(json, "classId");
            if (!string.IsNullOrEmpty(cid))
            {
                _classId = cid;
                _world?.SetClassLook(_classId);
            }

            var skinId = JsonUtil.ExtractString(json, "equippedSkinId");
            _skin = string.IsNullOrEmpty(skinId) || skinId == "null" ? "none" : skinId;

            if (JsonUtil.TryInt(json, "towerClearedFloor", out var tf))
            {
                _towerFloor = tf;
            }

            var w2 = JsonUtil.ExtractString(json, "equippedWeapon2Id");
            _weapon2 = string.IsNullOrEmpty(w2) || w2 == "null" ? "none" : w2;
            var ew = JsonUtil.ExtractString(json, "equippedWeaponId");
            if (!string.IsNullOrEmpty(ew) && ew != "null")
            {
                _weapon = ew;
            }

            ApplyGearIdsFromJson(json);
            _transformed = json.Contains("\"transformed\":true");
            RefreshWeaponMeta();
            var youSlice = JsonUtil.ExtractObject(json, "you");
            if (string.IsNullOrEmpty(youSlice))
            {
                youSlice = JsonUtil.SliceAround(json, "\"you\"", 0, 2400);
            }
            var nm = JsonUtil.ExtractString(youSlice, "name");
            if (string.IsNullOrEmpty(nm))
            {
                nm = JsonUtil.ExtractString(json, "name");
            }

            if (!string.IsNullOrEmpty(nm))
            {
                _playerLabel = nm;
            }

            _inWorld = !json.Contains("\"inWorld\":false");
            if (_inWorld && _charNameSet)
            {
                _gatePhase = 3;
                MaybeStartTutorial();
            }

            ApplySkillIdsFromState(json);

            ApplyQuestFromState(json);
            ApplyPortalsFromState(json);

            if (_world != null)
            {
                _status = "map " + _world.MapId + " " + _world.MapWidth + "×" + _world.MapHeight +
                          " @" + _x.ToString("0.0") + "," + _y.ToString("0.0");
            }
            else
            {
                _status = "sync_state";
            }

            MaybeBeginMapLoad(json);
            return;
        }

        if (json.Contains("\"type\":\"sync_xp\""))
        {
            var prevLevel = _level;
            if (JsonUtil.TryInt(json, "level", out var lv))
            {
                _level = lv;
            }

            if (JsonUtil.TryInt(json, "xp", out var xpNow))
            {
                _xp = xpNow;
            }

            if (JsonUtil.TryInt(json, "xpToLevel", out var need))
            {
                _xpToLevel = need;
            }

            if (JsonUtil.TryInt(json, "skillPoints", out var pts))
            {
                _skillPoints = pts;
            }

            if (_inWorld && _level > prevLevel)
            {
                SoundCatalog.Play(SoundCatalog.Id.LevelUp);
                _world?.PlayLevelUpFx(_world.SelfId);
            }

            return;
        }

        if (json.Contains("\"type\":\"sync_auth\""))
        {
            var token = JsonUtil.ExtractString(json, "guestToken");
            if (!string.IsNullOrEmpty(token))
            {
                _guestToken = token;
                PlayerPrefs.SetString("gaaacha_guest", token);
                PlayerPrefs.Save();
            }

            _authStatus = "logged in as " + (JsonUtil.ExtractString(json, "username") ?? "?");
            _showLogin = false;
            _gatePhase = 1;
            _inWorld = false;
            _input?.RequestServerList();
            return;
        }

        if (json.Contains("\"type\":\"sync_server_list\""))
        {
            _serverIds.Clear();
            _serverNames.Clear();
            var cursor = 0;
            while (true)
            {
                var idAt = json.IndexOf("\"id\":", cursor, System.StringComparison.Ordinal);
                if (idAt < 0)
                {
                    break;
                }

                var id = JsonUtil.ExtractString(json.Substring(idAt), "id");
                var nameAt = json.IndexOf("\"name\":", idAt, System.StringComparison.Ordinal);
                var name = nameAt > 0 ? JsonUtil.ExtractString(json.Substring(nameAt), "name") : id;
                if (!string.IsNullOrEmpty(id))
                {
                    _serverIds.Add(id);
                    _serverNames.Add(name ?? id);
                }

                cursor = idAt + 5;
            }

            if (_serverIds.Count == 0)
            {
                _serverIds.Add("local");
                _serverNames.Add("Local Dev");
            }

            if (_gatePhase < 1)
            {
                _gatePhase = 1;
            }

            return;
        }

        if (json.Contains("\"type\":\"sync_char_list\""))
        {
            for (var i = 0; i < 8; i++)
            {
                _charEmpty[i] = true;
                _charNames[i] = "";
                _charClasses[i] = "";
                _charLevels[i] = 1;
            }

            var cursor = 0;
            while (true)
            {
                var slotAt = json.IndexOf("\"slotIndex\":", cursor, System.StringComparison.Ordinal);
                if (slotAt < 0)
                {
                    break;
                }

                if (JsonUtil.TryInt(json.Substring(slotAt), "slotIndex", out var slot) && slot >= 0 && slot < 8)
                {
                    var chunk = json.Substring(slotAt, System.Math.Min(280, json.Length - slotAt));
                    _charEmpty[slot] = chunk.Contains("\"empty\":true");
                    _charNames[slot] = JsonUtil.ExtractString(chunk, "name") ?? "";
                    _charClasses[slot] = JsonUtil.ExtractString(chunk, "classId") ?? "";
                    if (JsonUtil.TryInt(chunk, "level", out var lv))
                    {
                        _charLevels[slot] = lv;
                    }
                }

                cursor = slotAt + 12;
            }

            _gatePhase = 2;
            _inWorld = false;
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

        if (json.Contains("\"type\":\"sync_pong\""))
        {
            if (JsonUtil.TryNumber(json, "serverTime", out var st))
            {
                _serverSkewMs = st - (Time.realtimeSinceStartupAsDouble * 1000.0);
            }

            return;
        }

        if (json.Contains("\"type\":\"sync_cond\""))
        {
            ApplyCondFromJson(json);
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

        if (json.Contains("\"type\":\"sync_party_invite\""))
        {
            _partyInviteId = JsonUtil.ExtractString(json, "inviteId");
            _partyInviteFrom = JsonUtil.ExtractString(json, "fromName");
            if (string.IsNullOrEmpty(_partyInviteFrom))
            {
                _partyInviteFrom = JsonUtil.ExtractString(json, "fromId");
            }

            _partyInviteUntil = Time.time + 60f;
            _status = "party invite from " + _partyInviteFrom;
            return;
        }

        if (json.Contains("\"type\":\"sync_party\""))
        {
            _partyId = JsonUtil.ExtractString(json, "partyId");
            if (_partyId == "null")
            {
                _partyId = "";
            }

            ParsePartyMembersFull(json);
            _status = string.IsNullOrEmpty(_partyId) ? "left party" : "party " + _partyMembersLine;
            return;
        }

        if (json.Contains("\"type\":\"sync_guild_invite\""))
        {
            _guildInviteId = JsonUtil.ExtractString(json, "inviteId");
            _guildInviteFrom = JsonUtil.ExtractString(json, "fromName");
            _guildInviteName = JsonUtil.ExtractString(json, "guildName");
            _guildInviteUntil = Time.time + 60f;
            _status = "guild invite " + _guildInviteName;
            return;
        }

        if (json.Contains("\"type\":\"sync_guild\""))
        {
            var name = JsonUtil.ExtractString(json, "guildName");
            if (!string.IsNullOrEmpty(name))
            {
                _guildName = name;
            }

            _status = "guild " + _guildName;
            return;
        }

        if (json.Contains("\"type\":\"sync_trade_invite\""))
        {
            _tradeInviteId = JsonUtil.ExtractString(json, "inviteId");
            _tradeInviteFrom = JsonUtil.ExtractString(json, "fromName");
            _tradeInviteUntil = Time.time + 60f;
            _status = "trade invite from " + _tradeInviteFrom;
            return;
        }

        if (json.Contains("\"type\":\"sync_trade\""))
        {
            _tradeId = JsonUtil.ExtractString(json, "tradeId");
            if (_tradeId == "null")
            {
                _tradeId = "";
            }

            var you = JsonUtil.SliceAround(json, "\"you\"", 0, 400);
            var them = JsonUtil.SliceAround(json, "\"them\"", 0, 500);
            if (JsonUtil.TryInt(you, "gold", out var yg))
            {
                _tradeMyGold = yg;
            }

            _tradeMyConfirm = you.Contains("\"confirmed\":true");
            if (JsonUtil.TryInt(them, "gold", out var tg))
            {
                _tradeTheirGold = tg;
            }

            _tradeTheirConfirm = them.Contains("\"confirmed\":true");
            _tradeTheirName = JsonUtil.ExtractString(them, "name") ?? "";
            _tradeOfferSummary = "you " + _tradeMyGold + "g / them " + _tradeTheirGold + "g";
            if (string.IsNullOrEmpty(_tradeId))
            {
                _tradeOfferSlots.Clear();
                _tradeOfferQtys.Clear();
            }

            return;
        }

        if (json.Contains("\"type\":\"sync_friends\""))
        {
            ParseFriends(json);
            return;
        }

        if (json.Contains("\"type\":\"sync_skills\""))
        {
            if (JsonUtil.TryInt(json, "skillPoints", out var sp))
            {
                _skillPoints = sp;
            }

            ParseSkillLists(json);
            return;
        }

        if (json.Contains("\"type\":\"sync_auction\""))
        {
            ParseAuction(json);
            _showAuction = true;
            return;
        }

        if (json.Contains("\"type\":\"sync_instance\""))
        {
            if (JsonUtil.TryInt(json, "expiresAt", out var exp))
            {
                _instanceExpiresAt = exp;
            }
            else if (JsonUtil.TryNumber(json, "expiresAt", out var expN))
            {
                _instanceExpiresAt = (long)expN;
            }

            if (JsonUtil.TryInt(json, "phase", out var ph))
            {
                _bossPhase = ph;
            }

            var iid = JsonUtil.ExtractString(json, "instanceId");
            if (iid == "null" || string.IsNullOrEmpty(iid))
            {
                _instanceExpiresAt = 0;
            }

            return;
        }

        if (json.Contains("\"type\":\"sync_interact\""))
        {
            ApplyInteractFromJson(json);
            _showNpcDialog = true;
            _showInteract = false;
            _pendingTalkId = "";
            _status = "talk " + _interactKind;
            return;
        }

        if (json.Contains("\"type\":\"sync_quest\""))
        {
            ApplyQuestFromState(json);
            return;
        }

        if (json.Contains("\"type\":\"sync_gold\""))
        {
            if (JsonUtil.TryInt(json, "gold", out var g))
            {
                _gold = g;
            }

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
                if (JsonUtil.TryInt(json, "seq", out var seq) && seq > 0)
                {
                    if (seq <= _moveAckSeq)
                    {
                        return;
                    }

                    _moveAckSeq = seq;
                }

                _lastGoodX = x;
                _lastGoodY = y;
                var dx = _x - x;
                var dy = _y - y;
                if (dx * dx + dy * dy > 2.25f)
                {
                    _x = x;
                    _y = y;
                    _world.SetLocalPos(_x, _y, instant: false);
                }
            }

            return;
        }

        if (json.Contains("\"code\":\"move_locked\"") || json.Contains("\"code\":\"blocked_entity\"") || json.Contains("\"code\":\"blocked\""))
        {
            if (IsStaleMoveReject(json))
            {
                return;
            }

            _x = _lastGoodX;
            _y = _lastGoodY;
            _world.SetLocalPos(_x, _y, instant: false);
            if (json.Contains("move_locked") && !_hasServerCond)
            {
                _moveLockUntil = Time.time + 0.25f;
            }

            _status = json.Contains("blocked_entity") ? "blocked entity"
                : json.Contains("move_locked") ? "move_locked"
                : "blocked tile";
            return;
        }

        if (json.Contains("\"type\":\"sync_skill\"") || json.Contains("\"type\":\"sync_aoe\""))
        {
            if (JsonUtil.TryInt(json, "mpAfter", out var mpAfter))
            {
                _mp = mpAfter;
            }

            var target = JsonUtil.ExtractString(json, "targetId");
            if (target == _world.SelfId && JsonUtil.TryInt(json, "hpAfter", out var hpAfterSelf))
            {
                _hp = hpAfterSelf;
            }

            if (JsonUtil.TryInt(json, "hpAfter", out var hpAfter))
            {
                var corr = _prediction.ReconcileSkill(_lastCastRequestId, target, hpAfter);
                if (corr.HasValue && !string.IsNullOrEmpty(corr.Value.EntityId) && corr.Value.Hp.HasValue)
                {
                    _world?.ApplyReconcileHp(corr.Value.EntityId, corr.Value.Hp.Value, corr.Value.Hard);
                    GameLog.Warn(GameLog.Channel.Combat,
                        "reconcile  entity=" + corr.Value.EntityId +
                        "  hp=" + corr.Value.Hp.Value +
                        "  hard=" + corr.Value.Hard);
                }

                _lastCastRequestId = "";
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
                if (JsonUtil.TryInt(json, "maxHp", out var maxHp) && maxHp > 0)
                {
                    _maxHp = maxHp;
                }
                if (JsonUtil.TryInt(json, "maxMp", out var maxMp) && maxMp > 0)
                {
                    _maxMp = maxMp;
                }
            }

            for (var i = 0; i < _partyMemberIds.Count; i++)
            {
                if (_partyMemberIds[i] != id)
                {
                    continue;
                }

                if (JsonUtil.TryInt(json, "hp", out var php))
                {
                    _partyMemberHp[i] = php;
                }

                if (JsonUtil.TryInt(json, "maxHp", out var pmax) && pmax > 0)
                {
                    _partyMemberMaxHp[i] = pmax;
                }

                if (JsonUtil.TryInt(json, "mp", out var pmp))
                {
                    _partyMemberMp[i] = pmp;
                }

                if (JsonUtil.TryInt(json, "maxMp", out var pmaxMp) && pmaxMp > 0)
                {
                    _partyMemberMaxMp[i] = pmaxMp;
                }
            }

            return;
        }

        if (json.Contains("\"type\":\"sync_gacha\""))
        {
            SoundCatalog.Play(SoundCatalog.Id.Pull);
            var pitySlice = JsonUtil.SliceAround(json, "\"pity\"", 0, 640);
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
                ApplyPityExtras(pitySlice);
            }

            var results = JsonUtil.SliceAround(json, "\"results\"", 0, 1200);
            _lastDrop = JsonUtil.ExtractString(results, "itemId");
            var rarity = JsonUtil.ExtractString(results, "rarity");
            if (!string.IsNullOrEmpty(rarity))
            {
                _lastDrop = _lastDrop + " (" + rarity + ")";
            }

            var ssrN = CountJsonToken(json, "\"ssr\"");
            var srN = CountJsonToken(json, "\"sr\"");
            var rN = CountJsonToken(json, "\"r\"");
            _gachaSummary = ssrN + " SSR  " + srN + " SR  " + rN + " R";
            _showGacha = true;
            _status = "gacha " + _lastDrop;
            _world?.PlayGachaRevealFx();
            return;
        }

        if (json.Contains("\"type\":\"sync_death\""))
        {
            var deadId = JsonUtil.ExtractString(json, "entityId");
            if (_world == null || string.IsNullOrEmpty(deadId) || deadId == _world.SelfId)
            {
                _showDeath = true;
                _hp = 0;
                _status = "you are dead";
            }

            return;
        }

        if (json.Contains("\"type\":\"sync_class_change\""))
        {
            _classId = JsonUtil.ExtractString(json, "classId");
            var cname = JsonUtil.ExtractString(json, "className");
            _classChangeTitle = string.IsNullOrEmpty(cname) ? _classId : cname;
            _classChangeBody = "New skill set unlocked. Resist bonus applied.\nHUD sprite updates with class.";
            _showClassChange = true;
            _world?.SetClassLook(_classId);
            ApplySkillIdsFromState(json);
            _status = "class change " + _classChangeTitle;
            return;
        }

        if (json.Contains("\"type\":\"sync_equip\""))
        {
            var wid = JsonUtil.ExtractString(json, "weaponId");
            if (!string.IsNullOrEmpty(wid))
            {
                _weapon = wid;
            }

            var w2 = JsonUtil.ExtractString(json, "weapon2Id");
            if (string.IsNullOrEmpty(w2))
            {
                w2 = JsonUtil.ExtractString(json, "equippedWeapon2Id");
            }

            if (json.Contains("\"weapon2Id\":null") || json.Contains("\"equippedWeapon2Id\":null"))
            {
                _weapon2 = "none";
            }
            else if (!string.IsNullOrEmpty(w2) && w2 != "null")
            {
                _weapon2 = w2;
            }

            RefreshWeaponMeta();

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

            ApplyGearIdsFromJson(json);
            _status = "Haupt " + WeaponPretty(_weapon) + " / Sek " + WeaponPretty(_weapon2);
            return;
        }

        if (json.Contains("\"type\":\"sync_loot\""))
        {
            _lastDrop = JsonUtil.ExtractString(json, "itemId") + " x" +
                (JsonUtil.TryInt(json, "quantity", out var qty) ? qty.ToString() : "1");
            _status = "loot " + _lastDrop;
            SoundCatalog.Play(SoundCatalog.Id.Loot);
            _world?.PlayPickupAnim();
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
        else if (channel == "party")
        {
            tab = "party";
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

        var block = JsonUtil.ExtractArray(json, "cooldowns");
        if (string.IsNullOrEmpty(block))
        {
            block = JsonUtil.SliceAround(json, "\"cooldowns\"", 0, 2400);
        }

        if (string.IsNullOrEmpty(block))
        {
            return;
        }

        var cursor = 0;
        while (true)
        {
            var idx = block.IndexOf("\"id\":", cursor);
            if (idx < 0)
            {
                break;
            }

            var slice = block.Substring(idx, Mathf.Min(160, block.Length - idx));
            var id = JsonUtil.ExtractString(slice, "id");
            if (string.IsNullOrEmpty(id) || id.StartsWith("monster_") || id.StartsWith("player_"))
            {
                cursor = idx + 5;
                continue;
            }

            if (JsonUtil.TryNumber(slice, "readyAt", out var readyAt) && readyAt > 1.0)
            {
                var localReady = (float)((readyAt - _serverSkewMs) / 1000.0);
                _readyAtLocal[id] = localReady;
            }
            else
            {
                _readyAtLocal.Remove(id);
            }

            if (JsonUtil.TryNumber(slice, "cooldownMs", out var cd) && cd > 0f)
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

        var resting = false;
        for (var i = 0; i < _buffs.Count; i++)
        {
            if (_buffs[i].Id == "resting")
            {
                resting = true;
                break;
            }
        }

        _world?.SetResting(resting);
    }

    private void CancelRestOnMove()
    {
        for (var i = _buffs.Count - 1; i >= 0; i--)
        {
            if (_buffs[i].Id == "resting")
            {
                _buffs.RemoveAt(i);
            }
        }

        _world?.BreakRestFromMove();
    }

    private void TickHeartbeat()
    {
        if (_net == null || !_net.IsConnected)
        {
            return;
        }

        _pingAcc += Time.unscaledDeltaTime;
        if (_pingAcc < 5f)
        {
            return;
        }

        _pingAcc = 0f;
        _ = _net.SendPingAsync();
    }

    private bool IsStaleMoveReject(string json)
    {
        if (!JsonUtil.TryInt(json, "seq", out var seq) || seq <= 0 || _net == null)
        {
            return false;
        }

        return seq < _net.LastMoveSeq;
    }

    private void ApplyCondFromJson(string json)
    {
        var wasMove = _canMove;
        _hasServerCond = true;
        _canMove = !json.Contains("\"canMove\":false");
        _canAct = !json.Contains("\"canAct\":false");
        var resting = json.Contains("\"resting\":true");
        if (JsonUtil.TryNumber(json, "serverTime", out var serverTime))
        {
            _serverSkewMs = serverTime - (Time.realtimeSinceStartupAsDouble * 1000.0);
        }

        _world?.SetResting(resting);
        if (wasMove && !_canMove)
        {
            _x = _lastGoodX;
            _y = _lastGoodY;
            _world?.SetLocalPos(_x, _y, instant: false);
            CancelClickMove();
        }
    }

    private void HandleContinuousMove()
    {
        if (_hp <= 0 || _showDeath)
        {
            return;
        }

        if (MoveLockedNow)
        {
            return;
        }

        var dir = ReadMoveIntent();
        var kbOnly = ReadKeyboardMove();
        var steering = kbOnly.sqrMagnitude >= 0.01f || dir.sqrMagnitude >= 0.45f;
        if (IsCasting)
        {
            if (!CanMoveCancelCast || (!steering && !_clickMoveActive))
            {
                return;
            }

            CancelLocalCast();
        }

        if (steering)
        {
            CancelClickMove();
            if (!(_stickyAutoAttack && IsAutoAttackSkill(_pendingSkillId)))
            {
                ClearPendingSkill(false);
            }
        }
        else if (_clickMoveActive || HasPendingSkillChase())
        {
            dir = ComputeAutoMoveDir();
            if (dir.sqrMagnitude < 0.01f)
            {
                return;
            }
        }
        else
        {
            return;
        }

        dir.Normalize();
        if (_world != null)
        {
            if (steering)
            {
                _world.GetCameraBasisXY(out var scrRight, out var scrUp);
                var intentX = dir.x;
                var intentY = dir.y;
                dir = intentX * scrRight + intentY * scrUp;
                if (dir.sqrMagnitude > 1e-8f)
                {
                    dir.Normalize();
                }
            }

            _world.SetLocalFacingFromWorld(dir.x, dir.y);
        }

        var speed = _moveSpeed * _moveSpeedMult;
        var dt = Time.deltaTime;
        if (_world != null)
        {
            _world.Depenetrate(ref _x, ref _y);
        }

        var nx = _x + dir.x * speed * dt;
        var ny = _y + dir.y * speed * dt;
        // Soft local collision — walls + other players/NPCs. Player↔monster overlap is allowed.
        if (_world != null && _world.WouldBlockLocalMove(nx, ny))
        {
            if (!_world.WouldBlockLocalMove(nx, _y))
            {
                ny = _y;
            }
            else if (!_world.WouldBlockLocalMove(_x, ny))
            {
                nx = _x;
            }
            else
            {
                var sx = nx;
                var sy = ny;
                _world.Depenetrate(ref sx, ref sy);
                if (_world.WouldBlockLocalMove(sx, sy))
                {
                    if (_clickMoveActive || HasPendingSkillChase())
                    {
                        TryRebuildRoute();
                    }

                    return;
                }

                nx = sx;
                ny = sy;
            }
        }

        _x = nx;
        _y = ny;
        _world.SetLocalPos(_x, _y);
        CancelRestOnMove();
        TryPendingNpcTalk();

        if (_clickMoveActive)
        {
            var dx = _clickMoveX - _x;
            var dy = _clickMoveY - _y;
            if (dx * dx + dy * dy <= 0.12f * 0.12f)
            {
                CancelClickMove();
                TryPendingNpcTalk();
            }
        }

        _moveSendAcc += dt;
        if (_moveSendAcc < 1f / 12f)
        {
            return;
        }

        _moveSendAcc = 0f;
        if (_input != null && _net != null && _net.IsConnected)
        {
            _input.RequestMove(_x, _y);
            if (!_clickMoveActive && !HasPendingSkillChase())
            {
                _status = "move " + _x.ToString("0.0") + "," + _y.ToString("0.0");
            }
        }
    }

    private Vector2 ComputeAutoMoveDir()
    {
        float tx;
        float ty;
        if (HasPendingSkillChase() && _world != null &&
            _world.TryGetMapXY(_pendingSkillTarget, out tx, out ty))
        {
            var dx = tx - _x;
            var dy = ty - _y;
            if (BestRangeGap(_pendingSkillTarget) <= _pendingSkillRange + 0.08f)
            {
                return Vector2.zero;
            }

            if (Time.time >= _repathAt)
            {
                BuildRoute(tx, ty, false);
                _repathAt = Time.time + 0.4f;
            }

            var along = FollowRouteDir();
            if (along.sqrMagnitude >= 1e-6f)
            {
                return along;
            }

            if (dx * dx + dy * dy < 1e-6f)
            {
                return Vector2.zero;
            }

            return new Vector2(dx, dy);
        }

        if (_clickMoveActive)
        {
            var along = FollowRouteDir();
            if (along.sqrMagnitude >= 1e-6f)
            {
                return along;
            }

            return new Vector2(_clickMoveX - _x, _clickMoveY - _y);
        }

        return Vector2.zero;
    }

    private Vector2 FollowRouteDir()
    {
        while (_routeI < _route.Count)
        {
            var wp = _route[_routeI];
            var dx = wp.x - _x;
            var dy = wp.y - _y;
            if (dx * dx + dy * dy <= 0.22f * 0.22f)
            {
                _routeI++;
                continue;
            }

            return new Vector2(dx, dy);
        }

        if (_clickMoveActive)
        {
            var dx = _clickMoveX - _x;
            var dy = _clickMoveY - _y;
            if (dx * dx + dy * dy <= 0.12f * 0.12f)
            {
                CancelClickMove();
                TryPendingNpcTalk();
                return Vector2.zero;
            }
        }

        return Vector2.zero;
    }

    private void BuildRoute(float destX, float destY, bool announce)
    {
        _clickMoveX = destX;
        _clickMoveY = destY;
        _route.Clear();
        _routeI = 0;
        if (_world == null)
        {
            return;
        }

        if (!_world.TryFindPath(_x, _y, destX, destY, _route))
        {
            _route.Clear();
            if (announce)
            {
                _status = "no path";
                _clickMoveActive = false;
            }

            return;
        }

        if (_route.Count > 0 && Vector2.Distance(_route[0], new Vector2(_x, _y)) < 0.35f)
        {
            _routeI = 1;
        }

        var last = _route[_route.Count - 1];
        _clickMoveX = last.x;
        _clickMoveY = last.y;

        if (announce)
        {
            _status = "route " + _clickMoveX.ToString("0.0") + "," + _clickMoveY.ToString("0.0");
        }
    }

    private void TryRebuildRoute()
    {
        if (Time.time < _repathAt)
        {
            return;
        }

        _repathAt = Time.time + 0.25f;
        if (HasPendingSkillChase() && _world != null &&
            _world.TryGetMapXY(_pendingSkillTarget, out var tx, out var ty))
        {
            BuildRoute(tx, ty, false);
            return;
        }

        if (_clickMoveActive)
        {
            BuildRoute(_clickMoveX, _clickMoveY, false);
        }
    }

    private bool HasPendingSkillChase()
    {
        return !string.IsNullOrEmpty(_pendingSkillId) && Time.time < _pendingSkillUntil;
    }

    private void CancelClickMove()
    {
        _clickMoveActive = false;
        _route.Clear();
        _routeI = 0;
    }

    private bool NpcInTalkRange(string npcId)
    {
        if (_world == null || string.IsNullOrEmpty(npcId) || !_world.TryGetMapXY(npcId, out var tx, out var ty))
        {
            return false;
        }

        var dx = tx - _x;
        var dy = ty - _y;
        return dx * dx + dy * dy <= NpcTalkRange * NpcTalkRange;
    }

    private void StartNpcTalk(string npcId)
    {
        if (string.IsNullOrEmpty(npcId) || _input == null || _world == null)
        {
            return;
        }

        _world.SetLockTarget(npcId);
        _pendingTalkId = npcId;
        if (NpcInTalkRange(npcId))
        {
            _pendingTalkId = "";
            _input.RequestInteract(npcId);
            _status = "talk " + npcId;
            return;
        }

        if (_world.TryGetMapXY(npcId, out var tx, out var ty))
        {
            StartClickRoute(tx, ty);
            _status = "approach " + npcId;
        }
    }

    private void TryPendingNpcTalk()
    {
        if (string.IsNullOrEmpty(_pendingTalkId) || _input == null)
        {
            return;
        }

        if (!NpcInTalkRange(_pendingTalkId))
        {
            return;
        }

        var id = _pendingTalkId;
        _pendingTalkId = "";
        CancelClickMove();
        _input.RequestInteract(id);
        _status = "talk " + id;
    }

    private void CloseNpcDialog()
    {
        if (_showNpcDialog)
        {
            _input?.RequestDialogClose();
        }

        _showNpcDialog = false;
        _showInteract = false;
    }

    private void ClearPendingSkill(bool announce)
    {
        if (announce && !string.IsNullOrEmpty(_pendingSkillId))
        {
            _status = "skill chase cancelled";
        }

        _pendingSkillId = "";
        _pendingSkillTarget = "";
        _pendingSkillRange = 0f;
        _pendingSkillUntil = 0f;
        _world?.ClearAimIndicator();
    }

    private void BeginPendingSkill(string skillId, string targetId, float range)
    {
        _pendingSkillId = skillId;
        _pendingSkillTarget = targetId;
        _pendingSkillRange = range;
        _pendingSkillUntil = Time.time + 8f;
        RefreshTargetAoeAim(skillId, targetId);
        CancelClickMove();
        _repathAt = 0f;
        _status = skillId + " → approach " + targetId;
        GameLog.CastLocal(skillId, targetId, "approach");
    }

    private void TickPendingSkillChase()
    {
        if (!HasPendingSkillChase() || _world == null || _input == null)
        {
            if (!string.IsNullOrEmpty(_pendingSkillId) && Time.time >= _pendingSkillUntil)
            {
                ClearPendingSkill(true);
            }

            return;
        }

        if (string.IsNullOrEmpty(_pendingSkillTarget) || !_world.TryGetMapXY(_pendingSkillTarget, out _, out _))
        {
            ClearPendingSkill(true);
            return;
        }

        var gap = BestRangeGap(_pendingSkillTarget);
        if (gap > _pendingSkillRange + 0.08f)
        {
            RefreshTargetAoeAim(_pendingSkillId, _pendingSkillTarget);
            return;
        }

        var skillId = _pendingSkillId;
        var targetId = _pendingSkillTarget;
        ClearPendingSkill(false);
        ExecuteCastNow(skillId, targetId);
    }

    private void TickStickyAutoAttack()
    {
        if (!_stickyAutoAttack || _world == null || _hp <= 0 || _showDeath || ActLockedNow)
        {
            return;
        }

        var lockId = _world.LockTargetId;
        if (string.IsNullOrEmpty(lockId) || !_world.IsLivingFoe(lockId))
        {
            _stickyAutoAttack = false;
            return;
        }

        if (IsCasting || HasPendingSkillChase() || _clickMoveActive)
        {
            return;
        }

        var kb = ReadKeyboardMove();
        if (kb.sqrMagnitude >= 0.01f)
        {
            return;
        }

        var stick = ReadMoveIntent();
        if (stick.sqrMagnitude >= 0.45f)
        {
            return;
        }

        var skill = string.IsNullOrEmpty(_stickyAttackSkill) ? "auto_attack" : _stickyAttackSkill;
        if (_readyAtLocal.TryGetValue(skill, out var readyAt) &&
            readyAt > Time.realtimeSinceStartup + 0.04f)
        {
            return;
        }

        TryCastOrChase(skill, lockId);
    }

    private Vector2 ReadKeyboardMove()
    {
        var v = Vector2.zero;
        if (_chatFocused)
        {
            return v;
        }

        var kb = Keyboard.current;
        if (kb == null)
        {
            return v;
        }

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

        return v;
    }

    private Vector2 ReadMoveIntent()
    {
        var v = _joystick != null ? _joystick.Axis : Vector2.zero;
        return v + ReadKeyboardMove();
    }

    private void HandleActionKeys()
    {
        var kb = Keyboard.current;
        if (kb == null)
        {
            return;
        }

        if (kb.f1Key.wasPressedThisFrame)
        {
            _showHelp = !_showHelp;
            _status = _showHelp ? "help on (F1)" : "help off";
            return;
        }

        if (kb.f8Key.wasPressedThisFrame)
        {
            var n = SpriteCatalog.AuditWiredClips();
            _status = n == 0
                ? "sprite audit ok (F8)"
                : "sprite audit FAIL " + n + " empty_facing/invisible — GFX log";
        }

        if (kb.f9Key.wasPressedThisFrame)
        {
            var path = GameLog.DumpRing();
            _status = "log dump  " + path;
        }

        if (kb.f10Key.wasPressedThisFrame)
        {
            GameLog.CycleLevel();
            _status = "log level " + GameLog.LevelName + " (F10)";
        }

        if (_hp <= 0 || _showDeath)
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

            if (_showNpcDialog || _showShopBuy || _showShopSell || _showClassService || _showEnhance)
            {
                CloseNpcDialog();
                _showShopBuy = false;
                _showShopSell = false;
                _showClassService = false;
                _showEnhance = false;
                _showSkillTree = false;
                _showAuction = false;
                _status = "menu closed";
                return;
            }

            if (_showFullMap)
            {
                _showFullMap = false;
                _status = "map closed";
                return;
            }

            if (_showCtxMenu || _showInspect || _showGacha || _showHelp || _chatFocused || !string.IsNullOrEmpty(_itemTooltip))
            {
                _showCtxMenu = false;
                _showInspect = false;
                _showGacha = false;
                _showHelp = false;
                _chatFocused = false;
                _itemTooltip = "";
                return;
            }

            _showSettings = !_showSettings;
            _status = _showSettings ? "settings on" : "settings off";
            return;
        }

        if (kb.tabKey.wasPressedThisFrame && !_chatFocused)
        {
            if (_world == null)
            {
                return;
            }

            if (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed)
            {
                _world.ClearLockTarget();
                _input?.SetTarget("");
                _status = "target cleared";
                return;
            }

            var target = _world.CycleLockTarget();
            _input?.SetTarget(target ?? "");
            if (string.IsNullOrEmpty(target))
            {
                _status = "no foes in range";
            }
            else
            {
                _status = "lock-on " + target;
            }

            return;
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

        if (TryPlayEmote(kb))
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

            if (kb.spaceKey.wasPressedThisFrame || kb.zKey.wasPressedThisFrame || DigitPressedThisFrame(kb) >= 0)
            {
                return;
            }
        }

        if (kb.spaceKey.wasPressedThisFrame)
        {
            CastHotbar(0);
        }

        if (kb.zKey.wasPressedThisFrame)
        {
            if (!HasSecondaryWeapon())
            {
                _status = "no offhand equipped";
            }
            else
            {
                CastSkill("auto_attack_off");
            }

            return;
        }

        var digit = DigitPressedThisFrame(kb);
        if (digit >= 1 && digit <= 7)
        {
            CastHotbar(digit);
        }
        if (kb.fKey.wasPressedThisFrame)
        {
            ToggleInspectOnLock();
        }
        if (kb.iKey.wasPressedThisFrame)
        {
            _showInventory = !_showInventory;
            _status = _showInventory ? "inventory on" : "inventory off";
        }
        if (kb.jKey.wasPressedThisFrame)
        {
            _showQuestLog = !_showQuestLog;
            _status = _showQuestLog ? "quests on" : "quests off";
        }
        if (kb.mKey.wasPressedThisFrame)
        {
            _showFullMap = !_showFullMap;
            _status = _showFullMap ? "map open (M close)" : "map closed";
        }
        if (kb.pKey.wasPressedThisFrame)
        {
            CycleSpirit();
        }
        if (kb.gKey.wasPressedThisFrame)
        {
            _showGacha = !_showGacha;
            _status = _showGacha ? "banner" : "banner closed";
        }
        if (kb.hKey.wasPressedThisFrame)
        {
            TryUseNearestPortal();
        }
        if (kb.nKey.wasPressedThisFrame)
        {
            TryWeaponSwap();
        }
        if (kb.rKey.wasPressedThisFrame && !_chatFocused)
        {
            TryQuickUseConsumable();
        }
        if (kb.lKey.wasPressedThisFrame)
        {
            _showLogin = !_showLogin;
            _status = _showLogin ? "login panel" : "login closed";
        }
        if (kb.kKey.wasPressedThisFrame)
        {
            _showFriends = !_showFriends;
            _status = _showFriends ? "friends on" : "friends off";
        }
        if (kb.uKey.wasPressedThisFrame)
        {
            _showSettings = !_showSettings;
            _status = _showSettings ? "settings on" : "settings off";
        }
        if (kb.tKey.wasPressedThisFrame)
        {
            _showGacha = true;
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

    private void CastHotbar(int index)
    {
        if (_skillIds == null || index < 0 || index >= _skillIds.Length)
        {
            return;
        }

        CastSkill(_skillIds[index]);
    }

    private bool SkillUnlocked(string skillId)
    {
        if (string.IsNullOrEmpty(skillId) || IsAutoAttackSkill(skillId))
        {
            return true;
        }

        if (_unlockedSkills.Count == 0)
        {
            return skillId == "shot";
        }

        return _unlockedSkills.Contains(skillId);
    }

    private static bool IsAutoAttackSkill(string skillId)
    {
        return skillId == "auto_attack" || skillId == "auto_attack_off";
    }

    private int HotbarIndexOf(string skillId)
    {
        if (_skillIds == null)
        {
            return -1;
        }

        for (var i = 0; i < _skillIds.Length; i++)
        {
            if (_skillIds[i] == skillId)
            {
                return i;
            }
        }

        return -1;
    }

    private static int DigitPressedThisFrame(Keyboard kb)
    {
        if (kb.digit1Key.wasPressedThisFrame) return 1;
        if (kb.digit2Key.wasPressedThisFrame) return 2;
        if (kb.digit3Key.wasPressedThisFrame) return 3;
        if (kb.digit4Key.wasPressedThisFrame) return 4;
        if (kb.digit5Key.wasPressedThisFrame) return 5;
        if (kb.digit6Key.wasPressedThisFrame) return 6;
        if (kb.digit7Key.wasPressedThisFrame) return 7;
        if (kb.digit8Key.wasPressedThisFrame) return 8;
        if (kb.digit9Key.wasPressedThisFrame) return 9;
        if (kb.digit0Key.wasPressedThisFrame) return 0;
        return -1;
    }

    private bool TryPlayEmote(Keyboard kb)
    {
        if (kb == null || _world == null)
        {
            return false;
        }

        if (!kb.leftShiftKey.isPressed && !kb.rightShiftKey.isPressed)
        {
            return false;
        }

        var digit = DigitPressedThisFrame(kb);
        if (digit < 0)
        {
            return false;
        }

        var msg = _world.PlayEmoteSlot(digit);
        if (!string.IsNullOrEmpty(msg))
        {
            _status = msg;
        }

        return true;
    }

    private static bool DigitIsPressed(Keyboard kb, int n)
    {
        return n switch
        {
            1 => kb.digit1Key.isPressed,
            2 => kb.digit2Key.isPressed,
            3 => kb.digit3Key.isPressed,
            4 => kb.digit4Key.isPressed,
            5 => kb.digit5Key.isPressed,
            6 => kb.digit6Key.isPressed,
            7 => kb.digit7Key.isPressed,
            8 => kb.digit8Key.isPressed,
            9 => kb.digit9Key.isPressed,
            _ => false,
        };
    }

    private static bool DigitWasReleased(Keyboard kb, int n)
    {
        return n switch
        {
            1 => kb.digit1Key.wasReleasedThisFrame,
            2 => kb.digit2Key.wasReleasedThisFrame,
            3 => kb.digit3Key.wasReleasedThisFrame,
            4 => kb.digit4Key.wasReleasedThisFrame,
            5 => kb.digit5Key.wasReleasedThisFrame,
            6 => kb.digit6Key.wasReleasedThisFrame,
            7 => kb.digit7Key.wasReleasedThisFrame,
            8 => kb.digit8Key.wasReleasedThisFrame,
            9 => kb.digit9Key.wasReleasedThisFrame,
            _ => false,
        };
    }

    private void CastSkill(string skillId)
    {
        if (_input == null || _world == null)
        {
            return;
        }

        if (ActLockedNow)
        {
            _status = "cannot act";
            return;
        }

        if (IsCasting)
        {
            if (IsAutoAttackSkill(skillId) || skillId == _castSkillId || skillId == _queuedSkillId)
            {
                return;
            }

            _queuedSkillId = skillId;
            _status = SkillDisplayName(skillId) + " queued";
            return;
        }

        if (!SkillUnlocked(skillId))
        {
            var need = 0;
            _unlockLevels.TryGetValue(skillId, out need);
            _status = need > 0
                ? SkillDisplayName(skillId) + " unlocks at L" + need
                : SkillDisplayName(skillId) + " locked";
            return;
        }

        if (!string.IsNullOrEmpty(_aimSkillId) && _aimSkillId != skillId)
        {
            CancelAim();
        }
        else if (!string.IsNullOrEmpty(_aimSkillId))
        {
            return;
        }

        if (_readyAtLocal.TryGetValue(skillId, out var readyAt) &&
            readyAt > Time.realtimeSinceStartup + 0.04f)
        {
            _status = SkillDisplayName(skillId) + " cooling down";
            return;
        }

        var mana = SkillManaCost(skillId);
        if (mana > 0 && _mp < mana)
        {
            _status = "Not enough MP (" + mana + ")";
            return;
        }

        if (IsAutoAttackSkill(skillId))
        {
            _stickyAutoAttack = true;
            _stickyAttackSkill = skillId;
        }

        if (skillId == "rest")
        {
            _stickyAutoAttack = false;
        }

        if (IndicatorSkills.ContainsKey(skillId))
        {
            var shotLock = _world.LockTargetId;
            if (string.IsNullOrEmpty(shotLock) || shotLock == _world.SelfId || !_world.IsLivingTarget(shotLock) ||
                _world.IsPlayerEntity(shotLock))
            {
                shotLock = _world.LockClosestEnemy();
            }

            if (!string.IsNullOrEmpty(shotLock) && _world.IsLivingTarget(shotLock) && !_world.IsPlayerEntity(shotLock))
            {
                _world.SetLockTarget(shotLock);
                TryCastOrChase(skillId, shotLock);
                return;
            }

            BeginAim(skillId, fromBar: false);
            return;
        }

        if (NoTargetSkills.Contains(skillId) || IsCasterAoeSkill(skillId))
        {
            ClearPendingSkill(false);
            _input.SetTarget(_world.SelfId);
            BeginLocalCast(skillId);
            _world.PlayCastAttack(_world.SelfId, _world.SelfId, skillId);
            _input.Cast(skillId);
            _status = SkillDisplayName(skillId);
            return;
        }

        if (IsAllyTargetSkill(skillId))
        {
            var allyId = ResolveAllyTargetId();
            TryCastOrChase(skillId, allyId);
            return;
        }

        var lockId = _world.LockTargetId;
        if (string.IsNullOrEmpty(lockId) || lockId == _world.SelfId || !_world.IsLivingTarget(lockId) ||
            _world.IsPlayerEntity(lockId))
        {
            if (IsAutoAttackSkill(skillId))
            {
                var aaRange = skillId == "auto_attack_off" ? _weapon2Range : _weaponRange;
                lockId = _world.FindClosestEnemyInRange(aaRange);
                if (string.IsNullOrEmpty(lockId) || _world.IsPlayerEntity(lockId))
                {
                    lockId = _world.LockClosestEnemy();
                }
            }
            else
            {
                lockId = _world.LockClosestEnemy();
            }
        }

        if (string.IsNullOrEmpty(lockId) || !_world.IsLivingTarget(lockId))
        {
            _status = skillId + " — need target (Tab / LMB)";
            return;
        }

        _world.SetLockTarget(lockId);
        TryCastOrChase(skillId, lockId);
    }

    private float ResolveSkillRange(string skillId)
    {
        if (IsAutoAttackSkill(skillId))
        {
            return Mathf.Max(0.8f, skillId == "auto_attack_off" ? _weapon2Range : _weaponRange);
        }

        if (SkillRanges.TryGetValue(skillId, out var r) && r > 0f)
        {
            return r;
        }

        if (_skillHud.TryGetValue(skillId, out var hud) && hud.Range > 0f)
        {
            return hud.Range;
        }

        if (IndicatorSkills.TryGetValue(skillId, out var def))
        {
            return def.Range;
        }

        return 1.5f;
    }

    private bool IsAllyTargetSkill(string skillId)
    {
        if (_skillHud.TryGetValue(skillId, out var hud) && hud.TargetingType == "ALLY_TARGET")
        {
            return true;
        }

        return skillId == "ally_mend" || skillId == "mend";
    }

    private bool IsCasterAoeSkill(string skillId)
    {
        if (_skillHud.TryGetValue(skillId, out var hud) &&
            hud.AoeOrigin == "caster" &&
            (hud.Affects == "friendly" || hud.Affects == "self"))
        {
            return true;
        }

        return skillId == "group_chant" || skillId == "rally";
    }

    private string ResolveAllyTargetId()
    {
        var lockId = _world.LockTargetId;
        if (!string.IsNullOrEmpty(lockId) && _world.IsLivingTarget(lockId) && _world.IsPlayerEntity(lockId))
        {
            return lockId;
        }

        var closest = _world.FindClosestPlayer(12f);
        if (!string.IsNullOrEmpty(closest) && _world.IsLivingTarget(closest))
        {
            return closest;
        }

        return _world.SelfId;
    }

    private bool IsEnemySplashSkill(string skillId)
    {
        if (_skillHud.TryGetValue(skillId, out var hud) &&
            hud.AoeOrigin == "target" &&
            hud.Affects == "hostile")
        {
            return true;
        }

        return skillId == "target_burst";
    }

    private void RefreshTargetAoeAim(string skillId, string targetId)
    {
        if (_world == null || !IsEnemySplashSkill(skillId))
        {
            return;
        }

        if (!_world.TryGetMapXY(_world.SelfId, out var sx, out var sy) ||
            !_world.TryGetMapXY(targetId, out var tx, out var ty))
        {
            return;
        }

        var radius = 2.5f;
        if (_skillHud.TryGetValue(skillId, out var hud) && hud.AoeRadius > 0.1f)
        {
            radius = hud.AoeRadius;
        }

        _world.UpdateGroundAim(
            WorldCoords.TileToWorld(sx, sy),
            WorldCoords.TileToWorld(tx, ty),
            ResolveSkillRange(skillId), radius);
    }

    private static float SkillProjectileSpeed(string skillId)
    {
        if (skillId == "shot")
        {
            return 16f;
        }

        if (skillId == "stun_bolt")
        {
            return 14f;
        }

        return 14f;
    }

    private void SpawnShotVisual(string skillId, float dx, float dy)
    {
        if (_world == null)
        {
            return;
        }

        var from = _world.GetEntityWorldPos(_world.SelfId);
        if (!from.HasValue)
        {
            return;
        }

        _world.SpawnLocalSkillshot(skillId, from.Value, dx, dy, SkillProjectileSpeed(skillId));
    }

    private void TryCastOrChase(string skillId, string targetId)
    {
        var range = ResolveSkillRange(skillId);
        if (BestRangeGap(targetId) > range + 0.08f)
        {
            BeginPendingSkill(skillId, targetId, range);
            return;
        }

        ExecuteCastNow(skillId, targetId);
    }

    private float BestRangeGap(string targetId)
    {
        if (_world == null || string.IsNullOrEmpty(targetId))
        {
            return float.MaxValue;
        }

        var ack = _world.RangeGapTo(targetId, _lastGoodX, _lastGoodY);
        var pred = _world.RangeGapTo(targetId, _x, _y);
        var vis = _world.RangeGapTo(targetId);
        return Mathf.Min(ack, Mathf.Min(pred, vis));
    }

    private void ExecuteCastNow(string skillId, string targetId)
    {
        if (_input == null || _world == null)
        {
            return;
        }

        _world.SetLockTarget(targetId);
        _input.SetTarget(targetId);

        var range = ResolveSkillRange(skillId);
        var gapAck = _world.RangeGapTo(targetId, _lastGoodX, _lastGoodY);
        var gapPred = _world.RangeGapTo(targetId, _x, _y);
        // Only snap back when last-good is in range and the predicted sprite drifted out.
        if (gapPred > range + 0.08f && gapAck <= range + 0.08f)
        {
            _x = _lastGoodX;
            _y = _lastGoodY;
            _world.SetLocalPos(_x, _y, instant: true);
            _input.RequestMove(_x, _y);
        }
        else
        {
            _world.SetLocalPos(_x, _y, instant: true);
        }

        _world.PlayCastAttack(_world.SelfId, targetId, skillId);
        BeginLocalCast(skillId);
        if (IsEnemySplashSkill(skillId))
        {
            _world.ClearAimIndicator();
        }

        var reqId = _prediction.NextRequestId("cast");
        _lastCastRequestId = reqId;
        _lastCastSkillId = skillId;
        var predictedHp = 0;
        var predictHp = skillId != "shot" && skillId != "stun_bolt";
        if (predictHp && _world.TryGetTargetInfo(out var info) && info.Id == targetId)
        {
            predictedHp = Mathf.Max(1, info.Hp - 8);
            _world.ApplyReconcileHp(targetId, predictedHp, false);
        }

        _prediction.Predict(new PredictionReconciler.PredictedAction
        {
            RequestId = reqId,
            Kind = skillId,
            TargetId = targetId,
            PredictedHpAfter = predictedHp,
            PredictedX = 0f,
            PredictedY = 0f,
        });

        if (IndicatorSkills.TryGetValue(skillId, out var def))
        {
            var selfPos = _world.GetEntityWorldPos(_world.SelfId);
            var tgtPos = _world.GetEntityWorldPos(targetId);
            if (!selfPos.HasValue)
            {
                _input.Cast(skillId, targetId);
                return;
            }

            var adx = 1f;
            var ady = 0f;
            if (tgtPos.HasValue)
            {
                adx = tgtPos.Value.x - selfPos.Value.x;
                ady = tgtPos.Value.z - selfPos.Value.z;
                var len = Mathf.Sqrt(adx * adx + ady * ady);
                if (len < 1e-4f)
                {
                    adx = 1f;
                    ady = 0f;
                }
                else
                {
                    adx /= len;
                    ady /= len;
                }
            }

            CancelAim();
            if (def.Kind == AimKind.Ground)
            {
                if (tgtPos.HasValue)
                {
                    var ground = WorldCoords.WorldToTile(tgtPos.Value);
                    _input.Cast(skillId, targetId, null, null, ground.x, ground.y);
                }
                else
                {
                    _input.Cast(skillId, targetId);
                }
            }
            else
            {
                _input.Cast(skillId, targetId, adx, ady);
                SpawnShotVisual(skillId, adx, ady);
            }

            GameLog.CastLocal(skillId, targetId, "indicator");
            _status = skillId + " → " + targetId;
            return;
        }

        _input.Cast(skillId, targetId);
        GameLog.CastLocal(skillId, targetId);
        _status = skillId + " → " + targetId;
    }

    /// <summary>Cast skillshot/AoE toward current lock (or closest). Returns false if no target.</summary>
    private bool TryCastIndicatorAtLock(string skillId)
    {
        if (_world == null || _input == null || !IndicatorSkills.ContainsKey(skillId))
        {
            return false;
        }

        var lockId = _world.LockTargetId;
        if (string.IsNullOrEmpty(lockId) || lockId == _world.SelfId || !_world.IsLivingTarget(lockId) ||
            _world.IsPlayerEntity(lockId))
        {
            lockId = _world.LockClosestEnemy();
        }

        if (string.IsNullOrEmpty(lockId) || !_world.IsLivingTarget(lockId))
        {
            return false;
        }

        _world.SetLockTarget(lockId);
        TryCastOrChase(skillId, lockId);
        return true;
    }

    private void TryWorldTargetClicks()
    {
        if (_world == null || _showSettings || _showGacha || _showCtxMenu || _showInspect || _showFullMap || _chatFocused ||
            _showNpcDialog || _showShopBuy || _showShopSell || _showClassService || _showEnhance ||
            !string.IsNullOrEmpty(_aimSkillId))
        {
            return;
        }

        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
        {
            return;
        }

        var screen = mouse.position.ReadValue();
        var gui = ScreenToGui(screen);
        if (_minimapArea.width > 1f && _minimapArea.Contains(gui))
        {
            return;
        }

        // Ignore HUD / inventory panel clicks (inventory open must not block the whole map).
        if (gui.y > GuiH - 140f)
        {
            return;
        }

        if (_showInventory && InventoryWindowRect().Contains(gui))
        {
            return;
        }

        var cam = Camera.main;
        if (cam == null || !TryScreenToMap(cam, screen, out var mx, out var my))
        {
            return;
        }

        // Click a gate → use it.
        var portalId = _world.PickPortalNear(mx, my, 1.1f);
        if (!string.IsNullOrEmpty(portalId))
        {
            _input?.RequestPortal(portalId);
            _status = "portal " + portalId;
            return;
        }

        var id = _world.PickCombatTargetNear(mx, my, 1.25f);
        if (!string.IsNullOrEmpty(id))
        {
            _world.SetLockTarget(id);
            _input?.SetTarget(id);
            _status = "lock-on " + id;
            return;
        }

        var npcId = _world.PickNpcNear(mx, my, 1.25f);
        if (!string.IsNullOrEmpty(npcId))
        {
            _world.SetLockTarget(npcId);
            _status = "lock-on " + npcId;
        }

        // Empty LMB: keep current target (no clear / no move).
    }

    private void TryWorldMoveClicks()
    {
        if (_world == null || _input == null || _showSettings || _showGacha || _showCtxMenu || _showInspect || _chatFocused ||
            _showNpcDialog || _showShopBuy || _showShopSell || _showClassService || _showEnhance ||
            !string.IsNullOrEmpty(_aimSkillId))
        {
            return;
        }

        var mouse = Mouse.current;
        if (mouse == null || !mouse.rightButton.wasPressedThisFrame)
        {
            return;
        }

        var screen = mouse.position.ReadValue();
        var gui = ScreenToGui(screen);

        if (_showFullMap)
        {
            TryRouteFromMapPixel(gui, _fullMapArea, false, _fullMapCell);
            return;
        }

        if (_minimapArea.width > 1f && _minimapArea.Contains(gui))
        {
            TryRouteFromMapPixel(gui, _minimapArea, true, _minimapCell);
            return;
        }

        if (gui.y > GuiH - 140f)
        {
            return;
        }

        if (_showInventory && InventoryWindowRect().Contains(gui))
        {
            return;
        }

        var cam = Camera.main;
        if (cam == null || !TryScreenToMap(cam, screen, out var mx, out var my))
        {
            return;
        }

        var combat = _world.PickCombatTargetNear(mx, my, 1.25f);
        var npcId = _world.PickNpcNear(mx, my, 1.25f);
        if (!string.IsNullOrEmpty(npcId) && string.IsNullOrEmpty(combat))
        {
            StartNpcTalk(npcId);
            return;
        }

        _pendingTalkId = "";
        StartClickRoute(mx, my);
    }

    private void TickMinimapZoom()
    {
        if (_world == null || !_inWorld || _gatePhase < 3 || _showFullMap)
        {
            return;
        }

        var mouse = Mouse.current;
        if (mouse == null)
        {
            return;
        }

        var gui = ScreenToGui(mouse.position.ReadValue());
        if (_minimapArea.width < 1f || !_minimapArea.Contains(gui))
        {
            return;
        }

        var scroll = mouse.scroll.y.ReadValue();
        if (Mathf.Abs(scroll) < 0.01f)
        {
            return;
        }

        var maxR = Mathf.Max(12, Mathf.Max(_world.MapWidth, _world.MapHeight));
        var next = _minimapRadius + (scroll > 0f ? -3 : 3);
        _minimapRadius = Mathf.Clamp(next, 8, maxR);
    }

    private void StartClickRoute(float mx, float my)
    {
        if (IsCasting)
        {
            _clickMoveActive = true;
            BuildRoute(mx, my, true);
            return;
        }

        ClearPendingSkill(false);
        _clickMoveActive = true;
        BuildRoute(mx, my, true);
    }

    private bool TryRouteFromMapPixel(Vector2 gui, Rect area, bool followPlayer, float cell)
    {
        if (cell < 0.1f || !area.Contains(gui))
        {
            return false;
        }

        var cols = Mathf.Max(1, Mathf.RoundToInt(area.width / cell));
        var rows = Mathf.Max(1, Mathf.RoundToInt(area.height / cell));
        var gx = Mathf.FloorToInt((gui.x - area.x) / cell);
        var gy = Mathf.FloorToInt((gui.y - area.y) / cell);
        gx = Mathf.Clamp(gx, 0, cols - 1);
        gy = Mathf.Clamp(gy, 0, rows - 1);

        int tx;
        int ty;
        if (followPlayer)
        {
            var px = MapPathing.TileOf(_x);
            var py = MapPathing.TileOf(_y);
            tx = px + gx - cols / 2;
            ty = py + (rows / 2 - gy);
        }
        else
        {
            tx = gx;
            ty = rows - 1 - gy;
        }

        StartClickRoute(tx, ty);
        return true;
    }

    private static bool TryScreenToMap(Camera cam, Vector2 screenPx, out float mapX, out float mapY)
    {
        mapX = 0f;
        mapY = 0f;
        var ray = cam.ScreenPointToRay(screenPx);
        var plane = new Plane(Vector3.up, Vector3.zero);
        if (!plane.Raycast(ray, out var enter))
        {
            return false;
        }

        var p = ray.GetPoint(enter);
        mapX = p.x;
        mapY = p.z;
        return true;
    }

    private float _portalCooldownUntil;

    private void TryAutoPortal()
    {
        if (_input == null || _world == null || _portalIds.Count == 0 || Time.time < _portalCooldownUntil)
        {
            return;
        }

        var best = -1;
        var bestD = 1.55f;
        for (var i = 0; i < _portalIds.Count; i++)
        {
            var d = Vector2.Distance(new Vector2(_x, _y), new Vector2(_portalXs[i], _portalYs[i]));
            if (d < bestD)
            {
                bestD = d;
                best = i;
            }
        }

        if (best < 0)
        {
            return;
        }

        _portalCooldownUntil = Time.time + 1.2f;
        _input.RequestPortal(_portalIds[best]);
        _status = "gate " + _portalIds[best];
    }

    private void BeginOrConfirmAim(string skillId)
    {
        if (TryCastIndicatorAtLock(skillId))
        {
            return;
        }

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
            var tile = WorldCoords.WorldToTile(selfPos.Value);
            _aimX = tile.x + 1f;
            _aimY = tile.y;
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
        _world.PlayCastAttack(_world.SelfId, lockId, skillId);
        BeginLocalCast(skillId);
        if (def.Kind == AimKind.Ground)
        {
            _input.Cast(skillId, lockId, null, null, _aimX, _aimY);
        }
        else
        {
            _input.Cast(skillId, lockId, _aimDx, _aimDy);
            SpawnShotVisual(skillId, _aimDx, _aimDy);
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

        var casterPos = _world.GetEntityWorldPos(_world.SelfId) ?? WorldCoords.TileToWorld(_x, _y);
        var mouseWorld = ReadMouseWorld();
        var dir = WorldCoords.MapXZ(mouseWorld) - WorldCoords.MapXZ(casterPos);
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
            var point = WorldCoords.OnGround(mouseWorld);
            var flat = WorldCoords.MapXZ(point) - WorldCoords.MapXZ(casterPos);
            if (flat.magnitude > def.Range)
            {
                flat = flat.normalized * def.Range;
            }

            _aimX = casterPos.x + flat.x;
            _aimY = casterPos.z + flat.y;
            _world.UpdateGroundAim(casterPos, WorldCoords.TileToWorld(_aimX, _aimY), def.Range, def.AoeRadius);
        }
    }

    private Vector3 ReadMouseWorld()
    {
        var cam = Camera.main;
        var mouse = Mouse.current;
        if (cam == null || mouse == null)
        {
            return WorldCoords.TileToWorld(_x + _aimDx, _y + _aimDy);
        }

        var screen = mouse.position.ReadValue();
        if (TryScreenToMap(cam, screen, out var mx, out var my))
        {
            return WorldCoords.TileToWorld(mx, my);
        }

        return WorldCoords.TileToWorld(_x + _aimDx, _y + _aimDy);
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

    private bool IsAimHotkeyPressed(Keyboard kb, string skillId)
    {
        var idx = HotbarIndexOf(skillId);
        if (idx == 0)
        {
            return kb.spaceKey.wasPressedThisFrame;
        }

        return idx > 0 && DigitPressedThisFrame(kb) == idx;
    }

    private bool IsAimHotkeyDown(Keyboard kb, string skillId)
    {
        var idx = HotbarIndexOf(skillId);
        if (idx == 0)
        {
            return kb.spaceKey.isPressed;
        }

        return idx > 0 && DigitIsPressed(kb, idx);
    }

    private bool IsAimHotkeyReleased(Keyboard kb, string skillId)
    {
        var idx = HotbarIndexOf(skillId);
        if (idx == 0)
        {
            return kb.spaceKey.wasReleasedThisFrame;
        }

        return idx > 0 && DigitWasReleased(kb, idx);
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
        var gui = ScreenToGui(p); var guiY = gui.y;
        var count = _skillIds != null ? Mathf.Min(_skillIds.Length, 8) : 0;
        for (var i = 0; i < count; i++)
        {
            var rect = SkillSlotRect(i);
            if (!rect.Contains(new Vector2(gui.x, guiY)))
            {
                continue;
            }

            var skillId = _skillIds[i];
            if (!string.IsNullOrEmpty(_aimSkillId))
            {
                return;
            }

            CastSkill(skillId);

            break;
        }

        var restRect = SkillSlotRect(Mathf.Min(_skillIds != null ? _skillIds.Length : 8, 8));
        if (restRect.Contains(new Vector2(gui.x, guiY)))
        {
            CastSkill("rest");
        }
    }

    private void TryContextMenuOpen()
    {
        // Context menu: middle-click entity (RMB is click-to-move).
        var mouse = Mouse.current;
        if (mouse == null || !mouse.middleButton.wasPressedThisFrame)
        {
            return;
        }

        var cam = Camera.main;
        if (cam == null || _world == null)
        {
            return;
        }

        var screen = mouse.position.ReadValue();
        if (!TryScreenToMap(cam, screen, out var mx, out var my))
        {
            return;
        }

        var id = _world.PickEntityNear(mx, my, 1.1f);
        if (string.IsNullOrEmpty(id) || id == _world.SelfId)
        {
            _showCtxMenu = false;
            return;
        }

        OpenContextMenuAt(id, screen);
    }

    private void OpenContextMenuAt(string id, Vector2 screen)
    {
        if (_world == null || string.IsNullOrEmpty(id))
        {
            return;
        }

        _ctxTargetId = id;
        _ctxTargetKind = InferKind(id);
        _ctxTargetLabel = id;
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

        _ctxMenuScreen = ScreenToGui(screen);
        _showCtxMenu = true;
        _status = "menu " + _ctxTargetLabel;
    }

    private static string InferKind(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return "monster";
        }

        if (id.StartsWith("npc_") || id.Contains("npc_"))
        {
            return "npc";
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
        var gui = ScreenToGui(p);
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
            return new[] { "Target", "Attack", "Inspect", "Whisper", "Invite party", "Trade", "Add friend", "Invite guild" };
        }

        if (_ctxTargetKind == "npc")
        {
            return new[] { "Target", "Talk", "Inspect" };
        }

        return new[] { "Target", "Attack", "Inspect" };
    }

    private Rect CtxMenuRect(int itemCount)
    {
        var w = 140f;
        var h = 8f + itemCount * 24f;
        var x = Mathf.Clamp(_ctxMenuScreen.x, 4f, GuiW - w - 4f);
        var y = Mathf.Clamp(_ctxMenuScreen.y, 4f, GuiH - h - 4f);
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
            TryCastOrChase("auto_attack", _ctxTargetId);
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
            return;
        }

        if (action == "Trade")
        {
            _input?.RequestTradeInvite(_ctxTargetId);
            _status = "trade invite " + _ctxTargetId;
            return;
        }

        if (action == "Add friend")
        {
            _input?.RequestFriendAdd(_ctxTargetId);
            _status = "friend add " + _ctxTargetId;
            return;
        }

        if (action == "Invite guild")
        {
            _input?.RequestGuildInvite(_ctxTargetId);
            _status = "guild invite " + _ctxTargetId;
            return;
        }

        if (action == "Talk")
        {
            StartNpcTalk(_ctxTargetId);
            _status = "talk " + _ctxTargetId;
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
        _weaponRange = WeaponRangeOf(_weapon);
        _weapon2Range = WeaponRangeOf(_weapon2);
        if (_spirit == "none" || string.IsNullOrEmpty(_spirit))
        {
            _element = _weapon.Contains("staff") ? "holy"
                : _weapon.Contains("bow") ? "water"
                : _weapon.Contains("gun") ? "fire"
                : _weapon.Contains("dagger") ? "wind"
                : "earth";
        }

        _world?.SetHeldWeapon(_weapon);
    }

    private static float WeaponRangeOf(string weaponId)
    {
        if (string.IsNullOrEmpty(weaponId) || weaponId == "none")
        {
            return 1.5f;
        }

        if (weaponId.Contains("bow") || weaponId.Contains("charm"))
        {
            return 5f;
        }

        if (weaponId.Contains("staff") || weaponId.Contains("orb"))
        {
            return 4.5f;
        }

        if (weaponId.Contains("gun"))
        {
            return 4f;
        }

        if (weaponId.Contains("tome"))
        {
            return 4.2f;
        }

        if (weaponId.Contains("dagger"))
        {
            return 1.2f;
        }

        return 1.5f;
    }

    private void TryEquipKey(Keyboard kb)
    {
        var keys = new[] { kb.xKey, kb.cKey, kb.vKey, kb.bKey };
        for (var i = 0; i < keys.Length && i + 1 < _weapons.Length; i++)
        {
            if (!keys[i].wasPressedThisFrame)
            {
                continue;
            }

            _weapon = _weapons[i + 1];
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
        while (slot < InvSize && cursor < part.Length)
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
            var index = si >= 0 && si < InvSize ? si : slot;
            _invSlots[index] = string.IsNullOrEmpty(itemId) ? null : itemId;
            _invQty[index] = qty;
            cursor = idToken + 9;
            slot += 1;
        }

        var skinId = JsonUtil.ExtractString(json, "equippedSkinId");
        if (!string.IsNullOrEmpty(skinId) && skinId != "null")
        {
            _skin = skinId;
        }
    }

    private void ApplyGearIdsFromJson(string json)
    {
        SetGearField(json, "equippedArmorId", "armorId", ref _armor);
        SetGearField(json, "equippedHelmId", "helmId", ref _helm);
        SetGearField(json, "equippedBootsId", "bootsId", ref _boots);
        SetGearField(json, "equippedGlovesId", "glovesId", ref _gloves);
        SetGearField(json, "equippedAmuletId", "amuletId", ref _amulet);
        if (_amulet == "none" || string.IsNullOrEmpty(_amulet))
        {
            SetGearField(json, "equippedAccessoryId", "accessoryId", ref _amulet);
        }

        _accessory = _amulet;
        SetGearField(json, "equippedRing1Id", "ring1Id", ref _ring1);
        SetGearField(json, "equippedRing2Id", "ring2Id", ref _ring2);
        SetGearField(json, "equippedSubclassId", "subclassId", ref _subclass);
        ParseEnhanceLevels(json);
    }

    private void ParseEnhanceLevels(string json)
    {
        _enhanceLevels.Clear();
        var idx = json.IndexOf("\"enhanceLevels\"");
        if (idx < 0)
        {
            return;
        }

        var slice = json.Substring(idx, Mathf.Min(400, json.Length - idx));
        var keys = new[] { "mainhand", "armor", "helm", "boots", "gloves", "amulet", "ring1", "ring2", "subclass" };
        for (var i = 0; i < keys.Length; i++)
        {
            var token = "\"" + keys[i] + "\":";
            var at = slice.IndexOf(token);
            if (at < 0)
            {
                continue;
            }

            var nStart = at + token.Length;
            var n = 0;
            var digits = "";
            while (nStart + digits.Length < slice.Length && char.IsDigit(slice[nStart + digits.Length]))
            {
                digits += slice[nStart + digits.Length];
            }

            if (int.TryParse(digits, out n))
            {
                _enhanceLevels[keys[i]] = n;
            }
        }
    }

    private static void SetGearField(string json, string longKey, string shortKey, ref string field)
    {
        var v = JsonUtil.ExtractString(json, longKey);
        if (string.IsNullOrEmpty(v))
        {
            v = JsonUtil.ExtractString(json, shortKey);
        }

        if (string.IsNullOrEmpty(v) || v == "null")
        {
            if (json.Contains("\"" + longKey + "\":null") || json.Contains("\"" + shortKey + "\":null"))
            {
                field = "none";
            }

            return;
        }

        field = v;
    }

    private Rect InventoryWindowRect()
    {
        const float charW = 220f;
        const float slot = 28f;
        const float gap = 2f;
        var gridW = InvCols * (slot + gap);
        var gridH = InvCols * (slot + gap);
        var w = 16f + charW + 12f + gridW + 16f;
        var h = 36f + Mathf.Max(gridH, 420f) + 16f;
        if (!_invPosReady)
        {
            _invPanelPos = new Vector2(GuiW - w - 16f, 90f);
            _invPosReady = true;
        }

        return new Rect(_invPanelPos.x, _invPanelPos.y, w, h);
    }

    private void DrawInventory()
    {
        if (!_showInventory)
        {
            _hoverItemTip = "";
            if (_itemDragging)
            {
                CancelItemDrag();
            }

            return;
        }

        const float charW = 220f;
        const float slot = 28f;
        const float gap = 2f;
        var win = InventoryWindowRect();
        UiChrome.Panel(win, UiChrome.Gold);

        var title = new Rect(win.x, win.y, win.width, 28f);
        GUI.Label(title,
            string.IsNullOrEmpty(_tradeId)
                ? "  Inventory (I) — drag title · drag items · RMB equip/unequip"
                : "  Inventory — LMB add to TRADE");
        HandleInvDrag(title, new Rect(0, 0, 0, 0));

        var charRect = new Rect(win.x + 10f, win.y + 34f, charW, win.height - 46f);
        GUI.color = new Color(0.12f, 0.14f, 0.18f, 0.95f);
        GUI.DrawTexture(charRect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(charRect.x + 8, charRect.y + 6, charW - 16, 20), _playerLabel);
        GUI.Label(new Rect(charRect.x + 8, charRect.y + 26, charW - 16, 54),
            ClassPretty(_classId) + "  Lv " + _level + "\nHP " + _hp + "/" + _maxHp + "  MP " + _mp + "/" + _maxMp +
            "\nGold " + _gold);

        GUI.Label(new Rect(charRect.x + 8, charRect.y + 82, charW - 16, 16), "Assume class (debug)");
        var btnW = (charW - 20f) / 5f;
        for (var c = 0; c < DebugClassIds.Length; c++)
        {
            var br = new Rect(charRect.x + 8 + c * btnW, charRect.y + 100, btnW - 2f, 22f);
            var on = _classId == DebugClassIds[c];
            GUI.color = on ? new Color(0.85f, 0.72f, 0.28f) : Color.white;
            if (GUI.Button(br, DebugClassLabels[c]))
            {
                _debugClassIndex = c;
                _input?.RequestDebugSetClass(DebugClassIds[c]);
                _status = "class → " + ClassPretty(DebugClassIds[c]);
            }

            GUI.color = Color.white;
        }

        GUI.Label(new Rect(charRect.x + 8, charRect.y + 126, 70f, 16), "Level");
        if (GUI.Button(new Rect(charRect.x + 78, charRect.y + 124, 50f, 20f), "L20"))
        {
            _input?.RequestDebugSetLevel(20);
            _status = "debug level → 20";
        }

        if (GUI.Button(new Rect(charRect.x + 132, charRect.y + 124, 50f, 20f), "L30"))
        {
            _input?.RequestDebugSetLevel(30);
            _status = "debug level → 30";
        }

        GUI.Label(new Rect(charRect.x + 8, charRect.y + 148, charW - 16, 16), "Equipment");
        DrawPaperdoll(charRect);

        var startX = win.x + 10f + charW + 12f;
        var startY = win.y + 34f;
        _hoverItemTip = "";
        var pointer = GuiPointer();
        for (var i = 0; i < InvSize; i++)
        {
            var col = i % InvCols;
            var row = i / InvCols;
            var rect = new Rect(startX + col * (slot + gap), startY + row * (slot + gap), slot, slot);
            var id = _invSlots[i];
            UiChrome.Slot(rect, UiChrome.Rarity(id));
            GUI.color = Color.white;
            if (!string.IsNullOrEmpty(id) && !(_itemDragging && _itemDragFrom == i))
            {
                var iconTint = ItemLevelReq(id) > _level
                    ? new Color(0.4f, 0.4f, 0.42f, 1f)
                    : Color.white;
                SpriteCatalog.DrawGui(
                    new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, rect.height - 4f),
                    SpriteCatalog.ItemIcon(id),
                    iconTint);
                if (_invQty[i] > 1)
                {
                    GUI.Label(new Rect(rect.x + 1, rect.y + rect.height - 14, rect.width - 2, 14), "x" + _invQty[i]);
                }
            }

            if (rect.Contains(pointer) && !string.IsNullOrEmpty(id))
            {
                _hoverItemTip = ItemDetailText(id, _invQty[i]);
            }
        }

        HandleInventoryEvents(win, charRect, startX, startY, slot, gap, pointer);

        if (_itemDragging && !string.IsNullOrEmpty(_itemDragId))
        {
            var ghost = new Rect(pointer.x - 14f, pointer.y - 14f, 28f, 28f);
            UiChrome.Slot(ghost, UiChrome.Rarity(_itemDragId));
            SpriteCatalog.DrawGui(
                new Rect(ghost.x + 2f, ghost.y + 2f, 24f, 24f),
                SpriteCatalog.ItemIcon(_itemDragId),
                Color.white);
        }
    }

    private void DrawPaperdoll(Rect charRect)
    {
        var labels = new[]
        {
            "Mainhand", "Offhand", "Body", "Helm", "Boots", "Gloves", "Amulet", "Ring A", "Ring B", "Fairy", "Spec",
        };
        var keys = new[]
        {
            "mainhand", "offhand", "armor", "helm", "boots", "gloves", "amulet", "ring1", "ring2", "spirit", "subclass",
        };
        var ids = new[]
        {
            EquipId(_weapon), EquipId(_weapon2), EquipId(_armor), EquipId(_helm),
            EquipId(_boots), EquipId(_gloves), EquipId(_amulet), EquipId(_ring1), EquipId(_ring2),
            EquipId(_spirit), EquipId(_subclass),
        };
        const float slot = 28f;
        var pointer = GuiPointer();
        for (var i = 0; i < keys.Length; i++)
        {
            var col = i % 2;
            var row = i / 2;
            var x = charRect.x + 8 + col * 104f;
            var y = charRect.y + 168 + row * 38f;
            GUI.Label(new Rect(x, y, 70f, 14f), labels[i]);
            var rect = new Rect(x + 70f, y, slot, slot);
            var id = ids[i];
            UiChrome.Slot(rect, UiChrome.Rarity(id));
            if (!string.IsNullOrEmpty(id) && !(_itemDragging && _itemDragEquip == keys[i]))
            {
                SpriteCatalog.DrawGui(
                    new Rect(rect.x + 2f, rect.y + 2f, 24f, 24f),
                    SpriteCatalog.ItemIcon(id),
                    Color.white);
            }

            if (rect.Contains(pointer) && !string.IsNullOrEmpty(id))
            {
                _hoverItemTip = ItemDetailText(id, 1);
            }
        }
    }

    private static string EquipId(string id)
    {
        return string.IsNullOrEmpty(id) || id == "none" || id == "null" ? "" : id;
    }

    private static string ClassPretty(string id)
    {
        return id switch
        {
            "fighter" => "Fighter",
            "marksman" => "Archer",
            "mage" => "Mage",
            "rogue" => "Rogue",
            _ => "Adventurer",
        };
    }

    private Rect PaperdollSlotRect(Rect charRect, string key)
    {
        var keys = new[]
        {
            "mainhand", "offhand", "armor", "helm", "boots", "gloves", "amulet", "ring1", "ring2", "spirit", "subclass",
        };
        const float slot = 28f;
        for (var i = 0; i < keys.Length; i++)
        {
            if (keys[i] != key)
            {
                continue;
            }

            var col = i % 2;
            var row = i / 2;
            var x = charRect.x + 8 + col * 104f + 70f;
            var y = charRect.y + 168 + row * 38f;
            return new Rect(x, y, slot, slot);
        }

        return new Rect(0, 0, 0, 0);
    }

    private string PaperdollKeyAt(Rect charRect, Vector2 pointer)
    {
        var keys = new[]
        {
            "mainhand", "offhand", "armor", "helm", "boots", "gloves", "amulet", "ring1", "ring2", "spirit", "subclass",
        };
        for (var i = 0; i < keys.Length; i++)
        {
            if (PaperdollSlotRect(charRect, keys[i]).Contains(pointer))
            {
                return keys[i];
            }
        }

        return "";
    }

    private string PaperdollId(string key)
    {
        return key switch
        {
            "mainhand" => EquipId(_weapon),
            "offhand" => EquipId(_weapon2),
            "armor" => EquipId(_armor),
            "helm" => EquipId(_helm),
            "boots" => EquipId(_boots),
            "gloves" => EquipId(_gloves),
            "amulet" => EquipId(_amulet),
            "ring1" => EquipId(_ring1),
            "ring2" => EquipId(_ring2),
            "spirit" => EquipId(_spirit),
            "subclass" => EquipId(_subclass),
            _ => "",
        };
    }

    private void HandleInventoryEvents(Rect win, Rect charRect, float startX, float startY, float slot, float gap, Vector2 pointer)
    {
        var e = Event.current;
        if (e == null || !string.IsNullOrEmpty(_tradeId))
        {
            return;
        }

        var title = new Rect(win.x, win.y, win.width, 28f);
        if (title.Contains(pointer) && e.type == EventType.MouseDown)
        {
            return;
        }

        var bagIndex = -1;
        for (var i = 0; i < InvSize; i++)
        {
            var col = i % InvCols;
            var row = i / InvCols;
            var rect = new Rect(startX + col * (slot + gap), startY + row * (slot + gap), slot, slot);
            if (rect.Contains(pointer))
            {
                bagIndex = i;
                break;
            }
        }

        var eqKey = PaperdollKeyAt(charRect, pointer);

        if (e.type == EventType.MouseDown && e.button == 1)
        {
            if (bagIndex >= 0 && !string.IsNullOrEmpty(_invSlots[bagIndex]))
            {
                EquipOrUseBagItem(bagIndex, _invSlots[bagIndex]);
                e.Use();
            }
            else if (!string.IsNullOrEmpty(eqKey) && !string.IsNullOrEmpty(PaperdollId(eqKey)))
            {
                UnequipPaperdoll(eqKey);
                e.Use();
            }

            return;
        }

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            if (bagIndex >= 0 && !string.IsNullOrEmpty(_invSlots[bagIndex]))
            {
                _itemDragging = true;
                _itemDragFrom = bagIndex;
                _itemDragEquip = "";
                _itemDragId = _invSlots[bagIndex];
                _itemDragQty = _invQty[bagIndex];
                e.Use();
            }
            else if (!string.IsNullOrEmpty(eqKey) && !string.IsNullOrEmpty(PaperdollId(eqKey)))
            {
                _itemDragging = true;
                _itemDragFrom = -1;
                _itemDragEquip = eqKey;
                _itemDragId = PaperdollId(eqKey);
                _itemDragQty = 1;
                e.Use();
            }

            return;
        }

        if (_itemDragging && e.type == EventType.MouseUp && e.button == 0)
        {
            DropItemDrag(bagIndex, eqKey);
            e.Use();
        }
    }

    private void DropItemDrag(int bagIndex, string eqKey)
    {
        var fromBag = _itemDragFrom;
        var fromEq = _itemDragEquip;
        var id = _itemDragId;
        CancelItemDrag();
        if (string.IsNullOrEmpty(id) || _input == null)
        {
            return;
        }

        if (bagIndex >= 0)
        {
            if (fromBag >= 0)
            {
                _input.RequestSwapInventory(fromBag, bagIndex);
                _status = "move slot";
                return;
            }

            if (!string.IsNullOrEmpty(fromEq))
            {
                UnequipPaperdoll(fromEq);
                return;
            }
        }

        if (!string.IsNullOrEmpty(eqKey))
        {
            TryEquipInto(eqKey, id, fromBag);
        }
    }

    private void CancelItemDrag()
    {
        _itemDragging = false;
        _itemDragFrom = -1;
        _itemDragEquip = "";
        _itemDragId = "";
        _itemDragQty = 1;
    }

    private void TryEquipInto(string slotKey, string id, int fromBag)
    {
        if (string.IsNullOrEmpty(id) || _input == null)
        {
            return;
        }

        var why = EquipRejectReason(slotKey, id);
        if (!string.IsNullOrEmpty(why))
        {
            _status = why;
            _comingSoonToast = why;
            _comingSoonUntil = Time.time + 2.2f;
            return;
        }

        switch (slotKey)
        {
            case "mainhand":
                _input.EquipWeapon(id);
                break;
            case "offhand":
                _input.EquipOffhand(id);
                break;
            case "spirit":
                _input.EquipSpirit(id);
                break;
            case "armor":
            case "helm":
            case "boots":
            case "gloves":
            case "amulet":
            case "ring1":
            case "ring2":
            case "subclass":
                _input.RequestEquipGear(slotKey, id);
                break;
        }

        _status = "equip " + ShortInv(id);
    }

    private void UnequipPaperdoll(string slotKey)
    {
        if (_input == null)
        {
            return;
        }

        switch (slotKey)
        {
            case "mainhand":
                _input.EquipWeapon(null);
                break;
            case "offhand":
                _input.EquipOffhand(null);
                break;
            case "spirit":
                _input.EquipSpirit(null);
                break;
            default:
                _input.RequestEquipGear(slotKey, null);
                break;
        }

        _status = "unequip " + slotKey;
    }

    private void EquipOrUseBagItem(int bagIndex, string id)
    {
        if (string.IsNullOrEmpty(id) || _input == null)
        {
            return;
        }

        if (id.StartsWith("item_") || id.StartsWith("card_"))
        {
            _input.RequestUseItem(bagIndex);
            _status = "use " + ShortInv(id);
            return;
        }

        var slot = GuessEquipSlot(id);
        if (string.IsNullOrEmpty(slot))
        {
            _input.RequestUseItem(bagIndex);
            return;
        }

        TryEquipInto(slot, id, bagIndex);
    }

    private static string GuessEquipSlot(string id)
    {
        if (id.StartsWith("spirit_")) return "spirit";
        if (id.StartsWith("subclass_")) return "subclass";
        if (id.StartsWith("armor_")) return "armor";
        if (id.StartsWith("helm_")) return "helm";
        if (id.StartsWith("boots_")) return "boots";
        if (id.StartsWith("gloves_")) return "gloves";
        if (id.StartsWith("acc_amulet")) return "amulet";
        if (id.StartsWith("acc_ring2")) return "ring2";
        if (id.StartsWith("acc_ring") || id == "acc_iron" || id == "acc_ash") return "ring1";
        if (id.StartsWith("acc_")) return "amulet";
        if (id.Contains("charm") || id.Contains("orb") || id.Contains("_off")) return "offhand";
        if (id.Contains("sword") || id.Contains("dagger") || id.Contains("staff") || id.Contains("bow")
            || id.Contains("gun") || id.Contains("tome"))
        {
            return "mainhand";
        }

        return "";
    }

    private string EquipRejectReason(string slotKey, string id)
    {
        var cls = _classId ?? "adventurer";
        if (cls == "adventurer")
        {
            return "";
        }

        if (slotKey == "mainhand")
        {
            if (cls == "fighter" && (id.Contains("staff") || id.Contains("bow") || id.Contains("tome") || id.Contains("gun")))
            {
                return "Fighter cannot equip that mainhand";
            }

            if (cls == "marksman" && !id.Contains("bow"))
            {
                return "Archer mainhand needs a bow";
            }

            if (cls == "mage" && !id.Contains("staff") && !id.Contains("tome"))
            {
                return "Mage mainhand needs a staff or tome";
            }

            if (cls == "rogue" && id.Contains("zwei"))
            {
                return "Rogue cannot wield a two-handed sword";
            }
        }

        if (slotKey == "offhand")
        {
            if ((_weapon ?? "").Contains("zwei"))
            {
                return "Two-handed mainhand blocks offhand";
            }

            if (cls == "marksman" && !id.Contains("charm"))
            {
                return "Archer offhand needs a charm";
            }

            if (cls == "mage" && !id.Contains("orb"))
            {
                return "Mage offhand needs an orb";
            }

            if (cls == "rogue" && !id.Contains("dagger"))
            {
                return "Rogue offhand needs a dagger";
            }

            if (cls == "fighter" && !id.Contains("sword"))
            {
                return "Fighter offhand needs a one-handed sword";
            }
        }

        if (slotKey == "armor" || slotKey == "helm" || slotKey == "boots" || slotKey == "gloves")
        {
            var w = ArmorWeightOf(id);
            if (cls == "mage" && w != "light")
            {
                return "Mage can wear light armor only";
            }

            if ((cls == "marksman" || cls == "rogue") && (w == "heavy" || w == "plate"))
            {
                return ClassPretty(cls) + " cannot wear " + w + " armor";
            }
        }

        return "";
    }

    private static string ArmorWeightOf(string id)
    {
        if (id.Contains("plate") || id.Contains("_ash")) return "plate";
        if (id.Contains("_iron")) return "heavy";
        if (id.Contains("hide")) return "medium";
        return "light";
    }

    private static string ItemHandLabel(string id)
    {
        if (id.Contains("charm") || id.Contains("orb") || id.Contains("_off")) return "Offhand";
        if (id.Contains("zwei")) return "Mainhand (2H)";
        if (id.Contains("sword") || id.Contains("dagger") || id.Contains("staff") || id.Contains("bow")
            || id.Contains("gun") || id.Contains("tome"))
        {
            return id.Contains("dagger") || id.Contains("sword_iron") ? "Mainhand or Offhand" : "Mainhand";
        }

        return "";
    }

    private string ItemDetailText(string id, int qty)
    {
        var name = ShortInv(id);
        var flavor = ItemFlavor(id);
        var req = ItemLevelReq(id);
        var line = name;
        var hand = ItemHandLabel(id);
        if (!string.IsNullOrEmpty(hand))
        {
            line += "\nSlot: " + hand;
        }

        if (id.StartsWith("armor_") || id.StartsWith("helm_") || id.StartsWith("boots_") || id.StartsWith("gloves_"))
        {
            line += "\nArmor: " + ArmorWeightOf(id);
        }

        if (id.StartsWith("spirit_"))
        {
            line += "\nFairy / spirit";
        }

        line += "\n" + flavor;
        if (qty > 1)
        {
            line += "\nx" + qty;
        }

        if (req > 1)
        {
            line += "\nRequires Lv " + req + (_level < req ? " (too low)" : "");
        }

        var slot = GuessEquipSlot(id);
        if (!string.IsNullOrEmpty(slot))
        {
            var why = EquipRejectReason(slot, id);
            if (!string.IsNullOrEmpty(why))
            {
                line += "\n" + why;
            }
        }

        return line;
    }

    private Vector2 GuiPointer()
    {
        var e = Event.current;
        if (e != null)
        {
            var s = Mathf.Max(0.1f, UiScaleSafe);
            return e.mousePosition / s;
        }

        var mouse = Mouse.current;
        if (mouse == null)
        {
            return Vector2.zero;
        }

        return ScreenToGui(mouse.position.ReadValue());
    }

    private string HoverItemText(string id, int qty)
    {
        var name = ShortInv(id);
        var flavor = ItemFlavor(id);
        var req = ItemLevelReq(id);
        var line = name + "\n" + flavor;
        if (qty > 1)
        {
            line += "\nx" + qty;
        }

        if (req > 1)
        {
            line += "\nLv " + req + (_level < req ? " (too low)" : "");
        }

        return line;
    }

    private void HandleInvDrag(Rect titleBar, Rect extraHandle)
    {
        var e = Event.current;
        if (e == null)
        {
            return;
        }

        var gui = GuiPointer();
        var over = titleBar.Contains(gui) || extraHandle.Contains(gui);
        if (e.type == EventType.MouseDown && e.button == 0 && over)
        {
            _invDragging = true;
            _invDragOffset = gui - _invPanelPos;
            e.Use();
        }

        if (_invDragging && e.type == EventType.MouseDrag)
        {
            _invPanelPos = gui - _invDragOffset;
            var win = InventoryWindowRect();
            _invPanelPos.x = Mathf.Clamp(_invPanelPos.x, 8f - win.width * 0.6f, GuiW - 48f);
            _invPanelPos.y = Mathf.Clamp(_invPanelPos.y, 8f, GuiH - 36f);
            e.Use();
        }

        if (e.type == EventType.MouseUp && e.button == 0)
        {
            _invDragging = false;
        }
    }

    private static string ShopItemLabel(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return id;
        }

        if (id.StartsWith("item_"))
        {
            return id.Substring(5);
        }

        return id.Replace('_', ' ');
    }

    private static string ItemFlavor(string id)
    {
        switch (id)
        {
            case "item_dust": return "Pull currency. 10 dust = 1 banner pull.";
            case "item_ticket": return "Pays for one banner pull before dust.";
            case "item_homestone": return "Warp to your set homestone.";
            case "item_ration": return "Field food. Restores a little HP and MP.";
            case "item_stew": return "60s ATK/DEF buff. R uses this first.";
            case "item_bread": return "Cheap snack. Small heal.";
            case "item_skill_tome": return "Grants +1 skill point. Spend it at the Trainer.";
            case "char_aurel": return "SSR portrait skin. RMB to equip.";
            case "char_nyla": return "SR portrait skin. RMB to equip.";
            case "card_fighter": return "Lv10 Warrior card. Earth resist + melee kit.";
            case "card_mage": return "Lv10 Mage card. Fire resist + staff kit.";
            case "card_marksman": return "Lv10 Archer card. Wind resist + charm seed.";
            case "card_rogue": return "Lv10 Rogue card. Dark resist + daggers.";
            case "sword_iron": return "One-handed sword. Mainhand or offhand.";
            case "sword_zwei": return "Two-handed sword. Blocks offhand.";
            case "sword_off": return "Offhand one-handed sword.";
            case "bow_hunter": return "Mainhand bow. Archer default.";
            case "staff_arcane": return "Mainhand staff. Mage default.";
            case "tome_novice": return "Mainhand tome. Uses staff animations.";
            case "dagger_twin": return "Light daggers. Mainhand or offhand.";
            case "dagger_off": return "Offhand dagger.";
            case "charm_leaf": return "Archer offhand charm.";
            case "orb_glass": return "Mage offhand orb.";
            case "gun_spark": return "Mainhand gun. Adventurer testing.";
            case "armor_leather": return "Light vest. Smith and early loot.";
            case "armor_hide": return "Medium hide armor.";
            case "armor_iron": return "Heavy iron vest. Requires level 8.";
            case "armor_ash": return "Plate ashen armor. Requires level 15.";
            case "armor_plate": return "Plate cuirass. Warrior / Adventurer.";
            default:
                if (id != null && id.StartsWith("spirit_")) return "Equip to boost a matching element.";
                if (id != null && id.Contains("_iron")) return "Iron tier. Requires level 8.";
                if (id != null && id.Contains("_ash")) return "Ashen tier. Requires level 15.";
                if (id != null && (id.Contains("leather") || id.Contains("_cap") || id.Contains("wrap")))
                    return "Leather tier. Wear from level 1.";
                return "An Ashen Realm item.";
        }
    }

    private static int ItemLevelReq(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return 1;
        }

        if (id.Contains("_ash"))
        {
            return 15;
        }

        if (id.StartsWith("armor_iron") || id.StartsWith("helm_iron") || id.StartsWith("boots_iron")
            || id.StartsWith("gloves_iron") || id.StartsWith("acc_iron"))
        {
            return 8;
        }

        return 1;
    }

    private Color InventorySlotColor(string id)
    {
        var tint = InventoryColor(id);
        if (!string.IsNullOrEmpty(id) && ItemLevelReq(id) > _level)
        {
            return new Color(tint.r * 0.35f, tint.g * 0.35f, tint.b * 0.35f, 0.9f);
        }

        return tint;
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
        if (id.StartsWith("card_"))
        {
            return new Color(0.55f, 0.4f, 0.2f, 0.9f);
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
        var plateH = string.IsNullOrEmpty(EquipId(_subclass)) ? 112f : 138f;
        var plate = new Rect(10f, 8f, 276f, plateH);
        UiChrome.Panel(plate, UiChrome.Gold);
        GUI.Label(new Rect(plate.x + 10f, plate.y + 6f, 180f, 18f),
            _playerLabel + "  ·  " + ClassPretty(_classId) + "  L" + _level);
        GUI.color = UiChrome.Gold;
        GUI.Label(new Rect(plate.x + 188f, plate.y + 6f, 80f, 18f), _gold + " g");
        GUI.color = Color.white;
        UiChrome.Bar(new Rect(plate.x + 10f, plate.y + 30f, 256f, 14f),
            _maxHp <= 0 ? 0f : (float)_hp / _maxHp, UiChrome.Hp, "HP", _hp + "/" + _maxHp);
        UiChrome.Bar(new Rect(plate.x + 10f, plate.y + 48f, 256f, 12f),
            _maxMp <= 0 ? 0f : (float)_mp / _maxMp, UiChrome.Mp, "MP", _mp + "/" + _maxMp);
        UiChrome.Bar(new Rect(plate.x + 10f, plate.y + 64f, 256f, 10f),
            _xpToLevel <= 0 ? 0f : (float)_xp / _xpToLevel, UiChrome.Xp, "XP", _xp + "/" + _xpToLevel);
        GUI.color = new Color(0.85f, 0.85f, 0.88f);
        GUI.Label(new Rect(plate.x + 10f, plate.y + 78f, 256f, 28f),
            WeaponPretty(_weapon) + " / " + WeaponPretty(_weapon2) + "   SP " + _skillPoints +
            ClassCardHint());
        GUI.color = Color.white;
        if (!string.IsNullOrEmpty(EquipId(_subclass)))
        {
            if (GUI.Button(new Rect(plate.x + 10f, plate.y + 108f, 130f, 22f),
                    _transformed ? "Revert" : "Transform"))
            {
                _input?.RequestTransform(!_transformed);
            }
        }

        var ticker = new Rect(296f, 8f, GuiW - 586f, 28f);
        if (ticker.width > 80f)
        {
            UiChrome.Panel(ticker, UiChrome.Steel);
            GUI.Label(new Rect(ticker.x + 8f, ticker.y + 5f, ticker.width - 16f, 20f), _status);
        }

        DrawWeaponPair();

        var log = GameLog.RecentText(5);
        if (!string.IsNullOrEmpty(log))
        {
            var logBox = new Rect(10f, 126f, Mathf.Min(360f, GuiW - 300f), 88f);
            UiChrome.Panel(logBox, UiChrome.Steel);
            var logStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                wordWrap = true,
            };
            GUI.Label(new Rect(logBox.x + 8f, logBox.y + 4f, logBox.width - 16f, logBox.height - 8f), log, logStyle);
        }
    }

    private bool HasSecondaryWeapon()
    {
        return !string.IsNullOrEmpty(_weapon2) && _weapon2 != "none";
    }

    private static string WeaponPretty(string id)
    {
        if (string.IsNullOrEmpty(id) || id == "none")
        {
            return "—";
        }

        if (id.Contains("bow"))
        {
            return "Bow";
        }

        if (id.Contains("gun"))
        {
            return "Gun";
        }

        if (id.Contains("tome"))
        {
            return "Tome";
        }

        if (id.Contains("charm"))
        {
            return "Charm";
        }

        if (id.Contains("orb"))
        {
            return "Orb";
        }

        if (id.Contains("staff"))
        {
            return "Staff";
        }

        if (id.Contains("dagger"))
        {
            return "Dagger";
        }

        if (id.Contains("sword"))
        {
            return "Sword";
        }

        return ShortInv(id);
    }

    private void TryWeaponSwap()
    {
        _comingSoonToast = "Offhand is a paperdoll slot (I) — N no longer swaps weapons";
        _comingSoonUntil = Time.time + 2.2f;
        _status = "use inventory offhand";
    }

    private void TryQuickUseConsumable()
    {
        if (_input == null)
        {
            return;
        }

        var slot = FindQuickUseSlot();
        if (slot < 0)
        {
            _status = "no food / ration";
            return;
        }

        _input.RequestUseItem(slot);
        _status = "use " + _invSlots[slot];
    }

    private int FindQuickUseSlot()
    {
        string[] prefer = { "item_stew", "item_ration", "item_bread" };
        for (var p = 0; p < prefer.Length; p++)
        {
            for (var i = 0; i < _invSlots.Length; i++)
            {
                if (_invSlots[i] == prefer[p] && _invQty[i] > 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private void DrawWeaponPair()
    {
        var y = GuiH - 196f;
        var flash = Time.time < _weaponSwapUntil;
        DrawWeaponChip(new Rect(12f, y, 88f, 36f), "Main", _weapon, true, flash);
        DrawWeaponChip(new Rect(104f, y, 88f, 36f), "Off", _weapon2, false, flash);
    }

    private void DrawWeaponChip(Rect rect, string tag, string weaponId, bool haupt, bool flash)
    {
        var empty = string.IsNullOrEmpty(weaponId) || weaponId == "none";
        var fill = empty
            ? new Color(0.14f, 0.14f, 0.16f, 0.9f)
            : haupt
                ? new Color(0.32f, 0.26f, 0.1f, 0.94f)
                : new Color(0.12f, 0.22f, 0.3f, 0.94f);
        if (flash)
        {
            fill = Color.Lerp(fill, Color.white, 0.28f);
        }

        GUI.color = fill;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        UiChrome.Border(rect, haupt
            ? UiChrome.Gold
            : new Color(0.55f, 0.82f, 0.95f, 1f), 2f);
        GUI.color = Color.white;
        GUI.Label(new Rect(rect.x + 6f, rect.y + 2f, rect.width - 8f, 38f),
            tag + "\n" + WeaponPretty(weaponId));
    }

    private static void DrawSlotEdge(Rect rect, Color color)
    {
        UiChrome.Border(rect, color, 2f);
    }

    private void DrawTargetFrame()
    {
        if (_world == null || !_world.TryGetTargetInfo(out var info) || string.IsNullOrEmpty(info.Id))
        {
            return;
        }

        var frame = new Rect(GuiW - 280f, 164f, 260f, 118f);
        if (!string.IsNullOrEmpty(info.ThreatTopId) && info.ThreatTopId == _world.SelfId)
        {
            UiChrome.Panel(frame, new Color(0.95f, 0.25f, 0.2f, 1f));
        }
        else if (info.ThreatSelf >= 35f)
        {
            UiChrome.Panel(frame, new Color(0.95f, 0.85f, 0.2f, 1f));
        }
        else
        {
            UiChrome.Panel(frame, UiChrome.Steel);
        }

        var portrait = new Rect(frame.x + 10, frame.y + 12, 48, 48);
        GUI.color = info.Kind == "player"
            ? new Color(0.15f, 0.85f, 1f, 1f)
            : info.Kind == "npc"
                ? new Color(0.95f, 0.85f, 0.35f, 1f)
                : new Color(0.2f, 1f, 0.25f, 1f);
        GUI.DrawTexture(portrait, Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUI.Label(new Rect(frame.x + 68, frame.y + 8, 180, 20),
            string.IsNullOrEmpty(info.Label) ? info.Id : info.Label);

        if (info.Kind == "npc")
        {
            GUI.Label(new Rect(frame.x + 68, frame.y + 32, 180, 40), "NPC — LMB inspect, RMB talk");
            return;
        }

        var hpRatio = info.MaxHp <= 0 ? 0f : (float)info.Hp / info.MaxHp;
        var mpRatio = info.MaxMp <= 0 ? 0f : (float)info.Mp / info.MaxMp;
        DrawResourceBar(new Rect(frame.x + 68, frame.y + 32, 170, 10), hpRatio, UiChrome.Hp, "");
        GUI.Label(new Rect(frame.x + 68, frame.y + 30, 170, 14),
            info.Hp + "/" + info.MaxHp + "  (" + Mathf.RoundToInt(hpRatio * 100f) + "%)");
        DrawResourceBar(new Rect(frame.x + 68, frame.y + 50, 170, 10), mpRatio, UiChrome.Mp, "");
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
            : _inspectKind == "npc"
                ? new Color(0.95f, 0.85f, 0.35f, 1f)
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
        var box = new Rect(GuiW - 340f, GuiH - 210f, 320f, 190f);
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

    private void TryUseNearestPortal()
    {
        if (_input == null || _world == null || _portalIds.Count == 0)
        {
            return;
        }

        var best = -1;
        var bestD = 2.4f;
        for (var i = 0; i < _portalIds.Count; i++)
        {
            var d = Vector2.Distance(new Vector2(_x, _y), new Vector2(_portalXs[i], _portalYs[i]));
            if (d < bestD)
            {
                bestD = d;
                best = i;
            }
        }

        if (best < 0)
        {
            _status = "no portal nearby (walk onto orange Gate or press H)";
            return;
        }

        _portalCooldownUntil = Time.time + 1.2f;
        _input.RequestPortal(_portalIds[best]);
        _status = "portal " + _portalIds[best];
    }

    private void MaybeBeginMapLoad(string json)
    {
        if (_world == null || _gatePhase < 3 || !_inWorld)
        {
            return;
        }

        var mapId = _world.MapId;
        if (string.IsNullOrEmpty(mapId) || mapId == _loadMapId)
        {
            return;
        }

        _loadMapId = mapId;
        var mapSlice = JsonUtil.ExtractObject(json, "map");
        if (string.IsNullOrEmpty(mapSlice))
        {
            mapSlice = JsonUtil.SliceAround(json, "\"map\":{", 0, 4000);
        }

        var name = JsonUtil.ExtractString(mapSlice, "name");
        _loadMapTitle = string.IsNullOrEmpty(name) ? PrettyMapId(mapId) : name;
        _loadArt = SpriteCatalog.ForLoadArt(mapId);
        _loadUntil = Time.unscaledTime + 1.2f;
        SoundCatalog.Play(SoundCatalog.Id.Portal);
        _world?.PlayPortalRipple();
    }

    private static string PrettyMapId(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return "Unknown";
        }

        var hash = id.IndexOf('#');
        var baseId = hash > 0 ? id.Substring(0, hash) : id;
        return baseId.Replace('_', ' ');
    }

    private void ApplyPortalsFromState(string json)
    {
        _portalIds.Clear();
        _portalXs.Clear();
        _portalYs.Clear();
        var idx = json.IndexOf("\"portals\"");
        if (idx < 0)
        {
            return;
        }

        var part = json.Substring(idx);
        var cursor = 0;
        while (cursor < part.Length && _portalIds.Count < 48)
        {
            var idIdx = part.IndexOf("\"id\":\"portal_", cursor);
            if (idIdx < 0)
            {
                break;
            }

            var slice = part.Substring(idIdx, Mathf.Min(280, part.Length - idIdx));
            var id = JsonUtil.ExtractString(slice, "id");
            if (string.IsNullOrEmpty(id) || !JsonUtil.TryNumber(slice, "x", out var px) || !JsonUtil.TryNumber(slice, "y", out var py))
            {
                cursor = idIdx + 8;
                continue;
            }

            _portalIds.Add(id);
            _portalXs.Add(px);
            _portalYs.Add(py);
            _world?.UpsertPortalMarker(id, px, py);
            cursor = idIdx + 8;
        }

        if (_portalIds.Count > 0)
        {
            _status = "gates " + _portalIds.Count;
        }
    }

    private void ApplyQuestFromState(string json)
    {
        _questLogText = "";
        _questTrackerLine = "";
        var idx = json.IndexOf("\"quests\"");
        if (idx < 0)
        {
            return;
        }

        var part = json.Substring(idx, Mathf.Min(4000, json.Length - idx));
        var cursor = 0;
        while (cursor < part.Length)
        {
            var qIdx = part.IndexOf("\"questId\":\"", cursor);
            if (qIdx < 0)
            {
                break;
            }

            var slice = part.Substring(qIdx, Mathf.Min(320, part.Length - qIdx));
            var qid = JsonUtil.ExtractString(slice, "questId");
            var name = JsonUtil.ExtractString(slice, "name");
            var hint = JsonUtil.ExtractString(slice, "hint");
            JsonUtil.TryInt(slice, "progress", out var prog);
            JsonUtil.TryInt(slice, "stepIndex", out var step);
            JsonUtil.TryInt(slice, "stepNeed", out var need);
            var done = slice.Contains("\"completed\":true");
            if (string.IsNullOrEmpty(name))
            {
                name = qid;
            }

            var line = name + "  " + (string.IsNullOrEmpty(hint) ? ("step " + step) : hint);
            if (need > 0)
            {
                line += "  " + prog + "/" + need;
            }

            if (done)
            {
                line += "  READY";
            }

            _questLogText += line + "\n";
            if (string.IsNullOrEmpty(_questTrackerLine))
            {
                _questTrackerLine = line;
            }

            cursor = qIdx + 10;
        }
    }

    private void ApplyInteractFromJson(string json)
    {
        _interactTargetId = JsonUtil.ExtractString(json, "targetId");
        _interactKind = JsonUtil.ExtractString(json, "interact");
        _interactLine = JsonUtil.ExtractString(json, "line");
        _interactNpcName = JsonUtil.ExtractString(json, "name");
        _shopId = "";
        _shopItemIds.Clear();
        _shopBuyPrices.Clear();
        _questIds.Clear();
        _questStates.Clear();
        _questNames.Clear();

        var shopIdx = json.IndexOf("\"shop\"");
        if (shopIdx >= 0)
        {
            var shopSlice = json.Substring(shopIdx, Mathf.Min(8000, json.Length - shopIdx));
            _shopId = JsonUtil.ExtractString(shopSlice, "id");
            var cursor = 0;
            while (cursor < shopSlice.Length && _shopItemIds.Count < 24)
            {
                var iIdx = shopSlice.IndexOf("\"itemId\":\"", cursor);
                if (iIdx < 0)
                {
                    break;
                }

                var slice = shopSlice.Substring(iIdx, Mathf.Min(120, shopSlice.Length - iIdx));
                var itemId = JsonUtil.ExtractString(slice, "itemId");
                JsonUtil.TryInt(slice, "buyPrice", out var price);
                if (!string.IsNullOrEmpty(itemId))
                {
                    _shopItemIds.Add(itemId);
                    _shopBuyPrices.Add(price);
                }

                cursor = iIdx + 10;
            }
        }

        var qIdx = json.IndexOf("\"quests\"");
        if (qIdx >= 0)
        {
            var qPart = json.Substring(qIdx, Mathf.Min(1200, json.Length - qIdx));
            var cursor = 0;
            while (cursor < qPart.Length && _questIds.Count < 6)
            {
                var idAt = qPart.IndexOf("\"id\":\"q_", cursor);
                if (idAt < 0)
                {
                    break;
                }

                var slice = qPart.Substring(idAt, Mathf.Min(200, qPart.Length - idAt));
                var id = JsonUtil.ExtractString(slice, "id");
                var name = JsonUtil.ExtractString(slice, "name");
                var state = "available";
                if (slice.Contains("\"state\":\"ready\"")) state = "ready";
                else if (slice.Contains("\"state\":\"active\"")) state = "active";
                else if (slice.Contains("\"state\":\"done\"")) state = "done";
                if (!string.IsNullOrEmpty(id))
                {
                    _questIds.Add(id);
                    _questNames.Add(string.IsNullOrEmpty(name) ? id : name);
                    _questStates.Add(state);
                }

                cursor = idAt + 6;
            }
        }
    }

    private void DrawLoginPanel()
    {
        if (!_showLogin)
        {
            return;
        }

        var rect = new Rect(GuiW * 0.5f - 170f, GuiH * 0.18f, 340f, 170f);
        UiChrome.Panel(rect, UiChrome.Gold);
        GUI.color = Color.white;
        GUI.Label(new Rect(rect.x + 12, rect.y + 8, 320, 20), "Account optional — Guest works without DATABASE_URL");
        _loginUser = GUI.TextField(new Rect(rect.x + 12, rect.y + 36, 316, 24), _loginUser ?? "");
        _loginPass = GUI.PasswordField(new Rect(rect.x + 12, rect.y + 66, 316, 24), _loginPass ?? "", '*');
        if (GUI.Button(new Rect(rect.x + 12, rect.y + 100, 150, 28), "Register"))
        {
            _input?.RequestRegister(_loginUser, _loginPass);
            _authStatus = "registering…";
        }

        if (GUI.Button(new Rect(rect.x + 178, rect.y + 100, 150, 28), "Login"))
        {
            _input?.RequestLogin(_loginUser, _loginPass);
            _authStatus = "logging in…";
        }

        GUI.Label(new Rect(rect.x + 12, rect.y + 136, 316, 24), _authStatus ?? "");
    }

    private void DrawGate()
    {
        GUI.color = new Color(0.05f, 0.06f, 0.09f, 0.97f);
        GUI.DrawTexture(new Rect(0, 0, GuiW, GuiH), Texture2D.whiteTexture);
        GUI.color = Color.white;
        var panel = new Rect(GuiW * 0.5f - 220f, GuiH * 0.12f, 440f, 420f);
        UiChrome.Panel(panel, UiChrome.Gold);
        GUI.color = Color.white;
        GUI.Label(new Rect(panel.x + 16, panel.y + 10, 400, 24), "Ashen Realm — Guest first");
        GUI.Label(new Rect(panel.x + 16, panel.y + 34, 400, 20), _status ?? "");

        if (_gatePhase <= 0)
        {
            GUI.Label(new Rect(panel.x + 16, panel.y + 64, 408, 36),
                "Guest play is the default. Login/register need Postgres (DATABASE_URL).");
            if (GUI.Button(new Rect(panel.x + 16, panel.y + 108, 408, 40), "Continue as Guest"))
            {
                _gatePhase = 3;
                _inWorld = true;
                _status = "guest world";
            }

            GUI.Label(new Rect(panel.x + 16, panel.y + 158, 400, 20), "Optional account");
            _loginUser = GUI.TextField(new Rect(panel.x + 16, panel.y + 182, 408, 26), _loginUser ?? "");
            _loginPass = GUI.PasswordField(new Rect(panel.x + 16, panel.y + 214, 408, 26), _loginPass ?? "", '*');
            if (GUI.Button(new Rect(panel.x + 16, panel.y + 248, 196, 32), "Register"))
            {
                _input?.RequestRegister(_loginUser, _loginPass);
                _authStatus = "registering…";
            }

            if (GUI.Button(new Rect(panel.x + 228, panel.y + 248, 196, 32), "Login"))
            {
                _input?.RequestLogin(_loginUser, _loginPass);
                _authStatus = "logging in…";
            }

            GUI.Label(new Rect(panel.x + 16, panel.y + 288, 408, 40), _authStatus ?? "");
            return;
        }

        if (_gatePhase == 1)
        {
            GUI.Label(new Rect(panel.x + 16, panel.y + 70, 400, 20), "Select server");
            for (var i = 0; i < _serverIds.Count; i++)
            {
                var selected = _selectedServerId == _serverIds[i];
                if (selected)
                {
                    GUI.color = new Color(0.3f, 0.45f, 0.35f);
                }

                if (GUI.Button(new Rect(panel.x + 16, panel.y + 100 + i * 36, 408, 32), _serverNames[i]))
                {
                    _selectedServerId = _serverIds[i];
                }

                GUI.color = Color.white;
            }

            if (GUI.Button(new Rect(panel.x + 16, panel.y + 280, 408, 32), "Confirm server"))
            {
                _input?.RequestCharList();
                _status = "loading characters…";
            }

            return;
        }

        if (_gatePhase == 2)
        {
            GUI.Label(new Rect(panel.x + 16, panel.y + 64, 400, 20), "Character slots (8)");
            for (var i = 0; i < 8; i++)
            {
                var col = i % 4;
                var row = i / 4;
                var r = new Rect(panel.x + 16 + col * 102, panel.y + 92 + row * 70, 96, 60);
                var label = _charEmpty[i]
                    ? "Empty " + i
                    : (_charNames[i] ?? "?") + "\n" + (_charClasses[i] ?? "") + " Lv" + _charLevels[i];
                if (_selectedSlot == i)
                {
                    GUI.color = new Color(0.35f, 0.5f, 0.4f);
                }

                if (GUI.Button(r, label))
                {
                    _selectedSlot = i;
                    _confirmDelete = false;
                }

                GUI.color = Color.white;
            }

            _charNameDraft = GUI.TextField(new Rect(panel.x + 16, panel.y + 250, 408, 26), _charNameDraft ?? "Adventurer");
            if (_charEmpty[_selectedSlot])
            {
                if (GUI.Button(new Rect(panel.x + 16, panel.y + 290, 408, 32), "Create Adventurer in slot " + _selectedSlot))
                {
                    var nm = string.IsNullOrEmpty(_charNameDraft) ? "Adventurer" : _charNameDraft;
                    if (nm.Trim().Length < 2)
                    {
                        _status = "name too short";
                    }
                    else
                    {
                        _input?.RequestCharCreateSlot(_selectedSlot, nm);
                        _status = "creating…";
                    }
                }
            }
            else
            {
                if (GUI.Button(new Rect(panel.x + 16, panel.y + 290, 200, 32), "Enter world"))
                {
                    SoundCatalog.Play(SoundCatalog.Id.UiClick);
                    _input?.RequestCharSelect(_selectedSlot);
                    _status = "entering…";
                }

                if (!_confirmDelete)
                {
                    if (GUI.Button(new Rect(panel.x + 224, panel.y + 290, 200, 32), "Delete…"))
                    {
                        _confirmDelete = true;
                    }
                }
                else if (GUI.Button(new Rect(panel.x + 224, panel.y + 290, 200, 32), "Confirm delete"))
                {
                    _input?.RequestCharDelete(_selectedSlot);
                    _confirmDelete = false;
                    _status = "deleted slot";
                }
            }
        }
    }

    private void DrawCharCreate()
    {
        if (_charNameSet || _gatePhase < 3)
        {
            return;
        }

        var rect = new Rect(GuiW * 0.5f - 180f, GuiH * 0.28f, 360f, 140f);
        GUI.color = new Color(0.1f, 0.12f, 0.16f, 0.96f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(rect.x + 12, rect.y + 8, 336, 20), "Create Adventurer");
        _charNameDraft = GUI.TextField(new Rect(rect.x + 12, rect.y + 36, 336, 24), _charNameDraft ?? "Adventurer");
        if (GUI.Button(new Rect(rect.x + 100, rect.y + 80, 160, 28), "Enter Ashen Town"))
        {
            _input?.RequestCharCreate(
                string.IsNullOrEmpty(_charNameDraft) ? "Adventurer" : _charNameDraft,
                "adventurer");
            _status = "creating character";
        }
    }

    private void DrawInteractPanel()
    {
    }

    private void DrawNpcDialog()
    {
        if (!_showNpcDialog)
        {
            return;
        }

        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(new Rect(0f, 0f, GuiW, GuiH), Texture2D.whiteTexture);
        GUI.color = Color.white;

        var portrait = new Rect(GuiW * 0.5f - 420f, 80f, 280f, 360f);
        GUI.color = new Color(0.12f, 0.12f, 0.14f, 1f);
        GUI.DrawTexture(portrait, Texture2D.whiteTexture);
        GUI.color = Color.white;
        var frames = SpriteCatalog.GetClip(_interactTargetId, "npc", SpriteCatalog.Clip.Idle, 2);
        if (frames != null && frames.Length > 0 && frames[0] != null)
        {
            SpriteCatalog.DrawGui(new Rect(portrait.x + 16f, portrait.y + 16f, 248f, 328f), frames[0], Color.white);
        }
        else
        {
            GUI.color = new Color(0.95f, 0.85f, 0.35f, 1f);
            GUI.DrawTexture(new Rect(portrait.x + 40f, portrait.y + 80f, 200f, 200f), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        var box = new Rect(GuiW * 0.5f - 120f, GuiH - 280f, 560f, 220f);
        GUI.color = new Color(0.08f, 0.09f, 0.12f, 0.96f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = Color.white;
        var who = string.IsNullOrEmpty(_interactNpcName) ? _interactTargetId : _interactNpcName;
        GUI.Label(new Rect(box.x + 12f, box.y + 8f, 440f, 22f), who);
        GUI.Label(new Rect(box.x + 12f, box.y + 32f, 530f, 40f),
            string.IsNullOrEmpty(_interactLine) ? "…" : _interactLine);
        GUI.Label(new Rect(box.x + 12f, box.y + 74f, 530f, 20f), "What do you want to do, Traveler?");

        var y = box.y + 100f;
        var btnW = 250f;
        var col = 0;
        void Option(string label, System.Action onClick)
        {
            var r = new Rect(box.x + 12f + col * (btnW + 8f), y, btnW, 24f);
            if (GUI.Button(r, label))
            {
                onClick();
            }

            col += 1;
            if (col >= 2)
            {
                col = 0;
                y += 28f;
            }
        }

        if (_interactKind.StartsWith("shop_"))
        {
            Option("Buy items from " + who, () =>
            {
                CloseNpcDialog();
                _showShopBuy = true;
            });
            Option("Sell items to " + who, () =>
            {
                CloseNpcDialog();
                _showShopSell = true;
            });
            if (_interactKind == "shop_weapon")
            {
                Option("Enhance gear", () =>
                {
                    CloseNpcDialog();
                    _showEnhance = true;
                });
            }
        }

        if (_interactKind == "quest" || _questIds.Count > 0)
        {
            for (var i = 0; i < _questIds.Count; i++)
            {
                var qid = _questIds[i];
                var qname = _questNames[i];
                var st = _questStates[i];
                if (st == "available")
                {
                    Option("Accept: " + qname, () =>
                    {
                        _input?.RequestQuestAccept(qid);
                        CloseNpcDialog();
                    });
                }
                else if (st == "ready")
                {
                    Option("Turn in: " + qname, () =>
                    {
                        _input?.RequestQuestTurnIn(qid);
                        CloseNpcDialog();
                    });
                }
            }
        }

        if (_interactKind == "trainer")
        {
            Option("Train skills", () =>
            {
                CloseNpcDialog();
                _showSkillTree = true;
            });
        }

        if (_interactKind == "class_change")
        {
            Option("Choose a class", () =>
            {
                CloseNpcDialog();
                _showClassService = true;
            });
        }

        if (_interactKind == "subclass")
        {
            Option("Specialist path", () => { CloseNpcDialog(); });
        }

        if (_interactKind == "homestone")
        {
            Option("Set home", () =>
            {
                _input?.RequestHomestone("set");
                CloseNpcDialog();
            });
            Option("Teleport home", () =>
            {
                _input?.RequestHomestone("teleport");
                CloseNpcDialog();
            });
        }

        if (_interactKind == "auction")
        {
            Option("Open auction", () =>
            {
                CloseNpcDialog();
                _showAuction = true;
            });
        }

        if (_interactKind == "switch")
        {
            Option("Flip switch", CloseNpcDialog);
        }

        if (_interactKind == "flavor")
        {
            Option("Continue", CloseNpcDialog);
        }

        if (GUI.Button(new Rect(box.x + box.width - 90f, box.y + box.height - 32f, 78f, 24f), "Exit"))
        {
            CloseNpcDialog();
        }
    }

    private void DrawShopBuyPanel()
    {
        if (!_showShopBuy)
        {
            return;
        }

        var shopRows = _shopItemIds.Count <= 0 ? 1 : (_shopItemIds.Count + 1) / 2;
        var rect = new Rect(40f, 80f, 460f, Mathf.Clamp(160f + shopRows * 26f, 200f, 520f));
        GUI.color = new Color(0.1f, 0.11f, 0.14f, 0.95f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(rect.x + 10, rect.y + 8, 360, 22), "Buy — " + _interactNpcName);
        var y = rect.y + 36f;
        for (var i = 0; i < _shopItemIds.Count; i++)
        {
            var col = i % 2;
            var row = i / 2;
            var req = ItemLevelReq(_shopItemIds[i]);
            var lockTag = _level < req ? " LOCK" : "";
            var reqTag = req > 1 ? " L" + req : "";
            var btn = new Rect(rect.x + 10 + col * 220f, y + row * 24f, 214f, 22f);
            if (GUI.Button(btn,
                    "Buy " + ShopItemLabel(_shopItemIds[i]) + reqTag + " (" + _shopBuyPrices[i] + "g)" + lockTag))
            {
                _input?.RequestShopBuy(_shopId, _shopItemIds[i]);
            }
        }

        if (GUI.Button(new Rect(rect.x + rect.width - 80, rect.y + rect.height - 30, 70, 24), "Exit"))
        {
            _showShopBuy = false;
        }
    }

    private void DrawShopSellPanel()
    {
        if (!_showShopSell)
        {
            return;
        }

        var rect = new Rect(40f, 80f, 460f, 360f);
        GUI.color = new Color(0.1f, 0.11f, 0.14f, 0.95f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(rect.x + 10, rect.y + 8, 360, 22), "Sell — " + _interactNpcName);
        var y = rect.y + 36f;
        var shown = 0;
        for (var i = 0; i < _invSlots.Length && shown < 10; i++)
        {
            var id = _invSlots[i];
            if (string.IsNullOrEmpty(id) || id == "none")
            {
                continue;
            }

            if (GUI.Button(new Rect(rect.x + 10, y, 360, 22), "Sell " + ShopItemLabel(id) + " x" + _invQty[i]))
            {
                _input?.RequestShopSell(_shopId, id);
            }

            y += 24f;
            shown += 1;
        }

        if (GUI.Button(new Rect(rect.x + rect.width - 80, rect.y + rect.height - 30, 70, 24), "Exit"))
        {
            _showShopSell = false;
        }
    }

    private void DrawClassServicePanel()
    {
        if (!_showClassService)
        {
            return;
        }

        var rect = new Rect(40f, 80f, 460f, 240f);
        GUI.color = new Color(0.1f, 0.11f, 0.14f, 0.95f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(rect.x + 10, rect.y + 8, 430, 36), "Choose a class. This cannot be undone.");
        var picks = new[] { ("fighter", "Fighter"), ("marksman", "Archer"), ("mage", "Mage"), ("rogue", "Rogue") };
        var y = rect.y + 50f;
        for (var i = 0; i < picks.Length; i++)
        {
            var on = _classPickPending == picks[i].Item1;
            GUI.color = on ? new Color(0.9f, 0.75f, 0.3f) : Color.white;
            if (GUI.Button(new Rect(rect.x + 10 + (i % 2) * 220f, y + (i / 2) * 26f, 210f, 24f), picks[i].Item2))
            {
                _classPickPending = picks[i].Item1;
            }

            GUI.color = Color.white;
        }

        y += 60f;
        var canConfirm = !string.IsNullOrEmpty(_classPickPending) && _level >= 20 && _classId == "adventurer";
        var confirm = canConfirm
            ? "Confirm " + ClassPretty(_classPickPending)
            : (_classId != "adventurer" ? "Already classed" : "Need L20 + a pick");
        GUI.enabled = canConfirm;
        if (GUI.Button(new Rect(rect.x + 10, y, 340, 26), confirm))
        {
            _input?.RequestChooseClass(_classPickPending);
            _showClassService = false;
            _classPickPending = "";
        }

        GUI.enabled = true;
        if (GUI.Button(new Rect(rect.x + rect.width - 80, rect.y + rect.height - 30, 70, 24), "Exit"))
        {
            _showClassService = false;
        }
    }

    private void DrawEnhancePanel()
    {
        if (!_showEnhance)
        {
            return;
        }

        var slots = new[]
        {
            ("mainhand", "Weapon", EquipId(_weapon)),
            ("armor", "Armor", EquipId(_armor)),
            ("helm", "Helm", EquipId(_helm)),
            ("boots", "Boots", EquipId(_boots)),
            ("gloves", "Gloves", EquipId(_gloves)),
            ("amulet", "Amulet", EquipId(_amulet)),
            ("ring1", "Ring A", EquipId(_ring1)),
            ("ring2", "Ring B", EquipId(_ring2)),
            ("subclass", "Subclass", EquipId(_subclass)),
        };
        var rect = new Rect(40f, 60f, 480f, 380f);
        GUI.color = new Color(0.1f, 0.11f, 0.14f, 0.95f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(rect.x + 10, rect.y + 8, 400, 22), "Enhance (gold + star dust, max +5)");
        var y = rect.y + 36f;
        for (var i = 0; i < slots.Length; i++)
        {
            var key = slots[i].Item1;
            var label = slots[i].Item2;
            var id = slots[i].Item3;
            var lv = 0;
            _enhanceLevels.TryGetValue(key, out lv);
            var next = lv + 1;
            var gold = 50 * next;
            var dust = 2 * next;
            var row = id + (string.IsNullOrEmpty(id) ? " (empty)" : "") + "  +" + lv + "  → " + gold + "g / " + dust + " dust";
            if (GUI.Button(new Rect(rect.x + 10, y, 460, 22), "Enhance " + label + ": " + row))
            {
                if (!string.IsNullOrEmpty(id) && lv < 5)
                {
                    _input?.RequestEnhance(key);
                }
            }

            y += 24f;
        }

        if (GUI.Button(new Rect(rect.x + rect.width - 80, rect.y + rect.height - 30, 70, 24), "Exit"))
        {
            _showEnhance = false;
        }
    }

    private void DrawQuestLog()
    {
        if (!_showQuestLog)
        {
            return;
        }

        var rect = new Rect(20f, 160f, 340f, 200f);
        GUI.color = new Color(0.08f, 0.1f, 0.12f, 0.92f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(rect.x + 8, rect.y + 6, 324, 20), "Quests (J) — Mira → Ridge → Crypt/Marsh → Tower → Lv20 card");
        GUI.Label(new Rect(rect.x + 8, rect.y + 32, 324, 160),
            string.IsNullOrEmpty(_questLogText) ? "(none active — talk to Sister Mira)" : _questLogText);
    }

    private void DrawQuestTracker()
    {
        if (string.IsNullOrEmpty(_questTrackerLine))
        {
            return;
        }

        var y = GuiH - 214f;
        GUI.color = new Color(0.08f, 0.1f, 0.14f, 0.88f);
        GUI.DrawTexture(new Rect(20f, y, 440f, 28f), Texture2D.whiteTexture);
        GUI.color = new Color(0.95f, 0.88f, 0.55f, 1f);
        GUI.Label(new Rect(24f, y + 2f, 432f, 24f), "J  " + _questTrackerLine);
        GUI.color = Color.white;
    }

    private void DrawPartyInvite()
    {
        if (string.IsNullOrEmpty(_partyInviteId) || Time.time > _partyInviteUntil)
        {
            return;
        }

        var rect = new Rect(GuiW * 0.5f - 160f, 120f, 320f, 72f);
        GUI.color = new Color(0.12f, 0.14f, 0.18f, 0.95f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(rect.x + 10, rect.y + 8, rect.width - 20, 22),
            "Party invite from " + _partyInviteFrom);
        if (GUI.Button(new Rect(rect.x + 20, rect.y + 36, 120, 26), "Accept"))
        {
            _input?.RequestPartyRespond(_partyInviteId, true);
            _partyInviteId = "";
            _status = "accepted party";
        }

        if (GUI.Button(new Rect(rect.x + 160, rect.y + 36, 120, 26), "Decline"))
        {
            _input?.RequestPartyRespond(_partyInviteId, false);
            _partyInviteId = "";
            _status = "declined party";
        }
    }

    private void DrawPartyPanel()
    {
        if (string.IsNullOrEmpty(_partyId))
        {
            return;
        }

        var rows = Mathf.Max(1, _partyMemberNames.Count);
        var rect = new Rect(10f, 118f, 240f, 28f + rows * 28f);
        GUI.color = new Color(0.1f, 0.12f, 0.16f, 0.92f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(rect.x + 8, rect.y + 4, rect.width - 16, 18), "Party HUD");
        for (var i = 0; i < _partyMemberNames.Count; i++)
        {
            var y = rect.y + 24f + i * 28f;
            var label = "Lv" + _partyMemberLevel[i] + " " + _partyMemberClass[i] + " " + _partyMemberNames[i];
            if (GUI.Button(new Rect(rect.x + 8, y, 160, 24), label, GUI.skin.label))
            {
                var pid = _partyMemberIds[i];
                if (!string.IsNullOrEmpty(pid) && _world != null)
                {
                    _world.SetLockTarget(pid);
                    _input?.SetTarget(pid);
                    _status = "lock-on " + _partyMemberNames[i];
                }
            }
            var hpRatio = _partyMemberMaxHp[i] <= 0 ? 0f : (float)_partyMemberHp[i] / _partyMemberMaxHp[i];
            var mpRatio = _partyMemberMaxMp[i] <= 0 ? 0f : (float)_partyMemberMp[i] / _partyMemberMaxMp[i];
            DrawResourceBar(new Rect(rect.x + 8, y + 14, 160, 5), hpRatio, Color.red, "");
            DrawResourceBar(new Rect(rect.x + 8, y + 20, 160, 4), mpRatio, new Color(0.3f, 0.55f, 1f, 1f), "");
        }

        if (GUI.Button(new Rect(rect.x + rect.width - 70f, rect.y + 4, 60, 20), "Leave"))
        {
            _input?.RequestPartyLeave();
            _status = "leaving party";
        }
    }

    private void DrawGuildInvite()
    {
        if (string.IsNullOrEmpty(_guildInviteId) || Time.time > _guildInviteUntil)
        {
            return;
        }

        var rect = new Rect(GuiW * 0.5f - 160f, 80f, 320f, 70f);
        GUI.color = new Color(0.12f, 0.14f, 0.2f, 0.95f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(rect.x + 10, rect.y + 8, 300, 20),
            _guildInviteFrom + " → guild " + _guildInviteName);
        if (GUI.Button(new Rect(rect.x + 20, rect.y + 36, 120, 26), "Join"))
        {
            _input?.RequestGuildRespond(_guildInviteId, true);
            _guildInviteId = "";
        }

        if (GUI.Button(new Rect(rect.x + 160, rect.y + 36, 120, 26), "Decline"))
        {
            _input?.RequestGuildRespond(_guildInviteId, false);
            _guildInviteId = "";
        }
    }

    private void DrawTradeInvite()
    {
        if (string.IsNullOrEmpty(_tradeInviteId) || Time.time > _tradeInviteUntil)
        {
            return;
        }

        var rect = new Rect(GuiW * 0.5f - 160f, 80f, 320f, 70f);
        GUI.color = new Color(0.14f, 0.12f, 0.1f, 0.95f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(rect.x + 10, rect.y + 8, 300, 20), "Trade from " + _tradeInviteFrom);
        if (GUI.Button(new Rect(rect.x + 20, rect.y + 36, 120, 26), "Accept"))
        {
            _input?.RequestTradeRespond(_tradeInviteId, true);
            _tradeInviteId = "";
        }

        if (GUI.Button(new Rect(rect.x + 160, rect.y + 36, 120, 26), "Decline"))
        {
            _input?.RequestTradeRespond(_tradeInviteId, false);
            _tradeInviteId = "";
        }
    }

    private void DrawTradePanel()
    {
        if (string.IsNullOrEmpty(_tradeId))
        {
            return;
        }

        var rect = new Rect(GuiW * 0.5f - 180f, GuiH * 0.52f, 360f, 160f);
        GUI.color = new Color(0.1f, 0.11f, 0.14f, 0.96f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        var offerLine = "";
        for (var i = 0; i < _tradeOfferSlots.Count; i++)
        {
            var sid = _invSlots[_tradeOfferSlots[i]];
            offerLine += (sid ?? "?") + "x" + _tradeOfferQtys[i] + " ";
        }

        GUI.Label(new Rect(rect.x + 10, rect.y + 8, 340, 50),
            "Trade with " + _tradeTheirName + "\n" + _tradeOfferSummary +
            "\nItems: " + (string.IsNullOrEmpty(offerLine) ? "(LMB inventory)" : offerLine) +
            "\nYou " + (_tradeMyConfirm ? "READY" : "…") +
            " / Them " + (_tradeTheirConfirm ? "READY" : "…"));
        if (GUI.Button(new Rect(rect.x + 10, rect.y + 70, 80, 24), "+10g"))
        {
            _tradeMyGold += 10;
            SendTradeOffers();
        }

        if (GUI.Button(new Rect(rect.x + 100, rect.y + 70, 80, 24), "Clear items"))
        {
            _tradeOfferSlots.Clear();
            _tradeOfferQtys.Clear();
            SendTradeOffers();
        }

        if (GUI.Button(new Rect(rect.x + 190, rect.y + 70, 80, 24), "Confirm"))
        {
            _input?.RequestTradeConfirm();
        }

        if (GUI.Button(new Rect(rect.x + 280, rect.y + 70, 70, 24), "Cancel"))
        {
            _input?.RequestTradeCancel();
            _tradeId = "";
            _tradeOfferSlots.Clear();
            _tradeOfferQtys.Clear();
        }
    }

    private void SendTradeOffers()
    {
        var parts = new List<string>();
        for (var i = 0; i < _tradeOfferSlots.Count; i++)
        {
            parts.Add("{\"slotIndex\":" + _tradeOfferSlots[i] + ",\"quantity\":" + _tradeOfferQtys[i] + "}");
        }

        _input?.RequestTradeOfferRaw(_tradeMyGold, "[" + string.Join(",", parts) + "]");
    }

    private void DrawSkillTreePanel()
    {
        if (!_showSkillTree)
        {
            return;
        }

        var rect = new Rect(GuiW * 0.5f - 160f, 160f, 320f, 200f);
        GUI.color = new Color(0.09f, 0.1f, 0.14f, 0.96f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(rect.x + 8, rect.y + 6, 300, 20),
            "Skills — points: " + _skillPoints + " (Trainer / level-ups)");
        var y = rect.y + 30f;
        for (var i = 0; i < _unlockableSkills.Count && i < 6; i++)
        {
            var sid = _unlockableSkills[i];
            var need = 0;
            _unlockLevels.TryGetValue(sid, out need);
            var label = need > 0
                ? "Unlock " + SkillDisplayName(sid) + "  L" + need + "  (1pt)"
                : "Unlock " + SkillDisplayName(sid) + " (1pt)";
            if (GUI.Button(new Rect(rect.x + 8, y, 300, 22), label))
            {
                _input?.RequestSkillUnlock(sid);
            }

            y += 24f;
        }

        if (GUI.Button(new Rect(rect.x + 8, rect.y + rect.height - 28, 100, 22), "Exit"))
        {
            _showSkillTree = false;
        }
    }

    private void DrawAuctionPanel()
    {
        if (!_showAuction)
        {
            return;
        }

        var rect = new Rect(GuiW * 0.5f - 200f, 140f, 400f, 240f);
        GUI.color = new Color(0.09f, 0.1f, 0.14f, 0.96f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(rect.x + 8, rect.y + 6, 380, 18), "Auction (talk to Auctioneer Lira)");
        var y = rect.y + 28f;
        for (var i = 0; i < _auctionIds.Count && i < 6; i++)
        {
            if (GUI.Button(new Rect(rect.x + 8, y, 300, 22), "Buy " + _auctionLabels[i]))
            {
                _input?.RequestAuctionBuy(_auctionIds[i]);
            }

            y += 24f;
        }

        _auctionSellItem = GUI.TextField(new Rect(rect.x + 8, rect.y + rect.height - 56, 160, 22), _auctionSellItem ?? "item_dust");
        if (GUI.Button(new Rect(rect.x + 176, rect.y + rect.height - 56, 100, 22), "Sell 1 @20g"))
        {
            _input?.RequestAuctionSell(_auctionSellItem, 1, 20);
        }

        if (GUI.Button(new Rect(rect.x + 286, rect.y + rect.height - 56, 100, 22), "Refresh"))
        {
            _input?.RequestAuctionList();
        }

        if (GUI.Button(new Rect(rect.x + 8, rect.y + rect.height - 28, 100, 22), "Exit"))
        {
            _showAuction = false;
        }
    }

    private void DrawInstanceHud()
    {
        if (_instanceExpiresAt <= 0)
        {
            return;
        }

        var leftMs = _instanceExpiresAt - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (leftMs < 0)
        {
            leftMs = 0;
        }

        var mins = leftMs / 60000;
        var secs = (leftMs % 60000) / 1000;
        var urgent = leftMs < 30000;
        var phase = _bossPhase > 0 ? "  ·  phase " + _bossPhase : "";
        var y = 56f;
        var rect = new Rect(GuiW * 0.5f - 120f, y, 240f, 22f);
        GUI.color = urgent
            ? new Color(0.35f, 0.08f, 0.08f, 0.88f)
            : new Color(0.08f, 0.1f, 0.14f, 0.82f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = urgent ? new Color(1f, 0.55f, 0.4f, 1f) : new Color(0.85f, 0.9f, 1f, 1f);
        GUI.Label(rect, "  " + mins + ":" + secs.ToString("00") + phase);
        GUI.color = Color.white;
    }

    private void DrawBossFrame()
    {
        if (_world == null || !_world.TryGetBossFrame(out var name, out var hp, out var maxHp) || maxHp <= 0)
        {
            return;
        }

        var ratio = Mathf.Clamp01((float)hp / maxHp);
        var phase = ratio > 0.66f ? 1 : (ratio > 0.33f ? 2 : 3);
        var width = Mathf.Min(520f, GuiW - 80f);
        var rect = new Rect(GuiW * 0.5f - width * 0.5f, 8f, width, 44f);
        UiChrome.Panel(rect, new Color(0.85f, 0.2f, 0.22f, 1f));
        GUI.Label(new Rect(rect.x + 8, rect.y + 2, width - 16, 18), name + "  ·  Phase " + phase);
        var bar = new Rect(rect.x + 8, rect.y + 22, width - 16, 14);
        GUI.color = new Color(0.2f, 0.12f, 0.12f, 1f);
        GUI.DrawTexture(bar, Texture2D.whiteTexture);
        GUI.color = phase >= 3 ? new Color(0.95f, 0.25f, 0.15f) : new Color(0.85f, 0.2f, 0.22f);
        GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * ratio, bar.height), Texture2D.whiteTexture);
        GUI.color = new Color(1f, 0.85f, 0.4f, 0.9f);
        GUI.DrawTexture(new Rect(bar.x + bar.width * 0.33f, bar.y, 2f, bar.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(bar.x + bar.width * 0.66f, bar.y, 2f, bar.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(bar.x, bar.y - 1, bar.width, bar.height + 2), hp + " / " + maxHp);
    }

    private void ParseSkillLists(string json)
    {
        _unlockableSkills.Clear();
        _unlockedSkills.Clear();
        _unlockableSkills.AddRange(JsonUtil.ExtractStringArray(json, "unlockable"));
        _unlockedSkills.AddRange(JsonUtil.ExtractStringArray(json, "skillIds"));
        ParseUnlockLevels(json);
        ApplyClassSkillIds(json);
        ParseSkillCatalog(json);
    }

    private static void ExtractQuotedIds(string slice, List<string> into)
    {
        if (string.IsNullOrEmpty(slice))
        {
            return;
        }

        var cursor = 0;
        while (cursor < slice.Length)
        {
            var a = slice.IndexOf('"', cursor);
            if (a < 0)
            {
                break;
            }

            var b = slice.IndexOf('"', a + 1);
            if (b < 0)
            {
                break;
            }

            var s = slice.Substring(a + 1, b - a - 1);
            if (s.Length > 2 && !s.Contains(":") && s != "unlockable" && s != "skillIds" &&
                s != "classSkillIds")
            {
                into.Add(s);
            }

            cursor = b + 1;
        }
    }

    private void ParseAuction(string json)
    {
        _auctionIds.Clear();
        _auctionLabels.Clear();
        var idx = json.IndexOf("\"listings\"");
        if (idx < 0)
        {
            return;
        }

        var cursor = idx;
        while (true)
        {
            var idAt = json.IndexOf("\"id\":\"", cursor);
            if (idAt < 0 || idAt > idx + 5000)
            {
                break;
            }

            var slice = json.Substring(idAt, Math.Min(220, json.Length - idAt));
            var id = JsonUtil.ExtractString(slice, "id");
            var item = JsonUtil.ExtractString(slice, "itemId");
            var seller = JsonUtil.ExtractString(slice, "sellerName");
            JsonUtil.TryInt(slice, "price", out var price);
            JsonUtil.TryInt(slice, "quantity", out var qty);
            if (!string.IsNullOrEmpty(id))
            {
                _auctionIds.Add(id);
                _auctionLabels.Add((item ?? "?") + " x" + qty + " @" + price + "g (" + seller + ")");
            }

            cursor = idAt + 6;
        }
    }

    private void DrawFriendsPanel()
    {
        if (!_showFriends)
        {
            return;
        }

        var rect = new Rect(10f, 300f, 260f, 180f);
        GUI.color = new Color(0.09f, 0.1f, 0.14f, 0.95f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(rect.x + 8, rect.y + 6, 240, 18), "Friends (K) — RMB context Add");
        var y = rect.y + 28f;
        for (var i = 0; i < _friendNames.Count && i < 6; i++)
        {
            var line = (_friendOnline[i] ? "[on] " : "[off] ") + _friendNames[i];
            if (GUI.Button(new Rect(rect.x + 8, y, 170, 20), line))
            {
                if (_friendOnline[i] && !string.IsNullOrEmpty(_friendPlayerIds[i]))
                {
                    _whisperTargetName = _friendNames[i];
                    _chatFocused = true;
                }
            }

            if (GUI.Button(new Rect(rect.x + 184, y, 60, 20), "X"))
            {
                _input?.RequestFriendRemove(_friendTokens[i]);
            }

            y += 22f;
        }

        _guildCreateDraft = GUI.TextField(new Rect(rect.x + 8, rect.y + rect.height - 48, 150, 20), _guildCreateDraft ?? "");
        if (GUI.Button(new Rect(rect.x + 164, rect.y + rect.height - 48, 80, 20), "Create guild"))
        {
            _input?.RequestGuildCreate(_guildCreateDraft);
        }

        if (GUI.Button(new Rect(rect.x + 8, rect.y + rect.height - 24, 120, 20), "Leave guild"))
        {
            _input?.RequestGuildLeave();
        }
    }

    private void DrawSettingsPanel()
    {
        if (!_showSettings)
        {
            return;
        }

        var rect = new Rect(GuiW * 0.5f - 160f, GuiH * 0.5f - 180f, 320f, 360f);
        GUI.color = new Color(0.09f, 0.1f, 0.14f, 0.96f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(rect.x + 8, rect.y + 6, 300, 18), "Settings (Esc / U)");
        _showNameplates = GUI.Toggle(new Rect(rect.x + 8, rect.y + 32, 300, 20), _showNameplates, " Show nameplates");
        var music = GUI.Toggle(new Rect(rect.x + 8, rect.y + 54, 300, 20), _musicOn, " Map music");
        if (music != _musicOn)
        {
            _musicOn = music;
            PlayerPrefs.SetInt("gaaacha_music", _musicOn ? 1 : 0);
            PlayerPrefs.Save();
            SoundCatalog.SetMusicEnabled(_musicOn);
        }

        GUI.Label(new Rect(rect.x + 8, rect.y + 80, 300, 18), "UI scale " + _uiScale.ToString("0.0"));
        var prevScale = _uiScale;
        _uiScale = GUI.HorizontalSlider(new Rect(rect.x + 8, rect.y + 102, 300, 18), _uiScale, 0.8f, 1.4f);
        if (Mathf.Abs(_uiScale - prevScale) > 0.001f)
        {
            PersistUiScale();
        }

        GUI.Label(new Rect(rect.x + 8, rect.y + 126, 300, 18), "Resolution");
        var labels = new[] { "1280x720", "1600x900", "1920x1080", "Native" };
        for (var i = 0; i < labels.Length; i++)
        {
            var r = new Rect(rect.x + 8 + (i % 2) * 150, rect.y + 148 + (i / 2) * 26, 144, 22);
            var on = _resPresetIndex == i;
            if (GUI.Toggle(r, on, " " + labels[i]) && !on)
            {
                _resPresetIndex = i;
            }
        }

        if (GUI.Button(new Rect(rect.x + 8, rect.y + 208, 140, 26), "Apply resolution"))
        {
            ApplyResolutionPreset(_resPresetIndex);
        }

#if UNITY_EDITOR
        GUI.Label(new Rect(rect.x + 8, rect.y + 240, 300, 36),
            "Editor: set Game view size to match. Builds apply Screen.SetResolution.");
#else
        GUI.Label(new Rect(rect.x + 8, rect.y + 240, 300, 36),
            "Cam: MMB-drag yaw  |  WASD move  |  I inventory");
#endif

        if (GUI.Button(new Rect(rect.x + 8, rect.y + rect.height - 40, 140, 28), "Log out"))
        {
            LogoutToLogin();
        }

        if (GUI.Button(new Rect(rect.x + 160, rect.y + rect.height - 40, 140, 28), "Close"))
        {
            _showSettings = false;
        }
    }

    private void ApplyPityExtras(string pitySlice)
    {
        if (JsonUtil.TryInt(pitySlice, "softPityStart", out var soft))
        {
            _softPityStart = soft;
        }
        if (JsonUtil.TryNumber(pitySlice, "baseSsrRate", out var baseSsr))
        {
            _baseSsr = baseSsr;
        }
        if (JsonUtil.TryNumber(pitySlice, "baseSrRate", out var baseSr))
        {
            _baseSr = baseSr;
        }
        if (JsonUtil.TryInt(pitySlice, "pullCostDust", out var dustCost))
        {
            _pullCostDust = dustCost;
        }
        if (JsonUtil.TryInt(pitySlice, "tenPullCostDust", out var tenCost))
        {
            _tenPullCostDust = tenCost;
        }
        var bname = JsonUtil.ExtractString(pitySlice, "bannerName");
        if (!string.IsNullOrEmpty(bname))
        {
            _bannerName = bname;
        }
    }

    private static int CountJsonToken(string json, string token)
    {
        var n = 0;
        var i = 0;
        while (i < json.Length)
        {
            var found = json.IndexOf(token, i, System.StringComparison.Ordinal);
            if (found < 0)
            {
                break;
            }

            n += 1;
            i = found + token.Length;
        }

        return n;
    }

    private void DrawGachaPanel()
    {
        if (!_showGacha)
        {
            return;
        }

        var rect = new Rect(GuiW * 0.5f - 190f, GuiH * 0.5f - 220f, 380f, 440f);
        UiChrome.Panel(rect, UiChrome.Gold);
        GUI.color = Color.white;
        SpriteCatalog.DrawGui(new Rect(rect.x + 10, rect.y + 8, 360f, 72f), SpriteCatalog.BannerArt(), Color.white);
        GUI.Label(new Rect(rect.x + 10, rect.y + 84, 360, 22), _bannerName + "  (G to close)");
        GUI.Label(new Rect(rect.x + 10, rect.y + 108, 360, 20),
            "Pity " + _pity + " / " + _hardPity + "  (hard)");
        GUI.Label(new Rect(rect.x + 10, rect.y + 128, 360, 20),
            "Soft pity from pull " + _softPityStart);
        GUI.Label(new Rect(rect.x + 10, rect.y + 148, 360, 20),
            "Next SSR  " + (_nextSsr * 100f).ToString("0.0") + "%");
        GUI.Label(new Rect(rect.x + 10, rect.y + 168, 360, 36),
            "Rates  SSR " + (_baseSsr * 100f).ToString("0") + "%   SR " + (_baseSr * 100f).ToString("0") +
            "%   10-pull: at least 1 SR");
        GUI.Label(new Rect(rect.x + 10, rect.y + 204, 360, 20), "Last  " + _lastDrop);
        var lastRarity = UiChrome.Rarity(_lastDrop);
        UiChrome.Border(new Rect(rect.x + 8, rect.y + 202, 364, 22), lastRarity, 1.5f);
        if (!string.IsNullOrEmpty(_lastDrop))
        {
            SpriteCatalog.DrawGui(new Rect(rect.x + 330, rect.y + 198, 36f, 36f),
                _lastDrop.StartsWith("char_") || _lastDrop.StartsWith("card_")
                    ? SpriteCatalog.Portrait(_lastDrop)
                    : SpriteCatalog.ItemIcon(_lastDrop.Split(' ')[0]),
                Color.white);
        }
        if (!string.IsNullOrEmpty(_gachaSummary))
        {
            GUI.Label(new Rect(rect.x + 10, rect.y + 226, 360, 20), "This pull  " + _gachaSummary);
        }

        GUI.Label(new Rect(rect.x + 10, rect.y + 248, 360, 36),
            "Pool: portraits, spirits, gear. Classes are chosen in town.\nPortrait: " + _skin);
        GUI.Label(new Rect(rect.x + 10, rect.y + 286, 360, 20),
            "1-pull: " + _pullCostDust + " dust or 1 ticket   10-pull: " + _tenPullCostDust +
            " dust or 10 tickets");

        if (GUI.Button(new Rect(rect.x + 10, rect.y + 314, 170, 36), "Pull x1"))
        {
            SoundCatalog.Play(SoundCatalog.Id.UiClick);
            _input?.RequestGacha(1);
            _status = "sent gacha";
        }

        if (GUI.Button(new Rect(rect.x + 200, rect.y + 314, 170, 36), "Pull x10"))
        {
            SoundCatalog.Play(SoundCatalog.Id.UiClick);
            _input?.RequestGacha(10);
            _status = "sent 10-pull";
        }

        if (GUI.Button(new Rect(rect.x + 10, rect.y + rect.height - 40, 360, 28), "Close"))
        {
            SoundCatalog.Play(SoundCatalog.Id.UiClick);
            _showGacha = false;
        }
    }

    private string ClassCardHint()
    {
        if (_classId != "adventurer")
        {
            return "";
        }

        return _level < 20
            ? "  · Class Master at L20"
            : "  · Talk to the Class Master";
    }

    private void MaybeStartTutorial()
    {
        if (_tutorialStep >= 0)
        {
            return;
        }

        if (PlayerPrefs.GetInt("gaaacha_tutorial_done", 0) == 1)
        {
            return;
        }

        _tutorialStep = 0;
    }

    private void ApplySkillIdsFromState(string json)
    {
        ApplyClassSkillIds(json);
        var found = JsonUtil.ExtractStringArray(json, "skillIds");
        if (found.Count > 0)
        {
            _unlockedSkills.Clear();
            _unlockedSkills.AddRange(found);
        }
    }

    private void ApplyClassSkillIds(string json)
    {
        var found = JsonUtil.ExtractStringArray(json, "classSkillIds");
        if (found.Count > 0)
        {
            _skillIds = found.Count > 8 ? found.GetRange(0, 8).ToArray() : found.ToArray();
        }
        else if (_skillIds == null || _skillIds.Length == 0)
        {
            _skillIds = ClassSkills;
        }
    }

    private void ParseUnlockLevels(string json)
    {
        var slice = JsonUtil.SliceAround(json, "\"unlockLevels\"", 0, 900);
        if (string.IsNullOrEmpty(slice))
        {
            return;
        }

        _unlockLevels.Clear();
        var cursor = 0;
        while (cursor < slice.Length)
        {
            var q1 = slice.IndexOf('"', cursor);
            if (q1 < 0)
            {
                break;
            }

            var q2 = slice.IndexOf('"', q1 + 1);
            if (q2 < 0)
            {
                break;
            }

            var key = slice.Substring(q1 + 1, q2 - q1 - 1);
            cursor = q2 + 1;
            if (key == "unlockLevels" || key.Contains(":"))
            {
                continue;
            }

            var colon = slice.IndexOf(':', cursor);
            if (colon < 0 || colon > cursor + 8)
            {
                continue;
            }

            int n = 0;
            var num = "";
            for (var i = colon + 1; i < slice.Length; i++)
            {
                var c = slice[i];
                if (c == '-' || (c >= '0' && c <= '9'))
                {
                    num += c;
                }
                else if (num.Length > 0)
                {
                    break;
                }
            }

            if (int.TryParse(num, out n) && n > 0)
            {
                _unlockLevels[key] = n;
            }
        }
    }

    private void DrawLoadingScreen()
    {
        var left = _loadUntil - Time.unscaledTime;
        if (left <= 0f || string.IsNullOrEmpty(_loadMapTitle))
        {
            return;
        }

        var t = 1f - Mathf.Clamp01(left / 1.2f);
        var alpha = t < 0.62f ? 1f : Mathf.Clamp01(1f - (t - 0.62f) / 0.38f);
        GUI.color = new Color(0.04f, 0.04f, 0.06f, 0.92f * alpha);
        GUI.DrawTexture(new Rect(0f, 0f, GuiW, GuiH), Texture2D.whiteTexture);

        var panel = new Rect(GuiW * 0.5f - 170f, GuiH * 0.5f - 150f, 340f, 200f);
        GUI.color = new Color(0.12f, 0.12f, 0.16f, 0.97f * alpha);
        GUI.DrawTexture(panel, Texture2D.whiteTexture);
        GUI.color = new Color(1f, 1f, 1f, alpha);
        DrawLoadArt(new Rect(panel.x + 12f, panel.y + 12f, 316f, 132f));
        var title = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 22,
            fontStyle = FontStyle.Bold,
        };
        title.normal.textColor = new Color(1f, 0.95f, 0.82f, alpha);
        GUI.Label(new Rect(panel.x, panel.y + 148f, panel.width, 44f), _loadMapTitle, title);
        GUI.color = Color.white;
    }

    private void DrawLoadArt(Rect rect)
    {
        if (_loadArt != null && _loadArt.texture != null)
        {
            var tex = _loadArt.texture;
            var tr = _loadArt.textureRect;
            var uv = new Rect(
                tr.x / tex.width,
                tr.y / tex.height,
                tr.width / tex.width,
                tr.height / tex.height);
            GUI.DrawTextureWithTexCoords(rect, tex, uv);
            return;
        }

        GUI.color = new Color(0.28f, 0.32f, 0.4f, GUI.color.a);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
    }

    private void DrawDeathPanel()
    {
        if (!_showDeath && _hp > 0)
        {
            return;
        }

        if (_hp > 0)
        {
            _showDeath = false;
            return;
        }

        var rect = new Rect(GuiW * 0.5f - 180f, GuiH * 0.5f - 70f, 360f, 140f);
        GUI.color = new Color(0.12f, 0.05f, 0.05f, 0.96f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(rect.x + 12, rect.y + 12, 336, 40), "You died.");
        GUI.Label(new Rect(rect.x + 12, rect.y + 40, 336, 24), "Respawn at this map's spawn.");
        if (GUI.Button(new Rect(rect.x + 12, rect.y + 80, 336, 40), "Respawn"))
        {
            _input?.RequestRespawn();
            _showDeath = false;
            _status = "respawning";
        }
    }

    private void DrawClassChangePanel()
    {
        if (!_showClassChange)
        {
            return;
        }

        var rect = new Rect(GuiW * 0.5f - 190f, GuiH * 0.5f - 110f, 380f, 220f);
        GUI.color = new Color(0.12f, 0.1f, 0.05f, 0.97f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        SpriteCatalog.DrawGui(new Rect(rect.x + 12, rect.y + 12, 72f, 72f),
            SpriteCatalog.ClassCardArt(_classId), Color.white);
        GUI.Label(new Rect(rect.x + 96, rect.y + 10, 272, 28), "Class change — " + _classChangeTitle);
        GUI.Label(new Rect(rect.x + 96, rect.y + 42, 272, 70), _classChangeBody);
        if (GUI.Button(new Rect(rect.x + 12, rect.y + 170, 356, 32), "Continue"))
        {
            _showClassChange = false;
        }
    }

    private void DrawMinimapHud()
    {
        if (_world == null || !_inWorld || _gatePhase < 3 || _showFullMap)
        {
            _minimapArea = default;
            return;
        }

        var maxR = Mathf.Max(12, Mathf.Max(_world.MapWidth, _world.MapHeight));
        _minimapRadius = Mathf.Clamp(_minimapRadius, 8, maxR);
        const float boxInner = 148f;
        var span = _minimapRadius * 2 + 1;
        var cell = boxInner / span;
        _minimapCell = cell;
        var box = new Rect(GuiW - boxInner - 18f, 8f, boxInner + 10f, boxInner + 24f);
        UiChrome.Panel(box, UiChrome.Gold);
        GUI.Label(new Rect(box.x + 6f, box.y + 2f, box.width - 12f, 16f),
            MapPretty(_world.MapId) + "  M  ·  wheel");
        _minimapArea = new Rect(box.x + 5f, box.y + 18f, boxInner, boxInner);
        DrawMapPixels(_minimapArea, true, cell);
    }

    private void DrawFullMapOverlay()
    {
        if (!_showFullMap || _world == null || !_inWorld || _gatePhase < 3)
        {
            _fullMapArea = default;
            return;
        }

        GUI.color = new Color(0f, 0f, 0f, 0.72f);
        GUI.DrawTexture(new Rect(0f, 0f, GuiW, GuiH), Texture2D.whiteTexture);
        GUI.color = Color.white;

        var mw = Mathf.Max(1, _world.MapWidth);
        var mh = Mathf.Max(1, _world.MapHeight);
        var cell = Mathf.Max(3f, Mathf.Floor(Mathf.Min((GuiW - 80f) / mw, (GuiH - 80f) / mh)));
        var mapPxW = mw * cell;
        var mapPxH = mh * cell;
        var panel = new Rect(
            (GuiW - mapPxW) * 0.5f - 10f,
            (GuiH - mapPxH) * 0.5f - 24f,
            mapPxW + 20f,
            mapPxH + 58f);
        UiChrome.Panel(panel, UiChrome.Gold);
        GUI.Label(new Rect(panel.x + 10f, panel.y + 4f, panel.width - 90f, 18f),
            MapPretty(_world.MapId) + "  ·  gold you  red foe  cyan gate");
        GUI.Label(new Rect(panel.x + 10f, panel.y + panel.height - 18f, panel.width - 20f, 16f),
            "violet npc   blue player   dark wall   brown prop   orange hazard");
        if (GUI.Button(new Rect(panel.xMax - 78f, panel.y + 3f, 68f, 20f), "Close"))
        {
            _showFullMap = false;
            _status = "map closed";
        }

        DrawMapPixels(new Rect(panel.x + 10f, panel.y + 24f, mapPxW, mapPxH), false, cell);
        _fullMapArea = new Rect(panel.x + 10f, panel.y + 24f, mapPxW, mapPxH);
        _fullMapCell = cell;
    }

    private void DrawMapPixels(Rect area, bool followPlayer, float cell)
    {
        var cols = Mathf.Max(1, Mathf.RoundToInt(area.width / cell));
        var rows = Mathf.Max(1, Mathf.RoundToInt(area.height / cell));
        var px = MapPathing.TileOf(_x);
        var py = MapPathing.TileOf(_y);

        for (var gy = 0; gy < rows; gy++)
        {
            for (var gx = 0; gx < cols; gx++)
            {
                int tx;
                int ty;
                if (followPlayer)
                {
                    tx = px + gx - cols / 2;
                    ty = py + (rows / 2 - gy);
                }
                else
                {
                    tx = gx;
                    ty = rows - 1 - gy;
                }

                var r = new Rect(area.x + gx * cell, area.y + gy * cell, cell - 0.4f, cell - 0.4f);
                GUI.color = MinimapCellColor(tx, ty);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
            }
        }

        DrawMinimapMarks(area, followPlayer, cell, cols, rows);
        GUI.color = Color.white;
    }

    private void DrawMinimapMarks(Rect area, bool followPlayer, float cell, int cols, int rows)
    {
        if (_world == null)
        {
            return;
        }

        _minimapMarks.Clear();
        _world.CopyMinimapMarks(_minimapMarks);
        DrawMinimapMarksOf(area, followPlayer, cell, cols, rows, "npc");
        DrawMinimapMarksOf(area, followPlayer, cell, cols, rows, "portal");
        DrawMinimapMarksOf(area, followPlayer, cell, cols, rows, "monster");
        DrawMinimapMarksOf(area, followPlayer, cell, cols, rows, "player");
        DrawMinimapMarksOf(area, followPlayer, cell, cols, rows, "self");
    }

    private void DrawMinimapMarksOf(Rect area, bool followPlayer, float cell, int cols, int rows, string kind)
    {
        var size = Mathf.Max(3.2f, cell * (kind == "self" ? 1.15f : 0.85f));
        for (var i = 0; i < _minimapMarks.Count; i++)
        {
            var m = _minimapMarks[i];
            if (m.Kind != kind)
            {
                continue;
            }

            if (!TryWorldToMinimap(area, followPlayer, cell, cols, rows, m.X, m.Y, out var mx, out var my))
            {
                continue;
            }

            var mark = new Rect(mx - size * 0.5f, my - size * 0.5f, size, size);
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(mark.x - 1f, mark.y - 1f, mark.width + 2f, mark.height + 2f), Texture2D.whiteTexture);
            GUI.color = MinimapMarkColor(kind);
            GUI.DrawTexture(mark, Texture2D.whiteTexture);
        }
    }

    private bool TryWorldToMinimap(Rect area, bool followPlayer, float cell, int cols, int rows,
        float wx, float wy, out float mx, out float my)
    {
        if (followPlayer)
        {
            mx = area.x + (cols / 2 + 0.5f) * cell + (wx - _x) * cell;
            my = area.y + (rows / 2 + 0.5f) * cell - (wy - _y) * cell;
        }
        else
        {
            mx = area.x + (wx + 0.5f) * cell;
            my = area.y + (rows - 0.5f - wy) * cell;
        }

        return mx >= area.x - 2f && mx <= area.xMax + 2f && my >= area.y - 2f && my <= area.yMax + 2f;
    }

    private static Color MinimapMarkColor(string kind)
    {
        if (kind == "self")
        {
            return new Color(1f, 0.92f, 0.22f, 1f);
        }

        if (kind == "player")
        {
            return new Color(0.35f, 0.6f, 1f, 1f);
        }

        if (kind == "monster")
        {
            return new Color(1f, 0.22f, 0.16f, 1f);
        }

        if (kind == "portal")
        {
            return new Color(0.15f, 0.95f, 1f, 1f);
        }

        if (kind == "npc")
        {
            return new Color(0.82f, 0.42f, 1f, 1f);
        }

        return Color.white;
    }

    private Color MinimapCellColor(int tx, int ty)
    {
        if (_world == null || !_world.InMapBounds(tx, ty))
        {
            return new Color(0.04f, 0.04f, 0.06f, 1f);
        }

        if (_world.IsPropCell(tx, ty))
        {
            return new Color(0.52f, 0.34f, 0.16f, 1f);
        }

        if (_world.IsWallCell(tx, ty))
        {
            return new Color(0.06f, 0.07f, 0.09f, 1f);
        }

        if (_world.IsHazardCell(tx, ty))
        {
            return new Color(0.85f, 0.38f, 0.16f, 1f);
        }

        return MinimapFloorColor(_world.MapId, ((tx + ty) & 1) == 0);
    }

    private static Color MinimapFloorColor(string mapId, bool even)
    {
        var id = mapId ?? "";
        Color a;
        Color b;
        if (id.IndexOf("marsh") >= 0)
        {
            a = new Color(0.28f, 0.32f, 0.18f);
            b = new Color(0.22f, 0.26f, 0.14f);
        }
        else if (id.StartsWith("town"))
        {
            a = new Color(0.42f, 0.34f, 0.24f);
            b = new Color(0.48f, 0.48f, 0.46f);
        }
        else if (id.StartsWith("dungeon") || id.StartsWith("tower"))
        {
            a = new Color(0.32f, 0.28f, 0.30f);
            b = new Color(0.26f, 0.24f, 0.26f);
        }
        else if (id.StartsWith("field"))
        {
            a = new Color(0.28f, 0.48f, 0.22f);
            b = new Color(0.24f, 0.40f, 0.18f);
        }
        else
        {
            a = new Color(0.36f, 0.32f, 0.22f);
            b = new Color(0.32f, 0.28f, 0.18f);
        }

        return even ? a : b;
    }

    private static string MapPretty(string mapId)
    {
        return string.IsNullOrEmpty(mapId) ? "Map" : mapId.Replace('_', ' ');
    }

    private void DrawHelpPanel()
    {
        if (!_showHelp)
        {
            return;
        }

        var rect = new Rect(GuiW * 0.5f - 230f, GuiH * 0.18f, 460f, 340f);
        UiChrome.Panel(rect, UiChrome.Gold);
        GUI.color = Color.white;
        GUI.Label(new Rect(rect.x + 12, rect.y + 8, 436, 24), "Controls  (F1 to close)");
        GUI.Label(new Rect(rect.x + 12, rect.y + 36, 436, 280),
            "WASD / RMB  path  ·  minimap wheel zoom  ·  minimap/M RMB  route\n" +
            "Map: gold you  red foe  cyan gate  violet npc  blue player  dark wall\n" +
            "Tab  cycle foes    Shift+Tab  clear lock    click player  ally\n" +
            "Space  primary AA (sticky)    Z  offhand AA (sticky)    Rest sits + heals\n" +
            "Shift+1–0  emotes    I  inventory (drag / paperdoll / RMB)\n" +
            "Hotbar follows your class. Ground skills: click to aim.\n" +
            "R  food    G banner    J quests    M map    P spirit    Esc settings\n" +
            "Trainer spends skill points. Tomes grant +1 point.\n" +
            "Class cards at level 10 (or inventory class toggle).\n" +
            "F1 this help   F8 sprite audit   F9 log dump   F10 log verbosity");
    }

    private void DrawTutorialTips()
    {
        if (_tutorialStep < 0 || _tutorialStep >= TutorialTips.Length)
        {
            return;
        }

        var rect = new Rect(GuiW * 0.5f - 220f, GuiH - 168f, 440f, 70f);
        GUI.color = new Color(0.08f, 0.12f, 0.18f, 0.94f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(rect.x + 10, rect.y + 6, 300, 40),
            "Tip " + (_tutorialStep + 1) + "/" + TutorialTips.Length + "  " + TutorialTips[_tutorialStep]);
        if (GUI.Button(new Rect(rect.x + 320, rect.y + 18, 100, 36),
                _tutorialStep + 1 >= TutorialTips.Length ? "Done" : "Next"))
        {
            _tutorialStep += 1;
            if (_tutorialStep >= TutorialTips.Length)
            {
                PlayerPrefs.SetInt("gaaacha_tutorial_done", 1);
                PlayerPrefs.Save();
            }
        }
    }

    private void ApplyResolutionPreset(int index, bool savePrefs = true)
    {
        index = Mathf.Clamp(index, 0, ResPresets.Length - 1);
        _resPresetIndex = index;
        if (savePrefs)
        {
            PlayerPrefs.SetInt("gaaacha_res_preset", index);
            PlayerPrefs.Save();
        }

        var p = ResPresets[index];
        if (p.x <= 0 || p.y <= 0)
        {
            var w = Display.main != null ? Display.main.systemWidth : Screen.currentResolution.width;
            var h = Display.main != null ? Display.main.systemHeight : Screen.currentResolution.height;
            Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
            Screen.SetResolution(w, h, FullScreenMode.FullScreenWindow);
            _status = "resolution native " + w + "x" + h;
            return;
        }

        Screen.fullScreenMode = FullScreenMode.Windowed;
        Screen.SetResolution(p.x, p.y, FullScreenMode.Windowed);
#if UNITY_EDITOR
        _status = "resolution " + p.x + "x" + p.y + " (set Game view to match)";
#else
        _status = "resolution " + p.x + "x" + p.y;
#endif
    }

    private async void LogoutToLogin()
    {
        _showSettings = false;
        _showInventory = false;
        _inWorld = false;
        _gatePhase = 0;
        _charNameSet = false;
        _status = "logging out…";
        _world?.ClearSessionEntities();
        if (_net != null)
        {
            try
            {
                await _net.DisconnectAsync();
                await _net.ConnectAsync(NetClient.DefaultUrl);
                await _net.SendRawAsync(
                    "{\"type\":\"request_hello\",\"guestToken\":\"" + _guestToken + "\"}");
                _status = "logged out — choose login or guest";
            }
            catch (Exception ex)
            {
                _status = "logout reconnect failed: " + ex.Message;
            }
        }
    }

    private void ParsePartyMembersFull(string json)
    {
        _partyMemberIds.Clear();
        _partyMemberNames.Clear();
        _partyMemberHp.Clear();
        _partyMemberMaxHp.Clear();
        _partyMemberMp.Clear();
        _partyMemberMaxMp.Clear();
        _partyMemberLevel.Clear();
        _partyMemberClass.Clear();
        var names = new List<string>();
        var idx = json.IndexOf("\"members\"");
        if (idx < 0)
        {
            _partyMembersLine = "";
            return;
        }

        var cursor = idx;
        while (true)
        {
            var idAt = json.IndexOf("\"id\":\"", cursor);
            if (idAt < 0 || idAt > idx + 2500)
            {
                break;
            }

            var slice = json.Substring(idAt, Math.Min(220, json.Length - idAt));
            var id = JsonUtil.ExtractString(slice, "id");
            var name = JsonUtil.ExtractString(slice, "name");
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name))
            {
                cursor = idAt + 6;
                continue;
            }

            JsonUtil.TryInt(slice, "hp", out var hp);
            JsonUtil.TryInt(slice, "maxHp", out var maxHp);
            JsonUtil.TryInt(slice, "mp", out var mp);
            JsonUtil.TryInt(slice, "maxMp", out var maxMp);
            JsonUtil.TryInt(slice, "level", out var level);
            var classId = JsonUtil.ExtractString(slice, "classId") ?? "?";
            _partyMemberIds.Add(id);
            _partyMemberNames.Add(name);
            _partyMemberHp.Add(hp);
            _partyMemberMaxHp.Add(maxHp > 0 ? maxHp : 1);
            _partyMemberMp.Add(mp);
            _partyMemberMaxMp.Add(maxMp > 0 ? maxMp : 1);
            _partyMemberLevel.Add(level > 0 ? level : 1);
            _partyMemberClass.Add(classId);
            names.Add(name);
            cursor = idAt + 6;
        }

        _partyMembersLine = string.Join(", ", names);
    }

    private void ParseFriends(string json)
    {
        _friendNames.Clear();
        _friendTokens.Clear();
        _friendOnline.Clear();
        _friendPlayerIds.Clear();
        var idx = json.IndexOf("\"friends\"");
        if (idx < 0)
        {
            return;
        }

        var cursor = idx;
        while (true)
        {
            var tokAt = json.IndexOf("\"guestToken\":\"", cursor);
            if (tokAt < 0 || tokAt > idx + 4000)
            {
                break;
            }

            var slice = json.Substring(tokAt, Math.Min(200, json.Length - tokAt));
            var token = JsonUtil.ExtractString(slice, "guestToken");
            var name = JsonUtil.ExtractString(slice, "name");
            var playerId = JsonUtil.ExtractString(slice, "playerId");
            if (string.IsNullOrEmpty(token))
            {
                cursor = tokAt + 10;
                continue;
            }

            _friendTokens.Add(token);
            _friendNames.Add(string.IsNullOrEmpty(name) ? token : name);
            _friendOnline.Add(slice.Contains("\"online\":true"));
            _friendPlayerIds.Add(playerId == "null" ? "" : (playerId ?? ""));
            cursor = tokAt + 10;
        }
    }

    private static string ParsePartyMembers(string json)
    {
        var names = new List<string>();
        var idx = json.IndexOf("\"members\"");
        if (idx < 0)
        {
            return "";
        }

        var part = json.Substring(idx);
        var cursor = 0;
        while (cursor < part.Length && names.Count < 4)
        {
            var nIdx = part.IndexOf("\"name\":\"", cursor);
            if (nIdx < 0)
            {
                break;
            }

            var start = nIdx + "\"name\":\"".Length;
            var end = part.IndexOf('"', start);
            if (end < 0)
            {
                break;
            }

            names.Add(part.Substring(start, end - start));
            cursor = end + 1;
        }

        if (names.Count == 0)
        {
            return "";
        }

        var line = names[0];
        for (var i = 1; i < names.Count; i++)
        {
            line += ", " + names[i];
        }

        return line;
    }

    private void DrawToast()
    {
        if (string.IsNullOrEmpty(_comingSoonToast) || Time.time > _comingSoonUntil)
        {
            return;
        }

        var rect = new Rect(GuiW * 0.5f - 140f, GuiH * 0.35f, 280f, 40f);
        GUI.color = new Color(0.15f, 0.12f, 0.08f, 0.92f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(rect, _comingSoonToast);
    }

    private void DrawItemTooltip()
    {
        var text = !string.IsNullOrEmpty(_hoverItemTip) ? _hoverItemTip : _itemTooltip;
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var gui = GuiPointer();
        var w = 220f;
        var h = 18f + 16f * Mathf.Max(2, text.Split('\n').Length);
        var x = Mathf.Min(gui.x + 14f, GuiW - w - 8f);
        var y = Mathf.Min(gui.y + 16f, GuiH - h - 8f);
        var rect = new Rect(x, y, w, h);
        GUI.color = new Color(0.08f, 0.08f, 0.1f, 0.94f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = new Color(0.85f, 0.72f, 0.35f, 1f);
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 2f), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(rect.x + 8, rect.y + 6, rect.width - 14, rect.height - 10), text);
    }

    private void DrawBuffRow()
    {
        var x = 20f;
        var y = GuiH - 186f;
        for (var i = 0; i < _buffs.Count; i++)
        {
            var b = _buffs[i];
            var rem = Mathf.Max(0f, b.UntilLocal - Time.realtimeSinceStartup);
            if (rem <= 0f)
            {
                continue;
            }

            var rect = new Rect(x + i * 44f, y, 40f, 40f);
            UiChrome.Slot(rect, BuffIconColor(b.Kind, b.Id));
            GUI.color = Color.black;
            GUI.Label(new Rect(rect.x + 2, rect.y + 2, rect.width - 4, 22), BuffIconLabel(b.Kind, b.Id));
            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x + 2, rect.y + 22, rect.width - 4, 16), rem.ToString("0.0"));
        }
    }

    private static string BuffIconLabel(string kind, string id)
    {
        if (kind == "stun") return "STN";
        if (kind == "dot") return "DoT";
        if (id != null && id.StartsWith("food_")) return id.Contains("def") ? "DEF" : "ATK";
        if (kind == "attr_up" || (id != null && id.Contains("rally"))) return "RAL";
        if (kind == "dmg_taken_mult" || (id != null && id.Contains("decoy"))) return "DEC";
        if (kind == "blind") return "BLD";
        if (kind != null && kind.Contains("shield")) return "SHD";
        if (kind == "speed_mult") return "HST";
        if (kind == "elem_dmg_up") return "ELM";
        if (string.IsNullOrEmpty(kind)) return "?";
        return kind.Length <= 3 ? kind.ToUpperInvariant() : kind.Substring(0, 3).ToUpperInvariant();
    }

    private static Color BuffIconColor(string kind, string id)
    {
        if (kind == "stun") return new Color(0.95f, 0.82f, 0.2f, 0.92f);
        if (kind == "dot") return new Color(0.95f, 0.4f, 0.15f, 0.92f);
        if (id != null && id.StartsWith("food_")) return new Color(0.85f, 0.45f, 0.15f, 0.92f);
        if (kind == "attr_up" || (id != null && id.Contains("rally"))) return new Color(1f, 0.5f, 0.12f, 0.92f);
        if (kind == "dmg_taken_mult" || (id != null && id.Contains("decoy"))) return new Color(0.35f, 0.75f, 1f, 0.92f);
        if (kind == "blind") return new Color(0.45f, 0.45f, 0.5f, 0.92f);
        if (kind != null && kind.Contains("shield")) return new Color(0.35f, 0.9f, 0.85f, 0.92f);
        if (kind == "speed_mult") return new Color(0.55f, 0.95f, 0.3f, 0.92f);
        return new Color(0.55f, 0.35f, 0.7f, 0.92f);
    }

    private const float SkillSlotW = 58f;
    private const float SkillSlotH = 62f;
    private const float SkillSlotGap = 6f;
    private const float SkillBarX = 20f;
    private float SkillBarY => GuiH - 128f;

    private Rect SkillSlotRect(int index)
    {
        return new Rect(SkillBarX + index * (SkillSlotW + SkillSlotGap), SkillBarY, SkillSlotW, SkillSlotH);
    }

    private void SeedDefaultSkillHud()
    {
        SetSkillHud("auto_attack", "Auto Attack", 0, 1, 1.225f);
        SetSkillHud("shot", "Shot", 8, 1, 0.7f);
        SetSkillHud("shockwave", "Shockwave", 16, 1, 0.788f, "GROUND_CIRCLE", "hostile", "ground", 3.5f, 2.5f);
        SetSkillHud("dash", "Dash", 10, 0, 0.438f);
        SetSkillHud("rally", "Rally", 12, 0, 0.7f, "NO_TARGET", "friendly", "caster", 0f, 4f);
        SetSkillHud("hook_shot", "Hook Shot", 12, 1, 0.788f);
        SetSkillHud("mend", "Mend", 12, 0, 0.875f, "ALLY_TARGET", "friendly", "none", 5f, 0f);
        SetSkillHud("decoy", "Decoy", 14, 0, 0.613f);
        SetSkillHud("rest", "Rest", 0, 0, 0.7f);
        SetSkillHud("powerup", "Powerup", 10, 0, 0.875f);
        SetSkillHud("cleave", "Cleave", 12, 1, 0.788f, "GROUND_CIRCLE", "hostile", "ground", 2.2f, 2.4f);
        SetSkillHud("arrow_rain", "Arrow Rain", 14, 1, 0.875f, "GROUND_CIRCLE", "hostile", "ground", 5f, 2.8f);
        SetSkillHud("arcane_nova", "Arcane Nova", 16, 1, 0.875f, "GROUND_CIRCLE", "hostile", "ground", 4f, 2.6f);
        SetSkillHud("thunderstorm", "Thunderstorm", 18, 1, 0.963f, "GROUND_CIRCLE", "hostile", "ground", 5f, 3.2f);
        SetSkillHud("explosion", "Explosion", 20, 1, 1.05f, "GROUND_CIRCLE", "hostile", "ground", 4.5f, 3.4f);
        SetSkillHud("knife_fan", "Knife Fan", 10, 1, 0.7f, "SKILLSHOT_CONE", "hostile", "none", 3.2f, 0f);
    }

    private void SetSkillHud(string id, string name, int mana, int slot, float castSec,
        string targeting = null, string affects = null, string aoeOrigin = null, float range = 0f, float aoeRadius = 0f)
    {
        _skillHud[id] = new SkillHudInfo
        {
            Name = name,
            ManaCost = mana,
            WeaponSlot = slot,
            CastSec = castSec,
            TargetingType = targeting ?? "",
            Affects = affects ?? "",
            AoeOrigin = aoeOrigin ?? "",
            Range = range,
            AoeRadius = aoeRadius,
        };
        if (!_cooldownMs.ContainsKey(id))
        {
            _cooldownMs[id] = 1000f;
        }
    }

    private void ParseSkillCatalog(string json)
    {
        var slice = JsonUtil.SliceAround(json, "\"catalog\"", 0, 8000);
        if (string.IsNullOrEmpty(slice))
        {
            return;
        }

        var cursor = 0;
        while (true)
        {
            var idx = slice.IndexOf("\"id\":\"", cursor);
            if (idx < 0)
            {
                break;
            }

            var obj = slice.Substring(idx, Mathf.Min(420, slice.Length - idx));
            var id = JsonUtil.ExtractString(obj, "id");
            cursor = idx + 6;
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            var name = JsonUtil.ExtractString(obj, "name");
            JsonUtil.TryInt(obj, "manaCost", out var mana);
            JsonUtil.TryInt(obj, "weaponSlot", out var slot);
            JsonUtil.TryNumber(obj, "cooldownMs", out var cd);
            JsonUtil.TryNumber(obj, "range", out var range);
            JsonUtil.TryNumber(obj, "aoeRadius", out var aoeRadius);
            var targeting = JsonUtil.ExtractString(obj, "targetingType");
            var affects = JsonUtil.ExtractString(obj, "affects");
            var aoeOrigin = JsonUtil.ExtractString(obj, "aoeOrigin");
            var prev = _skillHud.TryGetValue(id, out var existing) ? existing : default;
            _skillHud[id] = new SkillHudInfo
            {
                Name = string.IsNullOrEmpty(name) ? (string.IsNullOrEmpty(prev.Name) ? ShortName(id) : prev.Name) : name,
                ManaCost = mana,
                WeaponSlot = slot,
                CastSec = prev.CastSec > 0.05f ? prev.CastSec : SkillCastSec(id),
                TargetingType = string.IsNullOrEmpty(targeting) ? prev.TargetingType : targeting,
                Affects = string.IsNullOrEmpty(affects) ? prev.Affects : affects,
                AoeOrigin = string.IsNullOrEmpty(aoeOrigin) ? prev.AoeOrigin : aoeOrigin,
                Range = range > 0f ? range : prev.Range,
                AoeRadius = aoeRadius > 0f ? aoeRadius : prev.AoeRadius,
            };
            if (cd > 0f)
            {
                _cooldownMs[id] = cd;
            }

            if (range > 0f)
            {
                SkillRanges[id] = range;
            }
        }
    }

    private void BeginLocalCast(string skillId)
    {
        _castSkillId = skillId;
        _castLabel = SkillDisplayName(skillId);
        _castDur = SkillCastSec(skillId);
        _castUntil = Time.time + _castDur;
        _castCommitUntil = Time.time + (_castDur * CastHitFrac);
        // Cooldown sweep comes from sync_cooldowns. Predicting CD here blocked
        // Space/AA after a rejected (out_of_range) cast and broke the chase pipeline.
    }

    private void CancelLocalCast()
    {
        _castSkillId = "";
        _castLabel = "";
        _castUntil = 0f;
        _castCommitUntil = 0f;
        _queuedSkillId = "";
        if (_world != null)
        {
            _world.InterruptStrike(_world.SelfId);
        }
    }

    private string SkillDisplayName(string id)
    {
        if (_skillHud.TryGetValue(id, out var info) && !string.IsNullOrEmpty(info.Name))
        {
            return info.Name;
        }

        return ShortName(id);
    }

    private int SkillManaCost(string id)
    {
        return _skillHud.TryGetValue(id, out var info) ? info.ManaCost : 0;
    }

    private int SkillWeaponSlot(string id)
    {
        return _skillHud.TryGetValue(id, out var info) ? info.WeaponSlot : 0;
    }

    private float SkillCastSec(string id)
    {
        if (_skillHud.TryGetValue(id, out var info) && info.CastSec > 0.05f)
        {
            return info.CastSec;
        }

        if (id == "auto_attack" || id == "auto_attack_off")
        {
            return 1.225f;
        }

        if (id == "dash")
        {
            return 0.438f;
        }

        return GrayBoxWorld.IsStrikeSkill(id) ? 0.788f : 0.613f;
    }

    private void DrawCastBar()
    {
        var rem = _castUntil - Time.time;
        if (rem <= 0f || string.IsNullOrEmpty(_castSkillId))
        {
            return;
        }

        var ratio = _castDur <= 0.01f ? 1f : 1f - Mathf.Clamp01(rem / _castDur);
        var w = 320f;
        var rect = new Rect(GuiW * 0.5f - w * 0.5f, SkillBarY - 26f, w, 16f);
        UiChrome.Bar(rect, ratio, UiChrome.Xp, "", _castLabel);
    }

    private void DrawSkillBar()
    {
        var count = _skillIds != null ? Mathf.Min(_skillIds.Length, 8) : 0;
        _hoverSkillId = "";
        var mouse = Event.current != null ? Event.current.mousePosition : Vector2.zero;
        for (var i = 0; i < count; i++)
        {
            var id = _skillIds[i];
            var rect = SkillSlotRect(i);
            var readyAt = _readyAtLocal.TryGetValue(id, out var ra) ? ra : 0f;
            var cdMs = _cooldownMs.TryGetValue(id, out var c) ? c : 1000f;
            var rem = Mathf.Max(0f, readyAt - Time.realtimeSinceStartup);
            var fill = cdMs <= 0 ? 0f : Mathf.Clamp01(rem / (cdMs / 1000f));
            var unlocked = SkillUnlocked(id);
            var mana = SkillManaCost(id);
            var oom = unlocked && mana > 0 && _mp < mana;
            var slot = SkillWeaponSlot(id);
            var missingHand = unlocked && slot == 2 && !HasSecondaryWeapon();

            UiChrome.Slot(rect, !unlocked
                ? new Color(0.3f, 0.3f, 0.34f, 1f)
                : Color.Lerp(UiChrome.Well, SkillColor(id), 0.55f));
            var iconTint = !unlocked
                ? new Color(0.45f, 0.45f, 0.48f, 1f)
                : rem > 0.04f
                    ? new Color(0.35f, 0.35f, 0.38f, 1f)
                    : missingHand
                        ? new Color(1f, 0.7f, 0.45f, 1f)
                        : oom
                            ? new Color(0.55f, 0.7f, 1f, 1f)
                            : Color.white;
            SpriteCatalog.DrawGui(
                new Rect(rect.x + 10f, rect.y + 6f, rect.width - 20f, 32f),
                SpriteCatalog.SkillIcon(id),
                iconTint);
            DrawCdSweep(rect, unlocked ? fill : 0f);
            if (unlocked && rem > 0.04f)
            {
                GUI.color = new Color(0.02f, 0.02f, 0.05f, 0.45f);
                GUI.DrawTexture(rect, Texture2D.whiteTexture);
            }
            if (unlocked && slot == 1)
            {
                DrawSlotEdge(rect, new Color(0.95f, 0.82f, 0.28f, 0.95f));
            }
            else if (unlocked && slot == 2)
            {
                DrawSlotEdge(rect, HasSecondaryWeapon()
                    ? new Color(0.55f, 0.82f, 0.95f, 0.95f)
                    : new Color(0.7f, 0.25f, 0.2f, 0.95f));
            }

            GUI.color = unlocked ? Color.white : new Color(0.7f, 0.7f, 0.72f);
            var key = i == 0 ? "Spc" : i.ToString();
            var body = key;
            if (!unlocked)
            {
                var need = 0;
                _unlockLevels.TryGetValue(id, out need);
                body = key + "\n" + (need > 0 ? "L" + need : "—");
            }

            GUI.Label(new Rect(rect.x + 3, rect.y + 2, rect.width - 6, 20), body);
            if (unlocked && rem > 0.04f)
            {
                var cdStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 22,
                    fontStyle = FontStyle.Bold,
                };
                GUI.color = Color.white;
                GUI.Label(rect, Mathf.CeilToInt(rem).ToString(), cdStyle);
            }
            var meta = "";
            if (mana > 0)
            {
                meta = mana + " MP";
            }

            if (slot == 1)
            {
                meta = string.IsNullOrEmpty(meta) ? "W1" : meta + "  W1";
            }
            else if (slot == 2)
            {
                var w2 = string.IsNullOrEmpty(_weapon2) || _weapon2 == "none" ? "W2!" : "W2";
                meta = string.IsNullOrEmpty(meta) ? w2 : meta + "  " + w2;
            }

            if (!string.IsNullOrEmpty(meta))
            {
                GUI.color = oom ? new Color(0.55f, 0.75f, 1f) : new Color(0.9f, 0.9f, 0.95f);
                GUI.Label(new Rect(rect.x + 2, rect.y + rect.height - 18, rect.width - 4, 16), meta);
            }

            if (rect.Contains(mouse))
            {
                _hoverSkillId = id;
            }
        }

        var restRect = SkillSlotRect(Mathf.Min(_skillIds != null ? _skillIds.Length : 8, 8));
        UiChrome.Slot(restRect, new Color(0.35f, 0.55f, 0.4f, 1f));
        GUI.color = Color.white;
        GUI.Label(new Rect(restRect.x + 3, restRect.y + 8, restRect.width - 6, 40), "Rest\nsit +2%");
        if (restRect.Contains(mouse))
        {
            _hoverSkillId = "rest";
        }

        DrawSkillTooltip();
        GUI.color = Color.white;
    }

    private void DrawSkillTooltip()
    {
        if (string.IsNullOrEmpty(_hoverSkillId))
        {
            return;
        }

        var id = _hoverSkillId;
        var idx = HotbarIndexOf(id);
        var slot = SkillWeaponSlot(id);
        var mana = SkillManaCost(id);
        var key = idx <= 0 ? "Space" : idx.ToString();
        var cd = _cooldownMs.TryGetValue(id, out var c) ? c / 1000f : 0f;
        var weapon = slot == 1
            ? "Mainhand — " + WeaponPretty(_weapon)
            : slot == 2
                ? "Offhand — " + WeaponPretty(_weapon2)
                : "No weapon slot";
        var unlocked = SkillUnlocked(id);
        var lockLine = "";
        if (!unlocked)
        {
            _unlockLevels.TryGetValue(id, out var need);
            lockLine = need > 0 ? "\nLocked until L" + need : "\nLocked";
        }

        var text = SkillDisplayName(id) + "  [" + key + "]" +
                   "\n" + weapon +
                   (mana > 0 ? "\nMP " + mana : "\nNo MP cost") +
                   (cd > 0f ? "\nCD " + cd.ToString("0.0") + "s" : "") +
                   lockLine;
        var tip = new Rect(SkillBarX, SkillBarY - 78f, 260f, 72f);
        GUI.color = new Color(0.08f, 0.09f, 0.12f, 0.95f);
        GUI.DrawTexture(tip, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(tip.x + 8, tip.y + 4, tip.width - 12, tip.height - 6), text);
    }

    private static void DrawCdSweep(Rect rect, float remaining01)
    {
        if (remaining01 <= 0.001f)
        {
            return;
        }

        GUI.color = new Color(0.02f, 0.02f, 0.05f, 0.62f);
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, rect.height * remaining01), Texture2D.whiteTexture);
        var cx = rect.x + rect.width * 0.5f;
        var cy = rect.y + rect.height * 0.5f;
        var prev = GUI.matrix;
        GUIUtility.RotateAroundPivot(-remaining01 * 360f, new Vector2(cx, cy));
        GUI.color = new Color(1f, 1f, 1f, 0.9f);
        GUI.DrawTexture(new Rect(cx - 1.5f, cy - rect.height * 0.48f, 3f, rect.height * 0.48f), Texture2D.whiteTexture);
        GUI.matrix = prev;
    }

    private static Color SkillColor(string id)
    {
        if (id == "auto_attack" || id == "auto_attack_off")
        {
            return new Color(0.75f, 0.75f, 0.35f, 0.9f);
        }
        if (NoTargetSkills.Contains(id) || id == "rally" || id == "mend" || id == "group_chant" || id == "ally_mend")
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
        switch (id)
        {
            case "auto_attack": return "AA";
            case "auto_attack_off": return "Off";
            case "shot": return "Shot";
            case "shockwave": return "Wave";
            case "dash": return "Dash";
            case "rally": return "Rally";
            case "hook_shot": return "Hook";
            case "mend": return "Mend";
            case "decoy": return "Decoy";
            case "rest": return "Rest";
            case "powerup": return "Power";
            case "cleave": return "Cleave";
            case "arrow_rain": return "Rain";
            case "arcane_nova": return "Nova";
            case "thunderstorm": return "Storm";
            case "explosion": return "Boom";
            case "knife_fan": return "Fan";
            default:
                if (string.IsNullOrEmpty(id)) return "?";
                return id.Length <= 6 ? id : id.Substring(0, 6);
        }
    }

    private static void DrawResourceBar(Rect rect, float ratio, Color fill, string label)
    {
        UiChrome.Bar(rect, ratio, fill, label, null);
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
        if (UnityEngine.Object.FindAnyObjectByType<NetworkBootstrap>() != null)
        {
            return;
        }

        new GameObject("NetworkBootstrap").AddComponent<NetworkBootstrap>();
    }
}
