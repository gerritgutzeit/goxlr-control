using GoXlrControl.Audio;
using GoXlrControl.Config;
using GoXlrControl.Diagnostics;
using GoXlrControl.Engine;
using GoXlrControl.Hardware.Abstractions;
using Microsoft.Win32;

namespace GoXlrControl.App.Services;

public sealed class AppBootstrapper
{
    private readonly SettingsStore _settingsStore;
    private readonly ProfileStore _profileStore;
    private readonly IHardwareInputProvider _hardware;
    private readonly ControllerEngine _engine;
    private readonly WindowsAudioService _audio;
    private readonly DiagnosticLog _log;
    private readonly Lazy<TrayService> _tray;
    private readonly LightingFeedbackService _lighting;
    private readonly IDiscordIntegration _discord;
    private AppSettings _settings = new();

    public AppBootstrapper(
        SettingsStore settingsStore,
        ProfileStore profileStore,
        IHardwareInputProvider hardware,
        ControllerEngine engine,
        WindowsAudioService audio,
        DiagnosticLog log,
        Lazy<TrayService> tray,
        LightingFeedbackService lighting,
        IDiscordIntegration discord)
    {
        _settingsStore = settingsStore;
        _profileStore = profileStore;
        _hardware = hardware;
        _engine = engine;
        _audio = audio;
        _log = log;
        _tray = tray;
        _lighting = lighting;
        _discord = discord;
    }

    public AppSettings Settings => _settings;
    public IReadOnlyList<ControllerProfile> Profiles { get; private set; } = Array.Empty<ControllerProfile>();

    public event EventHandler? Changed;

    public async Task InitializeAsync()
    {
        _settings = _settingsStore.Load();
        Profiles = _profileStore.LoadAll();
        var active = Profiles.FirstOrDefault(p => p.Id == _settings.ActiveProfileId) ?? Profiles[0];
        _settings.ActiveProfileId = active.Id;
        _settingsStore.Save(_settings);

        _log.SetMinimumLevel(_settings.LogLevel);
        _audio.Configure(_settings.FollowDefaultPlayback, _settings.SelectedPlaybackDeviceId);
        _engine.SetProfile(active);
        _engine.IsPaused = _settings.ControllerPaused;
        _engine.DiagnosticMessage += (_, m) => _log.Info(m);
        _hardware.DiagnosticMessage += (_, m) => _log.Info(m);
        _hardware.ConnectionChanged += (_, s) => _log.Info($"Hardware: {s}");

        _engine.Start();
        await _hardware.StartAsync().ConfigureAwait(false);
        await _discord.StartAsync().ConfigureAwait(false);
        _lighting.Start();
        _lighting.DiagnosticsChanged += (_, _) => { /* UI binds via service */ };
        ApplyAutostart(_settings.StartWithWindows);
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _log.Info($"Bootstrap abgeschlossen. Discord-Modus: {_discord.Mode}.");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Must run on the WPF UI/STA thread (TaskbarIcon).</summary>
    public void InitializeTray() => _tray.Value.Initialize();

    public Task ActivateProfileAsync(string profileId)
    {
        var profile = _profileStore.LoadById(profileId);
        if (profile is null) return Task.CompletedTask;
        _engine.SetProfile(profile);
        _settings.ActiveProfileId = profileId;
        _settingsStore.Save(_settings);
        Profiles = _profileStore.LoadAll();
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public void SaveSettings(AppSettings settings)
    {
        _settings = settings;
        _settingsStore.Save(settings);
        _log.SetMinimumLevel(settings.LogLevel);
        _audio.Configure(settings.FollowDefaultPlayback, settings.SelectedPlaybackDeviceId);
        _engine.IsPaused = settings.ControllerPaused;
        ApplyAutostart(settings.StartWithWindows);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ReloadProfiles()
    {
        Profiles = _profileStore.LoadAll();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task ShutdownAsync()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        await _discord.StopAsync();
        await _lighting.StopAsync();
        _engine.Stop();
        await _hardware.StopAsync();
        _tray.Value.Dispose();
    }

    private async void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            _log.Info("System Resume — Hardware- und Discord-Reconnect anstoßen.");
            try
            {
                await _hardware.StopAsync();
                await _hardware.StartAsync();
                await _discord.StopAsync();
                await _discord.StartAsync();
            }
            catch (Exception ex)
            {
                _log.Warn($"Resume reconnect: {ex.Message}");
            }
        }
    }

    private static void ApplyAutostart(bool enabled)
    {
        const string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        using var key = Registry.CurrentUser.OpenSubKey(runKey, writable: true) ??
                        Registry.CurrentUser.CreateSubKey(runKey);
        const string name = "GoXlrControlStudio";
        if (enabled)
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
                key.SetValue(name, $"\"{exe}\"");
        }
        else if (key.GetValue(name) is not null)
        {
            key.DeleteValue(name, false);
        }
    }
}
