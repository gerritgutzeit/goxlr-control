using System.Text.Json.Nodes;
using GoXlrControl.Hardware.Abstractions;
using Microsoft.Extensions.Logging;

namespace GoXlrControl.Hardware;

public sealed class GoXlrUtilityProvider : IHardwareInputProvider, IHardwareOutputController
{
    private static readonly TimeSpan HttpReprobeInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StopWaitTimeout = TimeSpan.FromSeconds(5);

    private readonly ILogger<GoXlrUtilityProvider>? _logger;
    private readonly bool _autoStartDaemon;
    private readonly Dictionary<(string Serial, FaderId Fader), FaderSnapshot> _lastFaders = new();
    private readonly Dictionary<(string Serial, HardwareButtonId Button), bool> _lastButtons = new();
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly SemaphoreSlim _publishGate = new(1, 1);
    private readonly object _lifecycleGate = new();
    private CancellationTokenSource? _runCts;
    private Task? _runTask;
    private WebSocketStatusClient? _ws;
    private List<HardwareDeviceInfo> _devices = new();
    private DateTimeOffset _nextAutoStartAttempt = DateTimeOffset.MinValue;

    public GoXlrUtilityProvider(ILogger<GoXlrUtilityProvider>? logger = null, bool autoStartDaemon = true)
    {
        _logger = logger;
        _autoStartDaemon = autoStartDaemon;
    }

    public HardwareConnectionState ConnectionState { get; private set; } = HardwareConnectionState.Disconnected;
    public IReadOnlyList<HardwareDeviceInfo> Devices => _devices;
    public bool CanSendCommands =>
        ConnectionState is HardwareConnectionState.Connected or HardwareConnectionState.HttpDisabled
        && !string.IsNullOrEmpty(ActiveSerial);
    public string? ActiveSerial => _devices.FirstOrDefault()?.SerialNumber;

    public event EventHandler<HardwareConnectionState>? ConnectionChanged;
    public event EventHandler<FaderValueChanged>? FaderChanged;
    public event EventHandler<ButtonStateChanged>? ButtonChanged;
    public event EventHandler<string>? DiagnosticMessage;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_lifecycleGate)
        {
            if (_runTask is { IsCompleted: false })
            {
                Log("Hardware-Start ignoriert — Lauf bereits aktiv.");
                return Task.CompletedTask;
            }

            _runCts?.Dispose();
            _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _runTask = Task.Run(() => RunAsync(_runCts.Token), CancellationToken.None);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task? runTask;
        CancellationTokenSource? cts;
        lock (_lifecycleGate)
        {
            cts = _runCts;
            runTask = _runTask;
            _runCts = null;
            _runTask = null;
        }

        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // already disposed
        }

        if (runTask is not null)
        {
            var finished = await Task.WhenAny(runTask, Task.Delay(StopWaitTimeout, cancellationToken))
                .ConfigureAwait(false);
            if (finished != runTask)
                Log("Hardware-Stop: Lauf beendete nicht innerhalb des Timeouts — Fortsetzen nach Cleanup.");
            else
            {
                try
                {
                    await runTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // expected on cancel
                }
                catch (Exception ex)
                {
                    Log($"Hardware-Stop: Lauf endete mit Fehler: {ex.Message}");
                }
            }
        }

        WebSocketStatusClient? ws;
        lock (_lifecycleGate)
        {
            ws = _ws;
            _ws = null;
        }

        if (ws is not null)
            await ws.DisposeAsync().ConfigureAwait(false);

        ClearCaches();
        SetState(HardwareConnectionState.Disconnected);
        cts?.Dispose();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var delay = TimeSpan.FromMilliseconds(250);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (UtilityDaemonLifecycle.IsOfficialAppRunning())
                {
                    SetState(HardwareConnectionState.OfficialAppConflict);
                    Log("Offizielle GoXLR App erkannt — Utility kann nicht parallel laufen.");
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                    delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 10));
                    continue;
                }

                if (!UtilityDaemonLifecycle.IsDaemonRunning())
                {
                    SetState(HardwareConnectionState.UtilityMissing);
                    if (_autoStartDaemon && DateTimeOffset.UtcNow >= _nextAutoStartAttempt)
                    {
                        _nextAutoStartAttempt = DateTimeOffset.UtcNow.AddSeconds(30);
                        var start = await UtilityDaemonLifecycle.TryStartDaemonAsync(
                            cancellationToken: ct,
                            log: Log).ConfigureAwait(false);
                        if (start is UtilityStartResult.Started or UtilityStartResult.AlreadyRunning)
                        {
                            delay = TimeSpan.FromMilliseconds(250);
                            continue;
                        }

                        if (start == UtilityStartResult.NotInstalled)
                            Log("GoXLR Utility nicht installiert — Patcher in Settings/Wizard nutzen.");
                        else
                            Log("GoXLR Utility Daemon nicht gefunden.");
                    }
                    else
                    {
                        Log("GoXLR Utility Daemon nicht gefunden.");
                    }

                    await Task.Delay(delay, ct).ConfigureAwait(false);
                    delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 10));
                    continue;
                }

                SetState(ConnectionState is HardwareConnectionState.Disconnected or HardwareConnectionState.UtilityMissing
                    ? HardwareConnectionState.Connecting
                    : HardwareConnectionState.Reconnecting);

                await using var pipe = new NamedPipeTransport();
                await pipe.ConnectAsync(NamedPipeTransport.DefaultPipeName, 2000, ct).ConfigureAwait(false);
                var response = await pipe.GetStatusAsync(ct).ConfigureAwait(false);
                var status = response["Status"] ?? response;
                await PublishStatusAsync(status, isInitial: true, ct).ConfigureAwait(false);

                if (!DaemonStatusParser.IsHttpEnabled(status))
                {
                    SetState(HardwareConnectionState.HttpDisabled);
                    Log("Utility-HTTP/WebSocket deaktiviert — Pipe-Polling aktiv.");
                    await PollPipeAsync(ct).ConfigureAwait(false);
                    continue;
                }

                var (host, port) = DaemonStatusParser.GetHttpEndpoint(status);
                var wsUri = new Uri($"ws://{host}:{port}/api/websocket");
                var ws = new WebSocketStatusClient();
                lock (_lifecycleGate)
                    _ws = ws;
                ws.MessageLogged += (_, msg) => Log(msg);
                ws.StatusUpdated += (_, s) => _ = PublishStatusAsync(s, isInitial: false, CancellationToken.None);
                var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                ws.Disconnected += (_, _) => disconnected.TrySetResult();

                await ws.ConnectAsync(wsUri, ct).ConfigureAwait(false);
                if (_devices.Count == 0)
                    SetState(HardwareConnectionState.NoDevice);
                else
                    SetState(HardwareConnectionState.Connected);

                delay = TimeSpan.FromMilliseconds(250);
                await disconnected.Task.WaitAsync(ct).ConfigureAwait(false);
                Log("WebSocket getrennt — Reconnect.");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log($"Verbindungsfehler: {ex.Message}");
                SetState(HardwareConnectionState.Reconnecting);
                await Task.Delay(delay, ct).ConfigureAwait(false);
                delay = TimeSpan.FromSeconds(Math.Min(Math.Max(delay.TotalSeconds * 2, 0.5), 10));
            }
            finally
            {
                WebSocketStatusClient? ws;
                lock (_lifecycleGate)
                {
                    ws = _ws;
                    _ws = null;
                }

                if (ws is not null)
                    await ws.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task PollPipeAsync(CancellationToken ct)
    {
        var nextHttpReprobe = DateTimeOffset.UtcNow + HttpReprobeInterval;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeTransport();
                await pipe.ConnectAsync(ct: ct).ConfigureAwait(false);
                var response = await pipe.GetStatusAsync(ct).ConfigureAwait(false);
                var status = response["Status"] ?? response;
                await PublishStatusAsync(status, isInitial: false, ct).ConfigureAwait(false);
                if (_devices.Count == 0)
                    SetState(HardwareConnectionState.NoDevice);
                else
                    SetState(HardwareConnectionState.HttpDisabled);

                if (DateTimeOffset.UtcNow >= nextHttpReprobe)
                {
                    nextHttpReprobe = DateTimeOffset.UtcNow + HttpReprobeInterval;
                    if (DaemonStatusParser.IsHttpEnabled(status))
                    {
                        Log("Utility-HTTP wieder aktiv — wechsle zu WebSocket.");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Pipe-Poll Fehler: {ex.Message}");
                throw;
            }

            await Task.Delay(100, ct).ConfigureAwait(false);
        }
    }

    private async Task PublishStatusAsync(JsonNode status, bool isInitial, CancellationToken ct)
    {
        await _publishGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _devices = DaemonStatusParser.ParseDevices(status).ToList();
            var now = DateTimeOffset.UtcNow;

            foreach (var device in _devices)
            {
                foreach (var fader in DaemonStatusParser.ParseFaders(status, device.SerialNumber))
                {
                    var key = (device.SerialNumber, fader.Fader);
                    if (!isInitial && _lastFaders.TryGetValue(key, out var prev) &&
                        prev.RawVolume == fader.RawVolume && prev.ChannelName == fader.ChannelName)
                        continue;

                    _lastFaders[key] = fader;
                    FaderChanged?.Invoke(this, new FaderValueChanged(
                        device.SerialNumber, fader.Fader, fader.ChannelName,
                        fader.Normalized, fader.RawVolume, now, isInitial));
                }

                foreach (var button in DaemonStatusParser.ParseButtons(status, device.SerialNumber))
                {
                    var key = (device.SerialNumber, button.Button);
                    if (!isInitial && _lastButtons.TryGetValue(key, out var prev) && prev == button.IsPressed)
                        continue;

                    _lastButtons[key] = button.IsPressed;
                    ButtonChanged?.Invoke(this, new ButtonStateChanged(
                        device.SerialNumber, button.Button, button.IsPressed, now, isInitial));
                }
            }

            if (!isInitial && _devices.Count == 0 && ConnectionState == HardwareConnectionState.Connected)
                SetState(HardwareConnectionState.NoDevice);
        }
        finally
        {
            _publishGate.Release();
        }
    }

    private void ClearCaches()
    {
        EmitSyntheticButtonReleases();
        _lastFaders.Clear();
        _lastButtons.Clear();
        _devices = new List<HardwareDeviceInfo>();
    }

    /// <summary>
    /// If a physical button was held when the device disconnects, listeners must see a release
    /// so Engine/Lighting do not stay stuck in a pressed state.
    /// </summary>
    private void EmitSyntheticButtonReleases()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var ((serial, button), pressed) in _lastButtons.ToList())
        {
            if (!pressed) continue;
            _lastButtons[(serial, button)] = false;
            ButtonChanged?.Invoke(this, new ButtonStateChanged(serial, button, false, now, IsInitial: false));
        }
    }

    private void SetState(HardwareConnectionState state)
    {
        if (ConnectionState == state) return;
        ConnectionState = state;
        ConnectionChanged?.Invoke(this, state);
    }

    private void Log(string message)
    {
        _logger?.LogInformation("{Message}", message);
        DiagnosticMessage?.Invoke(this, message);
    }

    public Task SetFaderDisplayStyleAsync(FaderId fader, FaderDisplayStyle style, CancellationToken ct = default)
    {
        var serial = RequireSerial();
        return SendCommandAsync(GoXlrCommandBuilder.SetFaderDisplayStyle(serial, fader, style), ct);
    }

    public Task SetFaderColoursAsync(FaderId fader, string colourOneHex, string colourTwoHex, CancellationToken ct = default)
    {
        var serial = RequireSerial();
        return SendCommandAsync(GoXlrCommandBuilder.SetFaderColours(serial, fader, colourOneHex, colourTwoHex), ct);
    }

    public Task SetAllFaderColoursAsync(string colourOneHex, string colourTwoHex, CancellationToken ct = default)
    {
        var serial = RequireSerial();
        return SendCommandAsync(GoXlrCommandBuilder.SetAllFaderColours(serial, colourOneHex, colourTwoHex), ct);
    }

    public Task SetButtonColoursAsync(HardwareButtonId button, string colourOneHex, string colourTwoHex, CancellationToken ct = default)
    {
        var serial = RequireSerial();
        return SendCommandAsync(GoXlrCommandBuilder.SetButtonColours(serial, button, colourOneHex, colourTwoHex), ct);
    }

    public Task SetButtonOffStyleAsync(HardwareButtonId button, LightingOffStyle style, CancellationToken ct = default)
    {
        var serial = RequireSerial();
        return SendCommandAsync(GoXlrCommandBuilder.SetButtonOffStyle(serial, button, style), ct);
    }

    public Task SetAnimationModeAsync(AnimationMode mode, CancellationToken ct = default)
    {
        var serial = RequireSerial();
        return SendCommandAsync(GoXlrCommandBuilder.SetAnimationMode(serial, mode), ct);
    }

    private string RequireSerial() =>
        ActiveSerial ?? throw new InvalidOperationException("Kein GoXLR-Gerät verbunden.");

    private async Task SendCommandAsync(JsonNode command, CancellationToken ct)
    {
        if (!CanSendCommands)
            throw new InvalidOperationException($"Commands nicht möglich im Zustand {ConnectionState}.");

        await _commandGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            WebSocketStatusClient? ws;
            lock (_lifecycleGate)
                ws = _ws;

            if (ws is { IsConnected: true })
            {
                var response = await ws.SendRequestAsync(command, ct).ConfigureAwait(false);
                ThrowIfError(response);
                return;
            }

            await using var pipe = new NamedPipeTransport();
            await pipe.ConnectAsync(ct: ct).ConfigureAwait(false);
            var pipeResponse = await pipe.SendAsync(command, ct).ConfigureAwait(false);
            ThrowIfError(pipeResponse);
        }
        finally
        {
            _commandGate.Release();
        }
    }

    private static void ThrowIfError(JsonNode response)
    {
        // Utility often returns the bare string "Ok" (JsonValue) — do not index it as an object.
        if (response is JsonObject obj && obj["Error"] is JsonNode err)
            throw new InvalidOperationException(err.ToJsonString());
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
