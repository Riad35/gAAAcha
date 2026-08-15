using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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

    public bool IsConnected => _socket?.State == WebSocketState.Open;

    public event Action<string> MessageReceived;

    public async Task ConnectAsync(string url = DefaultUrl)
    {
        await DisconnectAsync();
        _cts = new CancellationTokenSource();
        _socket = new ClientWebSocket();
        await _socket.ConnectAsync(new Uri(url), _cts.Token);
        _receiveTask = ReceiveLoop(_socket, _cts.Token);
    }

    public Task SendMoveAsync(float x, float y)
    {
        return SendAsync($"{{\"type\":\"request_move\",\"x\":{x},\"y\":{y}}}");
    }

    public Task SendCastAsync(string skillId, string targetId)
    {
        return SendAsync(
            $"{{\"type\":\"cast_skill\",\"skillId\":\"{skillId}\",\"targetId\":\"{targetId}\"}}");
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
