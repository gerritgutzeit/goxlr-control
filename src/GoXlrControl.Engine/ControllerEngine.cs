using System.Collections.Concurrent;
using GoXlrControl.Abstractions;
using GoXlrControl.Config;
using GoXlrControl.Hardware.Abstractions;
using Microsoft.Extensions.Logging;

namespace GoXlrControl.Engine;

public sealed class ControllerEngine : IAsyncDisposable
{
    private static readonly TimeSpan FaderCoalesceWindow = TimeSpan.FromMilliseconds(12);

    private readonly IHardwareInputProvider _hardware;
    private readonly IVolumeSink _volume;
    private readonly ActionDispatcher _actions;
    private readonly ILogger<ControllerEngine>? _logger;
    private readonly ConcurrentDictionary<FaderId, SoftTakeoverTracker> _takeovers = new();
    private readonly ConcurrentDictionary<FaderId, double> _lastHardware = new();
    private readonly ConcurrentDictionary<FaderId, double> _lastTarget = new();
    private readonly ConcurrentDictionary<FaderId, bool> _softTakeoverWarned = new();
    private readonly ConcurrentDictionary<FaderId, FaderCoalesceSlot> _faderCoalesce = new();
    private readonly ButtonDebouncer _debouncer = new();
    private readonly ConcurrentDictionary<HardwareButtonId, DateTimeOffset> _pressStarted = new();
    private readonly object _gate = new();
    private ControllerProfile _profile = ControllerProfile.CreateDefault();
    private bool _paused;
    private bool _started;

    public ControllerEngine(
        IHardwareInputProvider hardware,
        IVolumeSink volume,
        ActionDispatcher actions,
        ILogger<ControllerEngine>? logger = null)
    {
        _hardware = hardware;
        _volume = volume;
        _actions = actions;
        _logger = logger;
    }

    public event EventHandler<FaderId>? FaderOutputChanged;
    public event EventHandler<FaderId>? SoftTakeoverPendingChanged;
    public event EventHandler<string>? DiagnosticMessage;
    public event EventHandler? ProfileChanged;

    public ControllerProfile ActiveProfile
    {
        get { lock (_gate) return _profile; }
    }

    public bool IsPaused
    {
        get => _paused;
        set => _paused = value;
    }

    public IReadOnlyDictionary<FaderId, double> LastHardwareValues => _lastHardware;
    public IReadOnlyDictionary<FaderId, double> LastTargetValues => _lastTarget;

    public bool IsSoftTakeoverEngaged(FaderId fader) =>
        _takeovers.TryGetValue(fader, out var t) && t.Engaged;

    public bool HasSoftTakeoverPending(FaderId fader)
    {
        if (!_takeovers.TryGetValue(fader, out var t))
            return false;
        return !t.Engaged;
    }

    public void SetProfile(ControllerProfile profile)
    {
        lock (_gate)
        {
            _profile = profile;
            ResetSoftTakeoverLocked();
        }

        ProfileChanged?.Invoke(this, EventArgs.Empty);
        DiagnosticMessage?.Invoke(this, $"Profil aktiv: {profile.Name}");
    }

    /// <summary>
    /// Resets soft-takeover trackers after hardware resync without forcing volume writes.
    /// </summary>
    public void ResetSoftTakeover()
    {
        lock (_gate)
            ResetSoftTakeoverLocked();

        foreach (var fader in Enum.GetValues<FaderId>())
            SoftTakeoverPendingChanged?.Invoke(this, fader);
    }

    private void ResetSoftTakeoverLocked()
    {
        foreach (var tracker in _takeovers.Values)
            tracker.Reset();
        _takeovers.Clear();
        _softTakeoverWarned.Clear();
        foreach (var slot in _faderCoalesce.Values)
            slot.Cancel();
        _faderCoalesce.Clear();
    }

    public void Start()
    {
        if (_started) return;
        _hardware.FaderChanged += OnFaderChanged;
        _hardware.ButtonChanged += OnButtonChanged;
        _hardware.ConnectionChanged += OnConnectionChanged;
        _started = true;
    }

    public void Stop()
    {
        if (!_started) return;
        _hardware.FaderChanged -= OnFaderChanged;
        _hardware.ButtonChanged -= OnButtonChanged;
        _hardware.ConnectionChanged -= OnConnectionChanged;
        foreach (var slot in _faderCoalesce.Values)
            slot.Cancel();
        _faderCoalesce.Clear();
        _started = false;
    }

    private void OnConnectionChanged(object? sender, HardwareConnectionState state)
    {
        if (state is HardwareConnectionState.Connected or HardwareConnectionState.HttpDisabled)
        {
            ResetSoftTakeover();
            DiagnosticMessage?.Invoke(this, $"Hardware {state} — Soft-Takeover zurückgesetzt.");
        }
    }

    private void OnFaderChanged(object? sender, FaderValueChanged e)
    {
        try
        {
            if (_paused || e.IsInitial)
            {
                _lastHardware[e.Fader] = e.NormalizedValue;
                return;
            }

            FaderBinding? binding;
            lock (_gate)
                binding = _profile.Faders.FirstOrDefault(f => f.FaderId.Equals(e.Fader.ToString(), StringComparison.OrdinalIgnoreCase));

            if (binding is null || binding.Target.Kind == FaderTargetKind.None)
            {
                _lastHardware[e.Fader] = e.NormalizedValue;
                return;
            }

            var slot = _faderCoalesce.GetOrAdd(e.Fader, _ => new FaderCoalesceSlot());
            slot.Schedule(e, () => ProcessFaderAsync(e.Fader), FaderCoalesceWindow);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Fader-Verarbeitung fehlgeschlagen");
            DiagnosticMessage?.Invoke(this, $"Fader-Fehler: {ex.Message}");
        }
    }

    private async Task ProcessFaderAsync(FaderId faderId)
    {
        try
        {
            if (!_faderCoalesce.TryGetValue(faderId, out var slot))
                return;

            var e = slot.TakeLatest();
            if (e is null || _paused)
                return;

            FaderBinding? binding;
            lock (_gate)
                binding = _profile.Faders.FirstOrDefault(f => f.FaderId.Equals(e.Fader.ToString(), StringComparison.OrdinalIgnoreCase));

            if (binding is null || binding.Target.Kind == FaderTargetKind.None)
            {
                _lastHardware[e.Fader] = e.NormalizedValue;
                return;
            }

            var mapped = FaderValueMapper.ApplyBinding(e.NormalizedValue, binding);
            var currentTarget = await ResolveCurrentTargetAsync(binding).ConfigureAwait(false) ?? mapped;
            var tracker = _takeovers.GetOrAdd(e.Fader, _ => new SoftTakeoverTracker());

            if (!tracker.ShouldApply(mapped, currentTarget, binding.SyncMode, binding.DeadZone))
            {
                _lastHardware[e.Fader] = e.NormalizedValue;
                _lastTarget[e.Fader] = currentTarget;
                if (!_softTakeoverWarned.ContainsKey(e.Fader))
                {
                    _softTakeoverWarned[e.Fader] = true;
                    DiagnosticMessage?.Invoke(this,
                        $"Soft-Takeover Fader {e.Fader}: Fader an Windows-Lautstärke ({currentTarget:P0}) vorbeiziehen, oder Sync-Modus → Absolute.");
                }

                SoftTakeoverPendingChanged?.Invoke(this, e.Fader);
                return;
            }

            _softTakeoverWarned.TryRemove(e.Fader, out _);
            await ApplyVolumeAsync(binding, mapped).ConfigureAwait(false);
            _lastHardware[e.Fader] = e.NormalizedValue;
            _lastTarget[e.Fader] = mapped;
            SoftTakeoverPendingChanged?.Invoke(this, e.Fader);
            FaderOutputChanged?.Invoke(this, e.Fader);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Fader-Verarbeitung fehlgeschlagen");
            DiagnosticMessage?.Invoke(this, $"Fader-Fehler: {ex.Message}");
        }
    }

    private async void OnButtonChanged(object? sender, ButtonStateChanged e)
    {
        try
        {
            if (_paused || e.IsInitial)
                return;

            if (!_debouncer.TryUpdate(e.Button, e.IsPressed, e.Timestamp, out _))
                return;

            ButtonBinding? binding;
            lock (_gate)
                binding = _profile.Buttons.FirstOrDefault(b =>
                    b.ButtonId.Equals(e.Button.ToString(), StringComparison.OrdinalIgnoreCase));

            if (binding is null) return;

            if (e.IsPressed)
            {
                _pressStarted[e.Button] = e.Timestamp;
                await _actions.DispatchAsync(binding.OnPress).ConfigureAwait(false);
            }
            else
            {
                if (_pressStarted.TryRemove(e.Button, out var started) &&
                    e.Timestamp - started >= TimeSpan.FromMilliseconds(500) &&
                    binding.OnLongPress is not null)
                {
                    await _actions.DispatchAsync(binding.OnLongPress).ConfigureAwait(false);
                }

                if (binding.OnRelease is not null)
                    await _actions.DispatchAsync(binding.OnRelease).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Button-Verarbeitung fehlgeschlagen");
            DiagnosticMessage?.Invoke(this, $"Button-Fehler: {ex.Message}");
        }
    }

    private async Task<double?> ResolveCurrentTargetAsync(FaderBinding binding)
    {
        return binding.Target.Kind switch
        {
            FaderTargetKind.MasterVolume => await _volume.GetMasterVolumeAsync().ConfigureAwait(false),
            FaderTargetKind.DiscordPlayback when binding.Target.Application is not null =>
                await _volume.GetApplicationVolumeAsync(binding.Target.Application).ConfigureAwait(false),
            FaderTargetKind.Application when binding.Target.Application is not null =>
                await _volume.GetApplicationVolumeAsync(binding.Target.Application).ConfigureAwait(false),
            _ => null
        };
    }

    private Task ApplyVolumeAsync(FaderBinding binding, double value) =>
        binding.Target.Kind switch
        {
            FaderTargetKind.MasterVolume => _volume.SetMasterVolumeAsync(value),
            FaderTargetKind.EndpointVolume when binding.Target.DeviceId is not null =>
                _volume.SetEndpointVolumeAsync(binding.Target.DeviceId, value),
            FaderTargetKind.Application when binding.Target.Application is not null =>
                _volume.SetApplicationVolumeAsync(binding.Target.Application, value, binding.Target.AggregateSessions),
            FaderTargetKind.DiscordPlayback when binding.Target.Application is not null =>
                _volume.SetApplicationVolumeAsync(binding.Target.Application, value, binding.Target.AggregateSessions),
            FaderTargetKind.ApplicationGroup => ApplyGroupAsync(binding, value),
            _ => Task.CompletedTask
        };

    private async Task ApplyGroupAsync(FaderBinding binding, double value)
    {
        foreach (var identity in binding.Target.Applications)
            await _volume.SetApplicationVolumeAsync(identity, value, true).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        Stop();
        return ValueTask.CompletedTask;
    }

    /// <summary>Latest-wins coalesce slot for one fader.</summary>
    private sealed class FaderCoalesceSlot
    {
        private readonly object _gate = new();
        private FaderValueChanged? _pending;
        private CancellationTokenSource? _cts;

        public void Schedule(FaderValueChanged value, Func<Task> process, TimeSpan window)
        {
            CancellationTokenSource cts;
            lock (_gate)
            {
                _pending = value;
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();
                cts = _cts;
            }

            _ = RunAsync(cts, process, window);
        }

        public FaderValueChanged? TakeLatest()
        {
            lock (_gate)
            {
                var value = _pending;
                _pending = null;
                return value;
            }
        }

        public void Cancel()
        {
            lock (_gate)
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;
                _pending = null;
            }
        }

        private async Task RunAsync(CancellationTokenSource cts, Func<Task> process, TimeSpan window)
        {
            try
            {
                await Task.Delay(window, cts.Token).ConfigureAwait(false);
                await process().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // superseded by a newer fader sample
            }
        }
    }
}
