using System.Collections.Concurrent;
using GoXlrControl.Config;
using GoXlrControl.Hardware.Abstractions;
using Microsoft.Extensions.Logging;

namespace GoXlrControl.Engine;

public sealed class ControllerEngine : IAsyncDisposable
{
    private readonly IHardwareInputProvider _hardware;
    private readonly IVolumeSink _volume;
    private readonly ActionDispatcher _actions;
    private readonly ILogger<ControllerEngine>? _logger;
    private readonly ConcurrentDictionary<FaderId, SoftTakeoverTracker> _takeovers = new();
    private readonly ConcurrentDictionary<FaderId, double> _lastHardware = new();
    private readonly ConcurrentDictionary<FaderId, double> _lastTarget = new();
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

    public void SetProfile(ControllerProfile profile)
    {
        lock (_gate)
        {
            _profile = profile;
            foreach (var tracker in _takeovers.Values)
                tracker.Reset();
            _takeovers.Clear();
        }

        ProfileChanged?.Invoke(this, EventArgs.Empty);
        DiagnosticMessage?.Invoke(this, $"Profil aktiv: {profile.Name}");
    }

    public void Start()
    {
        if (_started) return;
        _hardware.FaderChanged += OnFaderChanged;
        _hardware.ButtonChanged += OnButtonChanged;
        _started = true;
    }

    public void Stop()
    {
        if (!_started) return;
        _hardware.FaderChanged -= OnFaderChanged;
        _hardware.ButtonChanged -= OnButtonChanged;
        _started = false;
    }

    private async void OnFaderChanged(object? sender, FaderValueChanged e)
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

            var mapped = FaderValueMapper.ApplyBinding(e.NormalizedValue, binding);
            var currentTarget = await ResolveCurrentTargetAsync(binding).ConfigureAwait(false) ?? mapped;
            var tracker = _takeovers.GetOrAdd(e.Fader, _ => new SoftTakeoverTracker());

            if (!tracker.ShouldApply(mapped, currentTarget, binding.SyncMode, binding.DeadZone))
            {
                _lastHardware[e.Fader] = e.NormalizedValue;
                _lastTarget[e.Fader] = currentTarget;
                return;
            }

            await ApplyVolumeAsync(binding, mapped).ConfigureAwait(false);
            _lastHardware[e.Fader] = e.NormalizedValue;
            _lastTarget[e.Fader] = mapped;
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
}
