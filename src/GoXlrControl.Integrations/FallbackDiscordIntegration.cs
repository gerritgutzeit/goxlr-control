using GoXlrControl.Engine;
using Microsoft.Extensions.Logging;

namespace GoXlrControl.Integrations;

/// <summary>
/// Shortcut-only Discord control with a local mute/deafen mirror for GoXLR LEDs.
/// Mirror is never Confirmed — it tracks our toggles (and optional user sync).
/// </summary>
public sealed class FallbackDiscordIntegration : IDiscordIntegration
{
    private readonly Action<string> _sendChord;
    private readonly Func<string?> _muteChordProvider;
    private readonly Func<string?> _deafenChordProvider;
    private readonly ILogger<FallbackDiscordIntegration>? _logger;
    private readonly object _gate = new();
    private DiscordTriState _mute = DiscordTriState.Unknown;
    private DiscordTriState _deafen = DiscordTriState.Unknown;
    private DiscordStatusReliability _reliability = DiscordStatusReliability.Unknown;
    private DateTimeOffset? _statusAt;
    private bool _started;

    public FallbackDiscordIntegration(
        Action<string> sendChord,
        Func<string?> muteChordProvider,
        Func<string?> deafenChordProvider,
        ILogger<FallbackDiscordIntegration>? logger = null)
    {
        _sendChord = sendChord;
        _muteChordProvider = muteChordProvider;
        _deafenChordProvider = deafenChordProvider;
        _logger = logger;
    }

    public DiscordOperatingMode Mode => DiscordOperatingMode.Fallback;
    public DiscordConnectionState ConnectionState => Current.Connection;
    public DiscordVoiceSnapshot Current
    {
        get { lock (_gate) return CreateSnapshotLocked(); }
    }

    public event EventHandler<DiscordVoiceSnapshot>? StateChanged;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _started = true;
        lock (_gate)
        {
            // Keep mirror across soft restarts only if already set; fresh start stays Unknown.
            _reliability = _mute == DiscordTriState.Unknown && _deafen == DiscordTriState.Unknown
                ? DiscordStatusReliability.Unknown
                : DiscordStatusReliability.Mirrored;
        }

        Publish();
        _logger?.LogInformation(
            "Discord FALLBACK gestartet (Mirror für LEDs; kein Discord-Read ohne Approval).");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _started = false;
        Publish();
        return Task.CompletedTask;
    }

    public Task MuteAsync(bool muted, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureStarted();
        lock (_gate)
        {
            if (_mute == (muted ? DiscordTriState.On : DiscordTriState.Off))
                return Task.CompletedTask;
        }

        SendMuteChord();
        lock (_gate)
        {
            _mute = muted ? DiscordTriState.On : DiscordTriState.Off;
            // Deafen implies mute in Discord UX; keep mute On when deafened.
            if (_deafen == DiscordTriState.On)
                _mute = DiscordTriState.On;
            MarkMirroredLocked();
        }

        Publish();
        return Task.CompletedTask;
    }

    public Task DeafenAsync(bool deafened, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureStarted();
        lock (_gate)
        {
            if (_deafen == (deafened ? DiscordTriState.On : DiscordTriState.Off))
                return Task.CompletedTask;
        }

        SendDeafenChord();
        lock (_gate)
        {
            _deafen = deafened ? DiscordTriState.On : DiscordTriState.Off;
            if (deafened)
                _mute = DiscordTriState.On;
            MarkMirroredLocked();
        }

        Publish();
        return Task.CompletedTask;
    }

    public Task ToggleMuteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureStarted();
        SendMuteChord();
        lock (_gate)
        {
            _mute = _mute switch
            {
                DiscordTriState.On => DiscordTriState.Off,
                DiscordTriState.Off => DiscordTriState.On,
                // Unknown: assume Discord was unmuted and this press muted.
                _ => DiscordTriState.On
            };
            if (_deafen == DiscordTriState.On && _mute == DiscordTriState.Off)
            {
                // Unmuting while deafened is inconsistent in Discord; clear deafen mirror.
                _deafen = DiscordTriState.Off;
            }

            MarkMirroredLocked();
        }

        _logger?.LogInformation("Discord Mute Shortcut gesendet (Mirror={Mute}).", _mute);
        Publish();
        return Task.CompletedTask;
    }

    public Task ToggleDeafenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureStarted();
        SendDeafenChord();
        lock (_gate)
        {
            _deafen = _deafen switch
            {
                DiscordTriState.On => DiscordTriState.Off,
                DiscordTriState.Off => DiscordTriState.On,
                _ => DiscordTriState.On
            };
            if (_deafen == DiscordTriState.On)
                _mute = DiscordTriState.On;
            MarkMirroredLocked();
        }

        _logger?.LogInformation("Discord Deafen Shortcut gesendet (Mirror={Deafen}).", _deafen);
        Publish();
        return Task.CompletedTask;
    }

    public void SyncMirroredMute(bool muted)
    {
        lock (_gate)
        {
            _mute = muted ? DiscordTriState.On : DiscordTriState.Off;
            if (!muted && _deafen == DiscordTriState.On)
                _deafen = DiscordTriState.Off;
            MarkMirroredLocked();
        }

        _logger?.LogInformation("Discord Mute-Mirror manuell auf {Mute} gesetzt.", muted ? "On" : "Off");
        Publish();
    }

    public void SyncMirroredDeafen(bool deafened)
    {
        lock (_gate)
        {
            _deafen = deafened ? DiscordTriState.On : DiscordTriState.Off;
            if (deafened)
                _mute = DiscordTriState.On;
            MarkMirroredLocked();
        }

        _logger?.LogInformation("Discord Deafen-Mirror manuell auf {Deafen} gesetzt.", deafened ? "On" : "Off");
        Publish();
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private void SendMuteChord()
    {
        var chord = _muteChordProvider();
        if (string.IsNullOrWhiteSpace(chord))
            throw new InvalidOperationException("Discord Mute Shortcut ist nicht konfiguriert.");
        _sendChord(chord);
    }

    private void SendDeafenChord()
    {
        var chord = _deafenChordProvider();
        if (string.IsNullOrWhiteSpace(chord))
            throw new InvalidOperationException("Discord Deafen Shortcut ist nicht konfiguriert.");
        _sendChord(chord);
    }

    private void EnsureStarted()
    {
        if (!_started)
            throw new InvalidOperationException("Discord-Integration wurde nicht gestartet.");
    }

    private void MarkMirroredLocked()
    {
        _reliability = DiscordStatusReliability.Mirrored;
        _statusAt = DateTimeOffset.Now;
    }

    private DiscordVoiceSnapshot CreateSnapshotLocked() =>
        new(
            Mute: _mute,
            Deafen: _deafen,
            Reliability: _reliability,
            StatusConfirmedAt: _statusAt,
            Connection: _started ? DiscordConnectionState.Disconnected : DiscordConnectionState.Disconnected,
            Mode: DiscordOperatingMode.Fallback);

    private void Publish()
    {
        DiscordVoiceSnapshot snapshot;
        lock (_gate) snapshot = CreateSnapshotLocked();
        StateChanged?.Invoke(this, snapshot);
    }
}
