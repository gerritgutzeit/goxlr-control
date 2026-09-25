using GoXlrControl.Engine;
using Microsoft.Extensions.Logging;

namespace GoXlrControl.Integrations;

/// <summary>
/// Runtime gate for Hybrid/Native Discord backends.
/// Remains false until Discord partner/Social-SDK approval is validated —
/// see docs/planning/discord-approval-checklist.md.
/// </summary>
public static class DiscordAuthorizationGate
{
    /// <summary>
    /// When false (default), the factory always returns FALLBACK regardless of settings.
    /// Flip only after documented Discord approval for the chosen mode.
    /// </summary>
    public static bool AuthorizedApisEnabled { get; set; }
}

/// <summary>
/// Scaffold for HYBRID (authorized read + shortcut write).
/// Not activated until <see cref="DiscordAuthorizationGate.AuthorizedApisEnabled"/> and a real Social SDK backend exist.
/// </summary>
public sealed class HybridDiscordIntegration : IDiscordIntegration
{
    public HybridDiscordIntegration() =>
        throw new NotSupportedException(
            "HYBRID Discord-Integration erfordert genehmigten Social-SDK-Desktop-Voice-Read. " +
            "Siehe docs/planning/discord-approval-checklist.md. Nutze FALLBACK bis zur Freigabe.");

    public DiscordOperatingMode Mode => DiscordOperatingMode.Hybrid;
    public DiscordConnectionState ConnectionState => DiscordConnectionState.Error;
    public DiscordVoiceSnapshot Current => throw new NotSupportedException();
    public event EventHandler<DiscordVoiceSnapshot>? StateChanged
    {
        add { }
        remove { }
    }

    public Task StartAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task StopAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task MuteAsync(bool muted, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DeafenAsync(bool deafened, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task ToggleMuteAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task ToggleDeafenAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public void SyncMirroredMute(bool muted) => throw new NotSupportedException();
    public void SyncMirroredDeafen(bool deafened) => throw new NotSupportedException();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Scaffold for NATIVE (authorized read + write via Local RPC).
/// Must not request rpc.voice.* scopes until partner approval is confirmed.
/// </summary>
public sealed class NativeDiscordIntegration : IDiscordIntegration
{
    public NativeDiscordIntegration() =>
        throw new NotSupportedException(
            "NATIVE Discord-Integration erfordert Partner-Approval für rpc.voice.read/write. " +
            "Restricted Scopes dürfen nicht ohne Freigabe angefragt werden. " +
            "Siehe docs/planning/discord-approval-checklist.md.");

    public DiscordOperatingMode Mode => DiscordOperatingMode.Native;
    public DiscordConnectionState ConnectionState => DiscordConnectionState.Error;
    public DiscordVoiceSnapshot Current => throw new NotSupportedException();
    public event EventHandler<DiscordVoiceSnapshot>? StateChanged
    {
        add { }
        remove { }
    }

    public Task StartAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task StopAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task MuteAsync(bool muted, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DeafenAsync(bool deafened, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task ToggleMuteAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task ToggleDeafenAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public void SyncMirroredMute(bool muted) => throw new NotSupportedException();
    public void SyncMirroredDeafen(bool deafened) => throw new NotSupportedException();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
