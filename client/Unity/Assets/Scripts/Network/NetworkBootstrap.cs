using System.Collections.Concurrent;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Gray-box harness: connects on Play, shows tiles + placeholders, sends keyboard input.
/// Uses the new Input System (project activeInputHandler = Input System Only).
/// </summary>
public sealed class NetworkBootstrap : MonoBehaviour
{
    private const float MoveStep = 1f;

    private NetClient _net;
    private InputSender _input;
    private GrayBoxWorld _world;
    private float _x = 2f;
    private float _y = 6f;
    private string _status = "starting…";
    private string _lastRecv = "(none)";
    private readonly ConcurrentQueue<string> _inbox = new ConcurrentQueue<string>();

    private void Awake()
    {
        _status = "Awake — waiting for Start()";
        _world = new GrayBoxWorld();
        Debug.Log("gAAAcha: NetworkBootstrap Awake");
    }

    private async void Start()
    {
        Debug.Log("gAAAcha: NetworkBootstrap Start — connecting…");
        _status = "connecting to " + NetClient.DefaultUrl;

        _net = new NetClient();
        _net.MessageReceived += OnMessageFromSocket;
        _input = new InputSender(_net);

        try
        {
            await _net.ConnectAsync(NetClient.DefaultUrl);
            _status = "CONNECTED — WASD move · 1/2/3 skills · G gacha";
            Debug.Log("gAAAcha: connected to " + NetClient.DefaultUrl);
        }
        catch (System.Exception ex)
        {
            _status = "CONNECT FAILED (local WASD still works): " + ex.Message;
            Debug.LogError("gAAAcha: connect failed — start server with npm run dev. " + ex.Message);
        }
    }

    private void Update()
    {
        while (_inbox.TryDequeue(out var json))
        {
            _lastRecv = json.Length > 120 ? json.Substring(0, 120) + "…" : json;
            Debug.Log("gAAAcha recv: " + json);
            _world.HandleMessage(json);
            ApplyAuthoritativeMove(json);
        }

        HandleMoveKeys();
        HandleActionKeys();
    }

    private void HandleMoveKeys()
    {
        var kb = Keyboard.current;
        if (kb == null)
        {
            return;
        }

        var dx = 0f;
        var dy = 0f;
        if (kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame)
        {
            dy = MoveStep;
        }
        else if (kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame)
        {
            dy = -MoveStep;
        }
        else if (kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame)
        {
            dx = -MoveStep;
        }
        else if (kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame)
        {
            dx = MoveStep;
        }

        if (dx == 0f && dy == 0f)
        {
            return;
        }

        _x += dx;
        _y += dy;
        _world.SetLocalPos(_x, _y);
        _status = "move " + _x + "," + _y;
        if (_input != null && _net != null && _net.IsConnected)
        {
            _input.RequestMove(_x, _y);
            _status = "sent move " + _x + "," + _y;
        }
    }

    private void HandleActionKeys()
    {
        var kb = Keyboard.current;
        if (kb == null || _input == null || _net == null || !_net.IsConnected)
        {
            return;
        }

        if (kb.digit1Key.wasPressedThisFrame)
        {
            _input.CastSlash();
            _status = "sent slash";
        }
        if (kb.digit2Key.wasPressedThisFrame)
        {
            _input.CastShot();
            _status = "sent shot";
        }
        if (kb.digit3Key.wasPressedThisFrame)
        {
            _input.CastMend();
            _status = "sent mend";
        }
        if (kb.gKey.wasPressedThisFrame)
        {
            _input.RequestGacha(1);
            _status = "sent gacha";
        }
    }

    private void OnGUI()
    {
        var style = new GUIStyle(GUI.skin.box)
        {
            fontSize = 18,
            alignment = TextAnchor.UpperLeft,
            wordWrap = true,
            padding = new RectOffset(12, 12, 12, 12),
        };
        GUI.Box(new Rect(10, 10, Screen.width - 20, 120),
            "gAAAcha gray-box\n" +
            "Status: " + _status + "\n" +
            "Pos: " + _x + ", " + _y + "  |  WASD · 1/2/3 · G\n" +
            "Last: " + _lastRecv,
            style);
    }

    private void OnMessageFromSocket(string json)
    {
        _inbox.Enqueue(json);
    }

    private void ApplyAuthoritativeMove(string json)
    {
        if (!json.Contains("\"type\":\"sync_move\""))
        {
            return;
        }

        if (!TryReadNumberAfter(json, "\"x\":", out var serverX))
        {
            return;
        }
        if (!TryReadNumberAfter(json, "\"y\":", out var serverY))
        {
            return;
        }

        _x = serverX;
        _y = serverY;
        _status = "sync_move " + _x + "," + _y;
    }

    private static bool TryReadNumberAfter(string json, string token, out float value)
    {
        value = 0f;
        var start = json.IndexOf(token);
        if (start < 0)
        {
            return false;
        }

        start += token.Length;
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

    private async void OnDestroy()
    {
        if (_net == null)
        {
            return;
        }

        _net.MessageReceived -= OnMessageFromSocket;
        await _net.DisconnectAsync();
    }
}

public static class NetworkBootstrapLoader
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureBootstrap()
    {
        Debug.Log("gAAAcha: NetworkBootstrapLoader running");
        if (Object.FindAnyObjectByType<NetworkBootstrap>() != null)
        {
            return;
        }

        var go = new GameObject("NetworkBootstrap");
        go.AddComponent<NetworkBootstrap>();
        Debug.Log("gAAAcha: NetworkBootstrap spawned");
    }
}
