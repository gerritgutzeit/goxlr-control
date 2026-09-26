using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoXlrControl.Config;

public enum SyncMode
{
    SoftTakeover,
    Absolute
}

public enum LightingMode
{
    Off,
    Status,
    PeakProxy
}

public enum FaderTargetKind
{
    None,
    MasterVolume,
    EndpointVolume,
    Application,
    ApplicationGroup,
    DiscordPlayback
}

public enum AppIdentityKind
{
    ExePath,
    SessionIdPrefix,
    Aumid
}

public enum ActionType
{
    None,
    ToggleMasterMute,
    ToggleEndpointMute,
    ToggleApplicationMute,
    SendShortcut,
    DiscordMute,
    DiscordDeafen,
    MediaPlayPause,
    MediaNext,
    MediaPrevious,
    LaunchApplication,
    SwitchProfile
}

/// <summary>Persisted Discord integration mode. Hybrid/Native require Discord approval gates.</summary>
public enum DiscordIntegrationMode
{
    Fallback,
    Hybrid,
    Native
}

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 3;
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    public bool CloseToTray { get; set; } = true;
    public string? SelectedDeviceSerial { get; set; }
    public bool FollowDefaultPlayback { get; set; } = true;
    public string? SelectedPlaybackDeviceId { get; set; }
    public SyncMode SyncModeDefault { get; set; } = SyncMode.SoftTakeover;
    public string LogLevel { get; set; } = "Information";
    public bool WizardCompleted { get; set; }
    /// <summary>Must match a Discord keybind (User Settings → Keybinds). FALLBACK sends this via SendInput.</summary>
    public string? DiscordMuteChord { get; set; } = "Ctrl+Shift+M";
    public string? DiscordDeafenChord { get; set; } = "Ctrl+Shift+D";
    /// <summary>Requested mode. Factory forces Fallback until authorized APIs are enabled.</summary>
    public DiscordIntegrationMode DiscordIntegrationMode { get; set; } = DiscordIntegrationMode.Fallback;
    /// <summary>Discord application client id — unused until approval path is live.</summary>
    public string? DiscordClientId { get; set; }
    public bool UseSimulatedHardware { get; set; }
    /// <summary>Start goxlr-daemon automatically when Control Studio connects.</summary>
    public bool AutoStartUtilityDaemon { get; set; } = true;
    public string? ActiveProfileId { get; set; }
    public bool ControllerPaused { get; set; }
    public LightingMode LightingMode { get; set; } = LightingMode.Status;
    /// <summary>
    /// Disable GoXLR animations and pin button on/off colours so firmware mute
    /// state cannot flash Utility profile colours over our LED feedback.
    /// </summary>
    public bool ExclusiveLightingControl { get; set; } = true;
}

public sealed class AppIdentity
{
    public AppIdentityKind Kind { get; set; } = AppIdentityKind.ExePath;
    public string Value { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? IconCacheKey { get; set; }
}

public sealed class FaderTarget
{
    public FaderTargetKind Kind { get; set; } = FaderTargetKind.None;
    public string? DeviceId { get; set; }
    public AppIdentity? Application { get; set; }
    public List<AppIdentity> Applications { get; set; } = new();
    public bool AggregateSessions { get; set; } = true;
}

public sealed class FaderBinding
{
    public string FaderId { get; set; } = "A";
    public string Label { get; set; } = string.Empty;
    public FaderTarget Target { get; set; } = new();
    public double Min { get; set; }
    public double Max { get; set; } = 1.0;
    public bool Invert { get; set; }
    public string Curve { get; set; } = "Linear";
    public SyncMode SyncMode { get; set; } = SyncMode.SoftTakeover;
    public double DeadZone { get; set; } = 0.02;
    /// <summary>GoXLR LED accent as RRGGBB (no #). Empty → <see cref="FaderAccentColours.DefaultFor"/>.</summary>
    public string? AccentColour { get; set; }
}

/// <summary>Default / preset LED colours for fader identity (RRGGBB).</summary>
public static class FaderAccentColours
{
    public static readonly string[] Presets =
    [
        "2EC4B6", // teal
        "5B9FD4", // sky
        "81B29A", // sage
        "C9A227", // gold
        "E07A5F", // coral
        "E85D4C", // red
        "F4A261", // apricot
        "6C8EAD", // steel
        "A8DADC", // mist
        "E9C46A", // sand
        "264653", // deep teal
        "E8ECF1"  // light
    ];

    public static string DefaultFor(string faderId) => faderId.Trim().ToUpperInvariant() switch
    {
        "A" => "2EC4B6",
        "B" => "5B9FD4",
        "C" => "E07A5F",
        "D" => "C9A227",
        _ => "2EC4B6"
    };

    public static string Resolve(FaderBinding binding) =>
        Normalize(binding.AccentColour) ?? DefaultFor(binding.FaderId);

    public static string? Normalize(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        hex = hex.Trim().TrimStart('#');
        if (hex.Length != 6) return null;
        foreach (var c in hex)
        {
            var isHex = c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F');
            if (!isHex) return null;
        }

        return hex.ToUpperInvariant();
    }
}

public sealed class ActionRef
{
    public ActionType Type { get; set; } = ActionType.None;
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ButtonBinding
{
    public string ButtonId { get; set; } = string.Empty;
    public ActionRef OnPress { get; set; } = new();
    public ActionRef? OnLongPress { get; set; }
    public ActionRef? OnRelease { get; set; }
}

public sealed class ControllerProfile
{
    public int SchemaVersion { get; set; } = 3;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Default";
    public string Description { get; set; } = string.Empty;
    public List<FaderBinding> Faders { get; set; } = CreateDefaultFaders();
    public List<ButtonBinding> Buttons { get; set; } = CreateDefaultButtons();
    public bool FollowDefaultPlayback { get; set; } = true;
    public string? PlaybackDeviceId { get; set; }

    public static List<FaderBinding> CreateDefaultFaders() =>
    [
        // Absolute: SoftTakeover felt "broken" for Master until the physical fader crossed Windows volume.
        new() { FaderId = "A", Label = "Master", Target = new() { Kind = FaderTargetKind.MasterVolume }, SyncMode = SyncMode.Absolute, AccentColour = FaderAccentColours.DefaultFor("A") },
        new() { FaderId = "B", Label = "App 1", Target = new() { Kind = FaderTargetKind.None }, AccentColour = FaderAccentColours.DefaultFor("B") },
        new() { FaderId = "C", Label = "App 2", Target = new() { Kind = FaderTargetKind.None }, AccentColour = FaderAccentColours.DefaultFor("C") },
        new() { FaderId = "D", Label = "Music", Target = new() { Kind = FaderTargetKind.None }, AccentColour = FaderAccentColours.DefaultFor("D") }
    ];

    public static List<ButtonBinding> CreateDefaultButtons() =>
    [
        new() { ButtonId = "Fader1Mute", OnPress = new() { Type = ActionType.ToggleMasterMute } },
        new() { ButtonId = "Fader2Mute", OnPress = new() { Type = ActionType.None } },
        new() { ButtonId = "Fader3Mute", OnPress = new() { Type = ActionType.None } },
        new() { ButtonId = "Fader4Mute", OnPress = new() { Type = ActionType.None } },
        new() { ButtonId = "Bleep", OnPress = new() { Type = ActionType.MediaPlayPause } },
        new() { ButtonId = "Cough", OnPress = new() { Type = ActionType.DiscordMute } }
    ];

    public static ControllerProfile CreateDefault(string name = "Desktop") => new()
    {
        Name = name,
        Description = "Standard Desktop-Profil"
    };
}

public static class JsonConfig
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
