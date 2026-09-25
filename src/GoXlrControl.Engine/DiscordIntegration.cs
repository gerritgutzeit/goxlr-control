namespace GoXlrControl.Engine;

/// <summary>Operating mode for Discord mute/deafen integration.</summary>
public enum DiscordOperatingMode
{
    /// <summary>Keyboard shortcuts only; mute/deafen state is never confirmed.</summary>
    Fallback,
    /// <summary>Authorized API read + shortcut write. Requires Discord approval.</summary>
    Hybrid,
    /// <summary>Authorized API read and write. Requires Discord partner approval.</summary>
    Native
}

public enum DiscordConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Error
}

/// <summary>Tri-state for mute/deafen. Use Unknown when Discord has not confirmed the value.</summary>
public enum DiscordTriState
{
    Unknown,
    On,
    Off
}

public enum DiscordStatusReliability
{
    /// <summary>No verified Discord state.</summary>
    Unknown,
    /// <summary>
    /// Local mirror after our shortcuts (or user sync). Not Discord-confirmed —
    /// may desync if mute changed in the Discord UI.
    /// </summary>
    Mirrored,
    /// <summary>A mute/deafen command was sent; values may still be Unknown.</summary>
    CommandSent,
    /// <summary>Mute/deafen values were read from an authorized Discord API.</summary>
    Confirmed
}

public sealed record DiscordVoiceSnapshot(
    DiscordTriState Mute,
    DiscordTriState Deafen,
    DiscordStatusReliability Reliability,
    DateTimeOffset? StatusConfirmedAt,
    DiscordConnectionState Connection,
    DiscordOperatingMode Mode);

public interface IDiscordIntegration : IAsyncDisposable
{
    DiscordOperatingMode Mode { get; }
    DiscordConnectionState ConnectionState { get; }
    DiscordVoiceSnapshot Current { get; }
    event EventHandler<DiscordVoiceSnapshot>? StateChanged;

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);

    Task MuteAsync(bool muted, CancellationToken cancellationToken = default);
    Task DeafenAsync(bool deafened, CancellationToken cancellationToken = default);
    Task ToggleMuteAsync(CancellationToken cancellationToken = default);
    Task ToggleDeafenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Align the local mirror with what the user sees in Discord (no keystroke).
    /// Used when Discord was muted/deafened outside this app.
    /// </summary>
    void SyncMirroredMute(bool muted);

    /// <inheritdoc cref="SyncMirroredMute"/>
    void SyncMirroredDeafen(bool deafened);
}
