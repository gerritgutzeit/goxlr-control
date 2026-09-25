using GoXlrControl.Config;
using GoXlrControl.Engine;
using Microsoft.Extensions.Logging;

namespace GoXlrControl.Integrations;

public static class DiscordIntegrationFactory
{
    /// <summary>
    /// Creates the Discord integration. Always returns FALLBACK unless authorized APIs are
    /// enabled and a real Hybrid/Native backend is implemented. Never opens restricted RPC scopes.
    /// </summary>
    public static IDiscordIntegration Create(
        Func<AppSettings> settingsProvider,
        SendInputShortcutService shortcuts,
        ILoggerFactory? loggerFactory = null)
    {
        var requested = settingsProvider().DiscordIntegrationMode;
        var effective = ResolveEffectiveMode(requested, out var reason);
        if (reason is not null)
        {
            loggerFactory?.CreateLogger("DiscordIntegrationFactory")
                .LogWarning("Discord-Modus {Requested} nicht aktiv: {Reason}. Nutze FALLBACK.", requested, reason);
        }

        // Hybrid/Native backends are scaffolds only — ResolveEffectiveMode keeps us on Fallback
        // until a real implementation is wired after Discord approval.
        _ = effective;
        return new FallbackDiscordIntegration(
            shortcuts.SendChord,
            () => settingsProvider().DiscordMuteChord,
            () => settingsProvider().DiscordDeafenChord,
            loggerFactory?.CreateLogger<FallbackDiscordIntegration>());
    }

    /// <summary>
    /// Resolves the mode that will actually run. Hybrid/Native require the authorization gate
    /// and a implemented backend; otherwise FALLBACK.
    /// </summary>
    public static DiscordIntegrationMode ResolveEffectiveMode(
        DiscordIntegrationMode requested,
        out string? downgradeReason)
    {
        if (requested == DiscordIntegrationMode.Fallback)
        {
            downgradeReason = null;
            return DiscordIntegrationMode.Fallback;
        }

        if (!DiscordAuthorizationGate.AuthorizedApisEnabled)
        {
            downgradeReason =
                "AuthorizedApisEnabled=false (kein Discord-Partner-/Social-SDK-Approval validiert).";
            return DiscordIntegrationMode.Fallback;
        }

        downgradeReason =
            $"{requested}-Backend ist noch nicht implementiert (wartet auf genehmigte API).";
        return DiscordIntegrationMode.Fallback;
    }
}
