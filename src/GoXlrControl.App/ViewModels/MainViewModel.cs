using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GoXlrControl.App.Services;
using GoXlrControl.Audio;
using GoXlrControl.Config;
using GoXlrControl.Diagnostics;
using GoXlrControl.Engine;
using GoXlrControl.Hardware;
using GoXlrControl.Hardware.Abstractions;
using Microsoft.Win32;

namespace GoXlrControl.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly AppBootstrapper _bootstrap;
    private readonly IHardwareInputProvider _hardware;
    private readonly ControllerEngine _engine;
    private readonly WindowsAudioService _audio;
    private readonly ProfileStore _profiles;
    private readonly DiagnosticLog _log;
    private readonly UtilityPatcher _patcher;
    private readonly UpdateService _updates;
    private readonly LightingFeedbackService _lighting;
    private readonly IDiscordIntegration _discord;

    public MainViewModel(
        AppBootstrapper bootstrap,
        IHardwareInputProvider hardware,
        ControllerEngine engine,
        WindowsAudioService audio,
        ProfileStore profiles,
        DiagnosticLog log,
        UtilityPatcher patcher,
        UpdateService updates,
        LightingFeedbackService lighting,
        IDiscordIntegration discord)
    {
        _bootstrap = bootstrap;
        _hardware = hardware;
        _engine = engine;
        _audio = audio;
        _profiles = profiles;
        _log = log;
        _patcher = patcher;
        _updates = updates;
        _lighting = lighting;
        _discord = discord;

        Faders =
        [
            new FaderVm("A"), new FaderVm("B"), new FaderVm("C"), new FaderVm("D")
        ];

        _hardware.ConnectionChanged += (_, s) => _ = App.Current.Dispatcher.InvokeAsync(() =>
        {
            ConnectionState = s.ToString();
            ShowUtilityPatcher = s is HardwareConnectionState.UtilityMissing
                or HardwareConnectionState.OfficialAppConflict;
            RefreshDevice();
        });
        _hardware.FaderChanged += (_, e) => _ = App.Current.Dispatcher.InvokeAsync(() =>
        {
            var vm = Faders[(int)e.Fader];
            vm.HardwareValue = e.NormalizedValue;
            vm.Channel = e.ChannelName;
            vm.Raw = e.RawVolume;
            vm.SoftTakeoverPending = _engine.HasSoftTakeoverPending(e.Fader);
        });
        _hardware.ButtonChanged += (_, e) => _ = App.Current.Dispatcher.InvokeAsync(() =>
        {
            LastButtonEvent = $"{e.Button}: {(e.IsPressed ? "DOWN" : "UP")} @ {e.Timestamp:HH:mm:ss.fff}";
        });
        _engine.FaderOutputChanged += (_, id) => _ = App.Current.Dispatcher.InvokeAsync(() =>
        {
            if (_engine.LastTargetValues.TryGetValue(id, out var v))
                Faders[(int)id].TargetValue = v;
            Faders[(int)id].SoftTakeoverPending = _engine.HasSoftTakeoverPending(id);
        });
        _engine.SoftTakeoverPendingChanged += (_, id) => _ = App.Current.Dispatcher.InvokeAsync(() =>
        {
            Faders[(int)id].SoftTakeoverPending = _engine.HasSoftTakeoverPending(id);
            if (_engine.LastTargetValues.TryGetValue(id, out var v))
                Faders[(int)id].TargetValue = v;
        });
        _log.EntryAdded += (_, line) => _ = App.Current.Dispatcher.InvokeAsync(() =>
        {
            LogLines.Insert(0, line);
            if (LogLines.Count > 300) LogLines.RemoveAt(LogLines.Count - 1);
        });
        _bootstrap.Changed += (_, _) => _ = App.Current.Dispatcher.InvokeAsync(RefreshAll);
        _lighting.DiagnosticsChanged += (_, _) => _ = App.Current.Dispatcher.InvokeAsync(() =>
        {
            var summary = _lighting.DiagnosticsSummary;
            if (LightingDiagnostics != summary)
                LightingDiagnostics = summary;
        });
        _updates.PropertyChanged += (_, e) => _ = App.Current.Dispatcher.InvokeAsync(() =>
        {
            if (e.PropertyName is nameof(UpdateService.UpdateAvailable)
                or nameof(UpdateService.AvailableVersion)
                or nameof(UpdateService.IsBusy))
            {
                ShowUpdateButton = _updates.UpdateAvailable;
                UpdateBusy = _updates.IsBusy;
                if (_updates.UpdateAvailable && !string.IsNullOrEmpty(_updates.AvailableVersion))
                    StatusMessage = $"Update {_updates.AvailableVersion} verfügbar";
                ApplyUpdateCommand.NotifyCanExecuteChanged();
            }
        });

        _discord.StateChanged += (_, snap) => _ = App.Current.Dispatcher.InvokeAsync(() => ApplyDiscordSnapshot(snap));
        ApplyDiscordSnapshot(_discord.Current);

        RefreshAll();
        ConnectionState = _hardware.ConnectionState.ToString();
        ShowUtilityPatcher = _hardware.ConnectionState is HardwareConnectionState.UtilityMissing
            or HardwareConnectionState.OfficialAppConflict;
        ShowUpdateButton = _updates.UpdateAvailable;
    }

    public ObservableCollection<FaderVm> Faders { get; }
    public ObservableCollection<string> LogLines { get; } = new();
    public ObservableCollection<ControllerProfile> Profiles { get; } = new();
    public ObservableCollection<AudioEndpointInfo> Endpoints { get; } = new();
    public ObservableCollection<AudioSessionInfo> Sessions { get; } = new();
    public ObservableCollection<ButtonVm> Buttons { get; } = new();

    [ObservableProperty] private string connectionState = "Disconnected";
    [ObservableProperty] private string deviceSummary = "—";
    [ObservableProperty] private string activeProfileName = "—";
    [ObservableProperty] private string lastButtonEvent = "—";
    [ObservableProperty] private string selectedPage = "Dashboard";
    [ObservableProperty] private bool controllerPaused;
    [ObservableProperty] private bool startWithWindows;
    [ObservableProperty] private bool startMinimized;
    [ObservableProperty] private bool closeToTray = true;
    [ObservableProperty] private bool followDefaultPlayback = true;
    [ObservableProperty] private bool useSimulatedHardware;
    [ObservableProperty] private bool autoStartUtilityDaemon = true;
    [ObservableProperty] private bool showUtilityPatcher;
    [ObservableProperty] private bool utilityPatchBusy;
    [ObservableProperty] private bool showUpdateButton;
    [ObservableProperty] private bool updateBusy;
    [ObservableProperty] private string? discordMuteChord;
    [ObservableProperty] private string? discordDeafenChord;
    [ObservableProperty] private string discordIntegrationMode = nameof(Config.DiscordIntegrationMode.Fallback);
    [ObservableProperty] private string discordEffectiveMode = "Fallback";
    [ObservableProperty] private string discordConnectionStatus = "Disconnected";
    [ObservableProperty] private string discordMuteStatus = "Unknown";
    [ObservableProperty] private string discordDeafenStatus = "Unknown";
    [ObservableProperty] private string discordReliability = "Unknown";
    [ObservableProperty] private string discordStatusTimestamp = "—";
    [ObservableProperty] private string statusMessage = "Bereit";
    [ObservableProperty] private string selectedLightingMode = nameof(Config.LightingMode.Status);
    [ObservableProperty] private bool exclusiveLightingControl = true;
    [ObservableProperty] private string lightingDiagnostics = "—";
    [ObservableProperty] private ControllerProfile? selectedProfile;
    [ObservableProperty] private FaderVm? selectedFader;
    [ObservableProperty] private ButtonVm? selectedButton;
    [ObservableProperty] private AudioSessionInfo? selectedSession;

    [RelayCommand]
    private void Navigate(string page) => SelectedPage = page;

    [RelayCommand]
    private void RefreshSessions()
    {
        Sessions.Clear();
        foreach (var s in _audio.GetSessions())
            Sessions.Add(s);
        Endpoints.Clear();
        foreach (var e in _audio.GetPlaybackEndpoints())
            Endpoints.Add(e);
    }

    [RelayCommand]
    private void AssignSelectedSessionToFader()
    {
        if (SelectedFader is null || SelectedSession is null || SelectedProfile is null) return;
        var binding = SelectedProfile.Faders.First(f => f.FaderId == SelectedFader.Id);
        binding.Target = new FaderTarget
        {
            Kind = FaderTargetKind.Application,
            Application = new AppIdentity
            {
                Kind = AppIdentityKind.ExePath,
                Value = SelectedSession.ExecutablePath ?? SelectedSession.DisplayName,
                DisplayName = SelectedSession.DisplayName
            },
            AggregateSessions = true
        };
        binding.SyncMode = SyncMode.Absolute;
        binding.Label = SelectedSession.DisplayName;
        SelectedFader.RefreshFrom(binding);
        SelectedFader.SoftTakeoverPending = false;
        _profiles.Save(SelectedProfile);
        _engine.SetProfile(SelectedProfile);
        StatusMessage = $"{SelectedFader.HardwareLabel} → {SelectedFader.TargetTitle}";
    }

    [RelayCommand]
    private void SetFaderTargetMaster()
    {
        if (SelectedFader is null || SelectedProfile is null) return;
        var binding = SelectedProfile.Faders.First(f => f.FaderId == SelectedFader.Id);
        binding.Target = new FaderTarget { Kind = FaderTargetKind.MasterVolume };
        binding.SyncMode = SyncMode.Absolute;
        binding.Label = "Master";
        SelectedFader.RefreshFrom(binding);
        SelectedFader.SoftTakeoverPending = false;
        _profiles.Save(SelectedProfile);
        _engine.SetProfile(SelectedProfile);
        StatusMessage = $"{SelectedFader.HardwareLabel} → {SelectedFader.TargetTitle}";
    }

    [RelayCommand]
    private void ClearFaderTarget()
    {
        if (SelectedFader is null || SelectedProfile is null) return;
        var binding = SelectedProfile.Faders.First(f => f.FaderId == SelectedFader.Id);
        binding.Target = new FaderTarget { Kind = FaderTargetKind.None };
        binding.Label = SelectedFader.Id switch
        {
            "B" => "App 1",
            "C" => "App 2",
            "D" => "Music",
            _ => SelectedFader.Id
        };
        SelectedFader.RefreshFrom(binding);
        SelectedFader.SoftTakeoverPending = false;
        _profiles.Save(SelectedProfile);
        _engine.SetProfile(SelectedProfile);
        StatusMessage = $"{SelectedFader.HardwareLabel} → {SelectedFader.TargetTitle}";
    }

    [RelayCommand]
    private void SetFaderSyncMode(string mode)
    {
        if (SelectedFader is null || SelectedProfile is null) return;
        if (!Enum.TryParse<SyncMode>(mode, out var syncMode)) return;
        var binding = SelectedProfile.Faders.First(f => f.FaderId == SelectedFader.Id);
        binding.SyncMode = syncMode;
        SelectedFader.RefreshFrom(binding);
        SelectedFader.SoftTakeoverPending = false;
        _profiles.Save(SelectedProfile);
        _engine.SetProfile(SelectedProfile);
        StatusMessage = $"{SelectedFader.HardwareLabel} Sync = {binding.SyncMode}";
    }

    [RelayCommand]
    private void SaveButtonAction(string actionType)
    {
        if (SelectedButton is null || SelectedProfile is null) return;
        if (!Enum.TryParse<ActionType>(actionType, out var type)) return;
        var binding = SelectedProfile.Buttons.FirstOrDefault(b =>
            b.ButtonId.Equals(SelectedButton.ButtonId, StringComparison.OrdinalIgnoreCase));
        if (binding is null) return;
        binding.OnPress = new ActionRef { Type = type };
        SelectedButton.RefreshFrom(binding);
        _profiles.Save(SelectedProfile);
        _engine.SetProfile(SelectedProfile);
        StatusMessage = $"{SelectedButton.HardwareLabel} → {SelectedButton.ActionTitle}";
    }

    [RelayCommand]
    private async Task TestDiscordMuteAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(DiscordMuteChord))
            {
                StatusMessage = "Discord Mute Chord fehlt — unter Einstellungen z.B. Ctrl+Shift+M setzen und Speichern.";
                return;
            }

            // Push live VM chords into bootstrap so Fallback reads them without requiring Speichern first.
            ApplyDiscordChordsToLiveSettings();
            await _discord.ToggleMuteAsync();
            StatusMessage =
                $"Shortcut gesendet ({DiscordMuteChord}). Discord: Einstellungen → Tastenkürzel → " +
                $"denselben Chord für „Stummschalten“ setzen. Discord + App beide als Admin oder beide normal.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Discord Mute fehlgeschlagen: {ex.Message}";
            _log.Warn(StatusMessage);
        }
    }

    private void ApplyDiscordChordsToLiveSettings()
    {
        var s = _bootstrap.Settings;
        s.DiscordMuteChord = DiscordMuteChord;
        s.DiscordDeafenChord = DiscordDeafenChord;
    }

    [RelayCommand]
    private void SyncDiscordMuteOn()
    {
        _discord.SyncMirroredMute(true);
        StatusMessage = "Mirror: Discord Mute = an (LED rot). Kein Shortcut gesendet.";
    }

    [RelayCommand]
    private void SyncDiscordMuteOff()
    {
        _discord.SyncMirroredMute(false);
        StatusMessage = "Mirror: Discord Mute = aus (LED türkis). Kein Shortcut gesendet.";
    }

    [RelayCommand]
    private void SyncDiscordDeafenOn()
    {
        _discord.SyncMirroredDeafen(true);
        StatusMessage = "Mirror: Discord Deafen = an. Kein Shortcut gesendet.";
    }

    [RelayCommand]
    private void SyncDiscordDeafenOff()
    {
        _discord.SyncMirroredDeafen(false);
        StatusMessage = "Mirror: Discord Deafen = aus. Kein Shortcut gesendet.";
    }

    [RelayCommand]
    private async Task ActivateProfileAsync()
    {
        if (SelectedProfile is null) return;
        await _bootstrap.ActivateProfileAsync(SelectedProfile.Id);
        ActiveProfileName = SelectedProfile.Name;
        LoadButtons();
        StatusMessage = $"Profil aktiv: {SelectedProfile.Name}";
    }

    [RelayCommand]
    private void CreateProfile()
    {
        var p = ControllerProfile.CreateDefault($"Profil {Profiles.Count + 1}");
        _profiles.Save(p);
        _bootstrap.ReloadProfiles();
        RefreshProfiles();
    }

    [RelayCommand]
    private void DuplicateProfile()
    {
        if (SelectedProfile is null) return;
        _profiles.Duplicate(SelectedProfile, SelectedProfile.Name + " Kopie");
        _bootstrap.ReloadProfiles();
        RefreshProfiles();
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (SelectedProfile is null || Profiles.Count <= 1) return;
        _profiles.Delete(SelectedProfile.Id);
        _bootstrap.ReloadProfiles();
        RefreshProfiles();
    }

    [RelayCommand]
    private void ExportProfile()
    {
        if (SelectedProfile is null) return;
        var dlg = new SaveFileDialog { Filter = "JSON|*.json", FileName = SelectedProfile.Name + ".json" };
        if (dlg.ShowDialog() == true)
            _profiles.Export(SelectedProfile, dlg.FileName);
    }

    [RelayCommand]
    private void ImportProfile()
    {
        var dlg = new OpenFileDialog { Filter = "JSON|*.json" };
        if (dlg.ShowDialog() != true) return;
        _profiles.Import(dlg.FileName);
        _bootstrap.ReloadProfiles();
        RefreshProfiles();
    }

    [RelayCommand]
    private void SaveSettings()
    {
        var s = _bootstrap.Settings;
        s.StartWithWindows = StartWithWindows;
        s.StartMinimized = StartMinimized;
        s.CloseToTray = CloseToTray;
        s.FollowDefaultPlayback = FollowDefaultPlayback;
        s.UseSimulatedHardware = UseSimulatedHardware;
        s.AutoStartUtilityDaemon = AutoStartUtilityDaemon;
        s.DiscordMuteChord = DiscordMuteChord;
        s.DiscordDeafenChord = DiscordDeafenChord;
        s.DiscordIntegrationMode = Enum.TryParse<Config.DiscordIntegrationMode>(DiscordIntegrationMode, true, out var dm)
            ? dm
            : Config.DiscordIntegrationMode.Fallback;
        s.ControllerPaused = ControllerPaused;
        s.LightingMode = Enum.TryParse<Config.LightingMode>(SelectedLightingMode, true, out var lm)
            ? lm
            : Config.LightingMode.Status;
        s.ExclusiveLightingControl = ExclusiveLightingControl;
        _bootstrap.SaveSettings(s);
        DiscordEffectiveMode = _discord.Mode.ToString();
        StatusMessage = s.DiscordIntegrationMode == Config.DiscordIntegrationMode.Fallback
            ? "Einstellungen gespeichert."
            : "Einstellungen gespeichert. Hybrid/Native laufen noch als Fallback (Shortcuts) — Discord-Approval fehlt.";
    }

    [RelayCommand(CanExecute = nameof(CanPatchUtility))]
    private async Task PatchUtilityAsync()
    {
        UtilityPatchBusy = true;
        PatchUtilityCommand.NotifyCanExecuteChanged();
        try
        {
            var progress = new Progress<string>(msg => StatusMessage = msg);
            var result = await _patcher.EnsureReadyAsync(progress);
            StatusMessage = result switch
            {
                UtilityPatchResult.Ready => "GoXLR Utility bereit.",
                UtilityPatchResult.Installed => "Utility installiert — Verbindung wird aufgebaut.",
                UtilityPatchResult.AlreadyInstalled => "Utility bereits installiert.",
                UtilityPatchResult.OfficialAppConflict => "Offizielle GoXLR App beenden, dann erneut versuchen.",
                UtilityPatchResult.InstallFailed => "Installation fehlgeschlagen.",
                UtilityPatchResult.StartFailed => "Daemon-Start fehlgeschlagen.",
                _ => $"Utility-Patcher: {result}"
            };
        }
        catch (Exception ex)
        {
            StatusMessage = $"Utility-Patcher: {ex.Message}";
        }
        finally
        {
            UtilityPatchBusy = false;
            PatchUtilityCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanPatchUtility() => !UtilityPatchBusy;

    [RelayCommand(CanExecute = nameof(CanApplyUpdate))]
    private async Task ApplyUpdateAsync()
    {
        StatusMessage = "Update wird geladen…";
        await _updates.ApplyUpdateAsync();
    }

    private bool CanApplyUpdate() => ShowUpdateButton && !UpdateBusy;

    [RelayCommand]
    private void ExportDiagnostics()
    {
        var dlg = new SaveFileDialog { Filter = "Text|*.txt", FileName = $"goxlr-diag-{DateTime.Now:yyyyMMdd-HHmmss}.txt" };
        if (dlg.ShowDialog() != true) return;
        var report = _log.ExportReport(new Dictionary<string, string>
        {
            ["Connection"] = ConnectionState,
            ["Device"] = DeviceSummary.Replace('\n', ' '),
            ["Profile"] = ActiveProfileName,
            ["DiscordMode"] = DiscordIntegrationMode,
            ["DiscordReliability"] = DiscordReliability,
            ["DiscordMute"] = DiscordMuteStatus,
            ["DiscordDeafen"] = DiscordDeafenStatus
        });
        System.IO.File.WriteAllText(dlg.FileName, report);
        StatusMessage = "Diagnose exportiert.";
    }

    [RelayCommand]
    private void TogglePause()
    {
        ControllerPaused = !ControllerPaused;
        SaveSettings();
    }

    private void RefreshAll()
    {
        RefreshDevice();
        RefreshProfiles();
        RefreshSessions();
        LoadButtons();
        var s = _bootstrap.Settings;
        ControllerPaused = s.ControllerPaused;
        StartWithWindows = s.StartWithWindows;
        StartMinimized = s.StartMinimized;
        CloseToTray = s.CloseToTray;
        FollowDefaultPlayback = s.FollowDefaultPlayback;
        UseSimulatedHardware = s.UseSimulatedHardware;
        AutoStartUtilityDaemon = s.AutoStartUtilityDaemon;
        DiscordMuteChord = s.DiscordMuteChord;
        DiscordDeafenChord = s.DiscordDeafenChord;
        DiscordIntegrationMode = s.DiscordIntegrationMode.ToString();
        DiscordEffectiveMode = _discord.Mode.ToString();
        ApplyDiscordSnapshot(_discord.Current);
        SelectedLightingMode = s.LightingMode.ToString();
        ExclusiveLightingControl = s.ExclusiveLightingControl;
        LightingDiagnostics = _lighting.DiagnosticsSummary;
        ActiveProfileName = _engine.ActiveProfile.Name;
        SyncFaderLabels(_engine.ActiveProfile);
    }

    private void RefreshDevice()
    {
        if (_hardware.Devices.Count == 0)
        {
            DeviceSummary = "Kein Gerät verbunden";
            return;
        }

        var d = _hardware.Devices[0];
        DeviceSummary = $"{d.ProductName}\n{d.DeviceType} · {d.SerialNumber}\nUtility {d.DaemonVersion} · Profil {d.ProfileName}";
    }

    private void RefreshProfiles()
    {
        Profiles.Clear();
        foreach (var p in _bootstrap.Profiles)
            Profiles.Add(p);
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == _bootstrap.Settings.ActiveProfileId) ?? Profiles.FirstOrDefault();
    }

    private void LoadButtons()
    {
        var previousId = SelectedButton?.ButtonId;
        Buttons.Clear();
        foreach (var b in _engine.ActiveProfile.Buttons)
            Buttons.Add(ButtonVm.FromBinding(b));
        SelectedButton = Buttons.FirstOrDefault(b =>
                               previousId is not null &&
                               b.ButtonId.Equals(previousId, StringComparison.OrdinalIgnoreCase))
                           ?? Buttons.FirstOrDefault();
    }

    private void SyncFaderLabels(ControllerProfile profile)
    {
        var previousId = SelectedFader?.Id;
        foreach (var f in profile.Faders)
        {
            var vm = Faders.FirstOrDefault(x => x.Id == f.FaderId);
            if (vm is null) continue;
            vm.RefreshFrom(f);
            if (Enum.TryParse<FaderId>(f.FaderId, true, out var fid))
                vm.SoftTakeoverPending = _engine.HasSoftTakeoverPending(fid);
        }

        SelectedFader = Faders.FirstOrDefault(x =>
                               previousId is not null &&
                               x.Id.Equals(previousId, StringComparison.OrdinalIgnoreCase))
                       ?? SelectedFader
                       ?? Faders.FirstOrDefault();
    }

    private void ApplyDiscordSnapshot(DiscordVoiceSnapshot snap)
    {
        DiscordConnectionStatus = snap.Connection.ToString();
        DiscordMuteStatus = DiscordStatusPresentation.FormatTriState(snap.Mute);
        DiscordDeafenStatus = DiscordStatusPresentation.FormatTriState(snap.Deafen);
        DiscordReliability = DiscordStatusPresentation.FormatReliability(snap.Reliability);
        DiscordStatusTimestamp = snap.StatusConfirmedAt?.ToLocalTime().ToString("HH:mm:ss") ?? "—";
    }
}
