using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Patch;

namespace GoXlrControl.Hardware;

public sealed class WebSocketStatusClient : IAsyncDisposable
{
    private readonly ConcurrentDictionary<uint, TaskCompletionSource<JsonNode>> _pending = new();
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveLoop;
    private uint _nextId = 1;
    private JsonNode? _status;
    private int _disconnectSignaled;

    public JsonNode? Status => _status;
    public bool IsConnected => _socket?.State == WebSocketState.Open;

    public event EventHandler<JsonNode>? StatusUpdated;
    public event EventHandler<string>? MessageLogged;
    public event EventHandler? Disconnected;

    public async Task ConnectAsync(Uri websocketUri, CancellationToken ct = default)
    {
        await DisposeSocketAsync().ConfigureAwait(false);
        _disconnectSignaled = 0;
        _socket = new ClientWebSocket();
        await _socket.ConnectAsync(websocketUri, ct).ConfigureAwait(false);
        _receiveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token), CancellationToken.None);

        var statusResponse = await SendRequestAsync(JsonNode.Parse("\"GetStatus\"")!, ct).ConfigureAwait(false);
        if (statusResponse["Status"] is JsonNode status)
        {
            _status = status.DeepClone();
            StatusUpdated?.Invoke(this, _status);
        }
    }

    public async Task<JsonNode> SendRequestAsync(JsonNode data, CancellationToken ct = default)
    {
        if (_socket is null || _socket.State != WebSocketState.Open)
            throw new InvalidOperationException("WebSocket ist nicht verbunden.");

        var id = _nextId++;
        var tcs = new TaskCompletionSource<JsonNode>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        var envelope = new JsonObject
        {
            ["id"] = id,
            ["data"] = data.DeepClone()
        };

        var bytes = Encoding.UTF8.GetBytes(envelope.ToJsonString());
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);

        using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
        try
        {
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        var socket = _socket!;
        try
        {
            while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        SignalDisconnected();
                        return;
                    }

                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                var json = Encoding.UTF8.GetString(ms.ToArray());
                try
                {
                    HandleMessage(json);
                }
                catch (Exception ex)
                {
                    // Message/patch errors must not tear down the socket — that caused a reconnect storm
                    // when Utility replied with data: "Ok" (JsonValue) and code indexed ["Patch"] on it.
                    MessageLogged?.Invoke(this, $"WebSocket message error: {ex.Message}");
                }
            }

            if (!ct.IsCancellationRequested)
                SignalDisconnected();
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
        catch (Exception ex)
        {
            MessageLogged?.Invoke(this, $"WebSocket receive error: {ex.Message}");
            SignalDisconnected();
        }
    }

    private void SignalDisconnected()
    {
        if (Interlocked.Exchange(ref _disconnectSignaled, 1) != 0) return;
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    private void HandleMessage(string json)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (Exception ex)
        {
            MessageLogged?.Invoke(this, $"Ungültiges WS-JSON: {ex.Message}");
            return;
        }

        if (node is null) return;

        if (node["id"] is JsonValue idValue && idValue.TryGetValue<uint>(out var id) &&
            node["data"] is JsonNode data)
        {
            // Utility command replies are often the string "Ok" (JsonValue), not an object.
            // Indexing ["Patch"] on a JsonValue throws InvalidOperationException.
            if (data is JsonObject dataObj && dataObj["Patch"] is JsonArray patchArray)
            {
                ApplyPatch(patchArray);
                return;
            }

            if (_pending.TryRemove(id, out var tcs))
                tcs.TrySetResult(data);
            return;
        }

        if (node["data"] is JsonObject bareObj && bareObj["Patch"] is JsonArray barePatch)
            ApplyPatch(barePatch);
    }

    private void ApplyPatch(JsonArray patchArray)
    {
        if (_status is null) return;
        try
        {
            var patchJson = patchArray.ToJsonString();
            var patch = JsonSerializer.Deserialize<JsonPatch>(patchJson);
            if (patch is null) return;
            var result = patch.Apply(_status);
            if (result.IsSuccess && result.Result is not null)
            {
                _status = result.Result;
                StatusUpdated?.Invoke(this, _status);
            }
            else
            {
                MessageLogged?.Invoke(this, $"Patch fehlgeschlagen: {result.Error}");
            }
        }
        catch (Exception ex)
        {
            MessageLogged?.Invoke(this, $"Patch-Fehler: {ex.Message}");
        }
    }

    private async Task DisposeSocketAsync()
    {
        try
        {
            _receiveCts?.Cancel();
            if (_receiveLoop is not null)
                await Task.WhenAny(_receiveLoop, Task.Delay(500)).ConfigureAwait(false);
        }
        catch { /* ignore */ }

        _receiveCts?.Dispose();
        _receiveCts = null;
        _receiveLoop = null;

        if (_socket is not null)
        {
            try
            {
                if (_socket.State == WebSocketState.Open)
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
            }
            catch { /* ignore */ }
            _socket.Dispose();
            _socket = null;
        }

        foreach (var pending in _pending.Values)
            pending.TrySetCanceled();
        _pending.Clear();
    }

    public async ValueTask DisposeAsync() => await DisposeSocketAsync().ConfigureAwait(false);
}
