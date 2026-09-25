using GoXlrControl.Config;
using GoXlrControl.Engine;
using Microsoft.Extensions.Logging;

namespace GoXlrControl.Integrations;

public sealed class MuteActionExecutor : IActionExecutor
{
    private readonly IVolumeSink _volume;
    public MuteActionExecutor(IVolumeSink volume) => _volume = volume;
    public ActionType ActionType => ActionType.ToggleMasterMute;
    public Task ExecuteAsync(ActionRef action, CancellationToken cancellationToken = default) =>
        _volume.ToggleMasterMuteAsync(cancellationToken);
}

public sealed class EndpointMuteActionExecutor : IActionExecutor
{
    private readonly IVolumeSink _volume;
    public EndpointMuteActionExecutor(IVolumeSink volume) => _volume = volume;
    public ActionType ActionType => ActionType.ToggleEndpointMute;
    public Task ExecuteAsync(ActionRef action, CancellationToken cancellationToken = default)
    {
        var id = action.Parameters.GetValueOrDefault("deviceId")
                 ?? throw new InvalidOperationException("deviceId fehlt.");
        return _volume.ToggleEndpointMuteAsync(id, cancellationToken);
    }
}

public sealed class ApplicationMuteActionExecutor : IActionExecutor
{
    private readonly IVolumeSink _volume;
    public ApplicationMuteActionExecutor(IVolumeSink volume) => _volume = volume;
    public ActionType ActionType => ActionType.ToggleApplicationMute;
    public Task ExecuteAsync(ActionRef action, CancellationToken cancellationToken = default)
    {
        var value = action.Parameters.GetValueOrDefault("identity")
                    ?? throw new InvalidOperationException("identity fehlt.");
        var kind = Enum.TryParse<AppIdentityKind>(action.Parameters.GetValueOrDefault("kind"), true, out var k)
            ? k : AppIdentityKind.ExePath;
        return _volume.ToggleApplicationMuteAsync(new AppIdentity { Kind = kind, Value = value }, cancellationToken);
    }
}

public sealed class ShortcutActionExecutor : IActionExecutor
{
    private readonly SendInputShortcutService _shortcuts;
    public ShortcutActionExecutor(SendInputShortcutService shortcuts) => _shortcuts = shortcuts;
    public ActionType ActionType => ActionType.SendShortcut;
    public Task ExecuteAsync(ActionRef action, CancellationToken cancellationToken = default)
    {
        var chord = action.Parameters.GetValueOrDefault("chord")
                    ?? throw new InvalidOperationException("chord fehlt.");
        _shortcuts.SendChord(chord);
        return Task.CompletedTask;
    }
}

public sealed class DiscordMuteActionExecutor : IActionExecutor
{
    private readonly SendInputShortcutService _shortcuts;
    private readonly Func<string?> _chordProvider;
    private readonly ILogger<DiscordMuteActionExecutor>? _logger;
    public ActionType ActionType => ActionType.DiscordMute;
    public event EventHandler? CommandSent;

    public DiscordMuteActionExecutor(
        SendInputShortcutService shortcuts,
        Func<string?> chordProvider,
        ILogger<DiscordMuteActionExecutor>? logger = null)
    {
        _shortcuts = shortcuts;
        _chordProvider = chordProvider;
        _logger = logger;
    }

    public Task ExecuteAsync(ActionRef action, CancellationToken cancellationToken = default)
    {
        var chord = action.Parameters.GetValueOrDefault("chord") ?? _chordProvider();
        if (string.IsNullOrWhiteSpace(chord))
            throw new InvalidOperationException("Discord Mute Shortcut ist nicht konfiguriert.");
        _shortcuts.SendChord(chord);
        _logger?.LogInformation("Discord Mute Shortcut gesendet (Command sent — nicht bestätigt).");
        CommandSent?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }
}

public sealed class DiscordDeafenActionExecutor : IActionExecutor
{
    private readonly SendInputShortcutService _shortcuts;
    private readonly Func<string?> _chordProvider;
    private readonly ILogger<DiscordDeafenActionExecutor>? _logger;
    public ActionType ActionType => ActionType.DiscordDeafen;
    public event EventHandler? CommandSent;

    public DiscordDeafenActionExecutor(
        SendInputShortcutService shortcuts,
        Func<string?> chordProvider,
        ILogger<DiscordDeafenActionExecutor>? logger = null)
    {
        _shortcuts = shortcuts;
        _chordProvider = chordProvider;
        _logger = logger;
    }

    public Task ExecuteAsync(ActionRef action, CancellationToken cancellationToken = default)
    {
        var chord = action.Parameters.GetValueOrDefault("chord") ?? _chordProvider();
        if (string.IsNullOrWhiteSpace(chord))
            throw new InvalidOperationException("Discord Deafen Shortcut ist nicht konfiguriert.");
        _shortcuts.SendChord(chord);
        _logger?.LogInformation("Discord Deafen Shortcut gesendet (Command sent — nicht bestätigt).");
        CommandSent?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }
}

public sealed class MediaPlayPauseExecutor : IActionExecutor
{
    private readonly MediaKeyService _media;
    public MediaPlayPauseExecutor(MediaKeyService media) => _media = media;
    public ActionType ActionType => ActionType.MediaPlayPause;
    public Task ExecuteAsync(ActionRef action, CancellationToken cancellationToken = default)
    {
        _media.PlayPause();
        return Task.CompletedTask;
    }
}

public sealed class MediaNextExecutor : IActionExecutor
{
    private readonly MediaKeyService _media;
    public MediaNextExecutor(MediaKeyService media) => _media = media;
    public ActionType ActionType => ActionType.MediaNext;
    public Task ExecuteAsync(ActionRef action, CancellationToken cancellationToken = default)
    {
        _media.Next();
        return Task.CompletedTask;
    }
}

public sealed class MediaPreviousExecutor : IActionExecutor
{
    private readonly MediaKeyService _media;
    public MediaPreviousExecutor(MediaKeyService media) => _media = media;
    public ActionType ActionType => ActionType.MediaPrevious;
    public Task ExecuteAsync(ActionRef action, CancellationToken cancellationToken = default)
    {
        _media.Previous();
        return Task.CompletedTask;
    }
}

public sealed class LaunchApplicationExecutor : IActionExecutor
{
    private readonly ProcessLaunchService _launch;
    public LaunchApplicationExecutor(ProcessLaunchService launch) => _launch = launch;
    public ActionType ActionType => ActionType.LaunchApplication;
    public Task ExecuteAsync(ActionRef action, CancellationToken cancellationToken = default)
    {
        var path = action.Parameters.GetValueOrDefault("path")
                   ?? throw new InvalidOperationException("path fehlt.");
        _launch.Launch(path);
        return Task.CompletedTask;
    }
}

public sealed class SwitchProfileExecutor : IActionExecutor
{
    private readonly Func<string, Task> _switchProfile;
    public SwitchProfileExecutor(Func<string, Task> switchProfile) => _switchProfile = switchProfile;
    public ActionType ActionType => ActionType.SwitchProfile;
    public Task ExecuteAsync(ActionRef action, CancellationToken cancellationToken = default)
    {
        var id = action.Parameters.GetValueOrDefault("profileId")
                 ?? throw new InvalidOperationException("profileId fehlt.");
        return _switchProfile(id);
    }
}
