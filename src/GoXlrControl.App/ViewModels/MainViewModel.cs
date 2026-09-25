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
    private readonly SettingsStore _settingsStore;
    private readonly UtilityPatcher _patcher;

    public MainViewModel(
        AppBootstrapper bootstrap,
        IHardwareInputProvider hardware,
        ControllerEngine engine,
        WindowsAudioService audio,
        ProfileStore profiles,
        DiagnosticLog log,
        SettingsStore settingsStore,
        UtilityPatcher patcher)
    {
        _bootstrap = bootstrap;
        _hardware = hardware;
        _engine = engine;
        _audio = audio;
        _profiles = profiles;
        _log = log;
        _settingsStore = settingsStore;
        _patcher = patcher;

        Faders =
        [
            new FaderVm("A"), new FaderVm("B"), new FaderVm("C"), new FaderVm("D")
        ];

        _hardware.ConnectionChanged += (_, s) => App.Current.Dispatcher.Invoke(() =>
        {
            ConnectionState = s.ToString();
            ShowUtilityPatcher = s is HardwareConnectionState.UtilityMissing
                or HardwareConnectionState.OfficialAppConflict;
            RefreshDevice();
        });
        _hardware.FaderChanged += (_, e) => App.Current.Dispatcher.Invoke(() =>
        {
            var vm = Faders[(int)e.Fader];
            vm.HardwareValue = e.NormalizedValue;
            vm.Channel = e.ChannelName;
            vm.Raw = e.RawVolume;
        });
        _hardware.ButtonChanged += (_, e) => App.Current.Dispatcher.Invoke(() =>
        {
            LastButtonEvent = $"{e.Button}: {(e.IsPressed ? "DOWN" : "UP")} @ {e.Timestamp:HH:mm:ss.fff}";
        });
        _engine.FaderOutputChanged += (_, id) => App.Current.Dispatcher.Invoke(() =>
        {
            if (_engine.LastTargetValues.TryGetValue(id, out var v))
                Faders[(int)id].TargetValue = v;
        });
        _log.EntryAdded += (_, line) => App.Current.Dispatcher.Invoke(() =>
        {
            LogLines.Insert(0, line);
            if (LogLines.Count > 300) LogLines.RemoveAt(LogLines.Count - 1);
        });
        _bootstrap.Changed += (_, _) => App.Current.Dispatcher.Invoke(RefreshAll);

        RefreshAll();
        ConnectionState = _hardware.ConnectionState.ToString();
        ShowUtilityPatcher = _hardware.ConnectionState is HardwareConnectionState.UtilityMissing
            or HardwareConnectionState.OfficialAppConflict;
    }

    public ObservableCollection<FaderVm> Faders { get; }
    public ObservableCollection<string> LogLines { get; } = new();
    public ObservableCollection<ControllerProfile> Profiles { get; } = new();
    public ObservableCollection<AudioEndpointInfo> Endpoints { get; } = new();
    public ObservableCollection<AudioSessionInfo> Sessions { get; } = new();
    public ObservableCollection<ButtonBinding> Buttons { get; } = new();

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
    [ObservableProperty] private string? discordMuteChord;
    [ObservableProperty] private string? discordDeafenChord;
    [ObservableProperty] private string statusMessage = "Bereit";
    [ObservableProperty] private ControllerProfile? selectedProfile;
    [ObservableProperty] private FaderVm? selectedFader;
    [ObservableProperty] private ButtonBinding? selectedButton;
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
        binding.Label = SelectedSession.DisplayName;
        SelectedFader.Label = binding.Label;
        SelectedFader.TargetSummary = DescribeTarget(binding.Target);
        _profiles.Save(SelectedProfile);
        _engine.SetProfile(SelectedProfile);
        StatusMessage = $"Fader {SelectedFader.Id} → {SelectedSession.DisplayName}";
    }

    [RelayCommand]
    private void SetFaderTargetMaster()
    {
        if (SelectedFader is null || SelectedProfile is null) return;
        var binding = SelectedProfile.Faders.First(f => f.FaderId == SelectedFader.Id);
        binding.Target = new FaderTarget { Kind = FaderTargetKind.MasterVolume };
        binding.Label = "Master";
        SelectedFader.Label = binding.Label;
        SelectedFader.TargetSummary = "Master Volume";
        _profiles.Save(SelectedProfile);
        _engine.SetProfile(SelectedProfile);
    }

    [RelayCommand]
    private void SaveButtonAction(string actionType)
    {
        if (SelectedButton is null || SelectedProfile is null) return;
        if (!Enum.TryParse<ActionType>(actionType, out var type)) return;
        SelectedButton.OnPress = new ActionRef { Type = type };
        _profiles.Save(SelectedProfile);
        _engine.SetProfile(SelectedProfile);
        StatusMessage = $"{SelectedButton.ButtonId} → {type}";
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
        s.ControllerPaused = ControllerPaused;
        _bootstrap.SaveSettings(s);
        StatusMessage = "Einstellungen gespeichert. Hardware-Provider-Wechsel erfordert Neustart.";
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

    [RelayCommand]
    private void ExportDiagnostics()
    {
        var dlg = new SaveFileDialog { Filter = "Text|*.txt", FileName = $"goxlr-diag-{DateTime.Now:yyyyMMdd-HHmmss}.txt" };
        if (dlg.ShowDialog() != true) return;
        var report = _log.ExportReport(new Dictionary<string, string>
        {
            ["Connection"] = ConnectionState,
            ["Device"] = DeviceSummary.Replace('\n', ' '),
            ["Profile"] = ActiveProfileName
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
        Buttons.Clear();
        foreach (var b in _engine.ActiveProfile.Buttons)
            Buttons.Add(b);
    }

    private void SyncFaderLabels(ControllerProfile profile)
    {
        foreach (var f in profile.Faders)
        {
            var vm = Faders.FirstOrDefault(x => x.Id == f.FaderId);
            if (vm is null) continue;
            vm.Label = string.IsNullOrWhiteSpace(f.Label) ? f.FaderId : f.Label;
            vm.TargetSummary = DescribeTarget(f.Target);
            vm.SyncMode = f.SyncMode.ToString();
        }
    }

    private static string DescribeTarget(FaderTarget t) => t.Kind switch
    {
        FaderTargetKind.None => "Nicht zugewiesen",
        FaderTargetKind.MasterVolume => "Master Volume",
        FaderTargetKind.EndpointVolume => $"Endpoint {t.DeviceId}",
        FaderTargetKind.Application => t.Application?.DisplayName ?? "App",
        FaderTargetKind.ApplicationGroup => $"Gruppe ({t.Applications.Count})",
        FaderTargetKind.DiscordPlayback => "Discord Playback",
        _ => t.Kind.ToString()
    };
}

public partial class FaderVm : ObservableObject
{
    public FaderVm(string id) => Id = id;
    public string Id { get; }
    [ObservableProperty] private string label = "";
    [ObservableProperty] private string channel = "—";
    [ObservableProperty] private double hardwareValue;
    [ObservableProperty] private double targetValue;
    [ObservableProperty] private byte raw;
    [ObservableProperty] private string targetSummary = "—";
    [ObservableProperty] private string syncMode = "SoftTakeover";
}
