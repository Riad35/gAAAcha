using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Drop into a Unity 6.3 project (Assets/Scripts/Network).
/// Wire Update/Start from a MonoBehaviour — this file stays Editor-free.
/// </summary>
public sealed class NetClient
{
    public const string DefaultUrl = "ws://127.0.0.1:7777";

    private ClientWebSocket _socket;
    private CancellationTokenSource _cts;
    private Task _receiveTask = Task.CompletedTask;
    private readonly Queue<string> _outbox = new Queue<string>();
    private string _pendingMove;
    private bool _draining;

    public bool IsConnected => _socket?.State == WebSocketState.Open;

    public event Action<string> MessageReceived;

    private int _moveSeq;

    public int LastMoveSeq => _moveSeq;

    public async Task ConnectAsync(string url = DefaultUrl)
    {
        await DisconnectAsync();
        _moveSeq = 0;
        _cts = new CancellationTokenSource();
        _socket = new ClientWebSocket();
        await _socket.ConnectAsync(new Uri(url), _cts.Token);
        _receiveTask = ReceiveLoop(_socket, _cts.Token);
    }

    public Task SendMoveAsync(float x, float y)
    {
        var inv = CultureInfo.InvariantCulture;
        _moveSeq += 1;
        EnqueueSend(
            "{\"type\":\"request_move\",\"x\":" + x.ToString("G9", inv) +
            ",\"y\":" + y.ToString("G9", inv) +
            ",\"seq\":" + _moveSeq + "}",
            coalesceMove: true);
        return Task.CompletedTask;
    }

    public Task SendPingAsync()
    {
        var inv = CultureInfo.InvariantCulture;
        var t = (Time.realtimeSinceStartupAsDouble * 1000.0).ToString("G9", inv);
        EnqueueSend("{\"type\":\"request_ping\",\"clientTime\":" + t + "}", false);
        return Task.CompletedTask;
    }

    public Task SendCastAsync(string skillId, string targetId)
    {
        EnqueueSend(
            $"{{\"type\":\"cast_skill\",\"skillId\":\"{skillId}\",\"targetId\":\"{targetId}\"}}",
            false);
        return Task.CompletedTask;
    }

    public Task SendGachaAsync(string bannerId = "starter", int count = 1)
    {
        EnqueueSend(
            $"{{\"type\":\"request_gacha\",\"bannerId\":\"{bannerId}\",\"count\":{count}}}",
            false);
        return Task.CompletedTask;
    }

    public Task SendRawAsync(string json)
    {
        EnqueueSend(json, coalesceMove: false);
        return Task.CompletedTask;
    }

    public async Task DisconnectAsync()
    {
        var socket = _socket;
        var cts = _cts;
        var receiveTask = _receiveTask;
        if (socket == null)
        {
            return;
        }

        try
        {
            cts?.Cancel();
            if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                }
                catch (WebSocketException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
            }

            try
            {
                await receiveTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
        finally
        {
            socket.Dispose();
            if (ReferenceEquals(_socket, socket))
            {
                _socket = null;
            }

            cts?.Dispose();
            if (ReferenceEquals(_cts, cts))
            {
                _cts = null;
            }
        }
    }

    private void EnqueueSend(string json, bool coalesceMove)
    {
        lock (_outbox)
        {
            if (coalesceMove)
            {
                _pendingMove = json;
            }
            else
            {
                _outbox.Enqueue(json);
            }

            if (_draining)
            {
                return;
            }

            _draining = true;
        }

        _ = DrainAsync();
    }

    private async Task DrainAsync()
    {
        try
        {
            while (true)
            {
                string next;
                lock (_outbox)
                {
                    if (_outbox.Count > 0)
                    {
                        next = _outbox.Dequeue();
                    }
                    else if (!string.IsNullOrEmpty(_pendingMove))
                    {
                        next = _pendingMove;
                        _pendingMove = null;
                    }
                    else
                    {
                        _draining = false;
                        return;
                    }
                }

                await SendAsync(next);
            }
        }
        finally
        {
            lock (_outbox)
            {
                if (_outbox.Count > 0 || !string.IsNullOrEmpty(_pendingMove))
                {
                    _draining = true;
                    _ = DrainAsync();
                }
                else
                {
                    _draining = false;
                }
            }
        }
    }

    private async Task SendAsync(string json)
    {
        var socket = _socket;
        var cts = _cts;
        if (socket == null || socket.State != WebSocketState.Open || cts == null)
        {
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(json);
        try
        {
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task ReceiveLoop(ClientWebSocket socket, CancellationToken token)
    {
        var buffer = new byte[8192];
        try
        {
            while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(buffer, token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                MessageReceived?.Invoke(Encoding.UTF8.GetString(buffer, 0, result.Count));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
