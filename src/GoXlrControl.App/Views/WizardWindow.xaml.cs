using System.IO;
using System.Windows;
using GoXlrControl.App.Services;
using GoXlrControl.Audio;
using GoXlrControl.Config;
using GoXlrControl.Hardware;
using GoXlrControl.Hardware.Abstractions;

namespace GoXlrControl.App.Views;

public partial class WizardWindow : Window
{
    private readonly AppBootstrapper _bootstrap;
    private readonly IHardwareInputProvider _hardware;
    private readonly WindowsAudioService _audio;
    private readonly UtilityPatcher _patcher;
    private int _step;
    private bool _busy;

    private readonly (string Title, string Body)[] _steps =
    [
        ("Runtime", "GoXLR Control Studio benötigt .NET 8 Desktop und Windows 10/11. Dieses Setup prüft die lokale Umgebung."),
        ("Treiber", "Die offiziellen TC-Helicon GoXLR-Treiber müssen installiert sein. Diese App installiert keine Treiber automatisch."),
        ("GoXLR Utility", "Die App kann die GoXLR Utility selbst installieren (winget, sonst GitHub-Installer). Beende die offizielle GoXLR App — beide können nicht parallel laufen."),
        ("Utility-Dienst", "Control Studio startet den Daemon automatisch. Du musst die Utility-UI nicht manuell öffnen."),
        ("API-Verbindung", "Die App verbindet sich über Named Pipe und WebSocket mit der Utility."),
        ("GoXLR Mini", "Die Mini wird über hardware.device_type = Mini erkannt."),
        ("Fader-Test", "Bewege alle vier Fader. In Diagnostics sollten Events erscheinen. Bis zur ersten Bewegung keine Windows-Volumes schreiben."),
        ("Button-Test", "Drücke Mute-, Bleep- und Cough-Tasten. Side-Effects auf dem GoXLR-Mixer sind normal."),
        ("Playback-Gerät", "Wähle dein normales Playback-Gerät. Die GoXLR darf nicht Default Playback/Recording sein."),
        ("Erstes Profil", "Ein Desktop-Profil wird angelegt. Du kannst Zuordnungen später ändern.")
    ];

    public WizardWindow(
        AppBootstrapper bootstrap,
        IHardwareInputProvider hardware,
        WindowsAudioService audio,
        UtilityPatcher patcher)
    {
        InitializeComponent();
        _bootstrap = bootstrap;
        _hardware = hardware;
        _audio = audio;
        _patcher = patcher;
        ShowStep();
    }

    private void ShowStep()
    {
        var (title, body) = _steps[_step];
        StepTitle.Text = $"Schritt {_step + 1}/{_steps.Length}: {title}";
        StepBody.Text = body;
        StepStatus.Text = EvaluateStep();
        BackButton.IsEnabled = _step > 0 && !_busy;
        NextButton.Visibility = _step < _steps.Length - 1 ? Visibility.Visible : Visibility.Collapsed;
        FinishButton.Visibility = _step == _steps.Length - 1 ? Visibility.Visible : Visibility.Collapsed;
        PatchButton.Visibility = _step is 2 or 3 ? Visibility.Visible : Visibility.Collapsed;
        PatchButton.IsEnabled = !_busy;
    }

    private string EvaluateStep()
    {
        return _step switch
        {
            0 => $".NET: {Environment.Version} · OS: {Environment.OSVersion}",
            1 => DetectGoXlrEndpoint(),
            2 => OfficialAppRunning()
                ? "WARNUNG: Offizielle GoXLR App läuft. Bitte beenden."
                : UtilityInstalledHint(),
            3 => UtilityDaemonHint(),
            4 => $"Connection: {_hardware.ConnectionState}",
            5 => _hardware.Devices.FirstOrDefault() is { } d
                ? $"{d.ProductName} ({d.DeviceType}) · {d.SerialNumber}"
                : "Kein Gerät — Simulation in Settings möglich.",
            6 => "Öffne Diagnostics und bewege Fader A–D.",
            7 => "Prüfe Button-Events in Diagnostics.",
            8 => DescribePlayback(),
            9 => $"Aktives Profil: {_bootstrap.Settings.ActiveProfileId}",
            _ => ""
        };
    }

    private string DetectGoXlrEndpoint()
    {
        var endpoints = _audio.GetPlaybackEndpoints();
        var goxlr = endpoints.Where(e => e.IsGoXlr).Select(e => e.FriendlyName).ToList();
        return goxlr.Count == 0
            ? "Kein GoXLR-Endpoint gefunden (Treiber ggf. fehlend)."
            : "GoXLR-Endpoint(s): " + string.Join(", ", goxlr);
    }

    private string DescribePlayback()
    {
        var def = _audio.GetDefaultPlayback();
        if (def is null) return "Kein Default-Playback gelesen.";
        return def.IsGoXlr
            ? $"WARNUNG: Default ist GoXLR ({def.FriendlyName}). Bitte anderes Gerät wählen — diese App ändert Routing nicht automatisch."
            : $"Default Playback OK: {def.FriendlyName}";
    }

    private static bool OfficialAppRunning() =>
        UtilityDaemonLifecycle.IsOfficialAppRunning();

    private static string UtilityInstalledHint()
    {
        var exe = UtilityDaemonLifecycle.FindDaemonExecutable();
        if (exe is not null)
            return $"GoXLR Utility gefunden:\n{exe}";
        return Directory.Exists(@"C:\Program Files\GoXLR Utility")
            ? "GoXLR Utility-Ordner gefunden (Daemon-Exe noch nicht erkannt)."
            : "Utility nicht installiert. Button „Utility installieren / starten“ verwenden.";
    }

    private string UtilityDaemonHint()
    {
        if (UtilityDaemonLifecycle.IsDaemonRunning())
            return $"Daemon läuft · Hardware-Status: {_hardware.ConnectionState}";
        if (!UtilityDaemonLifecycle.IsInstalled())
            return "Utility fehlt — zuerst installieren (Schritt 3).";
        return $"Daemon gestoppt · Hardware-Status: {_hardware.ConnectionState}\nControl Studio startet ihn automatisch; alternativ Button nutzen.";
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (_step > 0) { _step--; ShowStep(); }
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (_step < _steps.Length - 1) { _step++; ShowStep(); }
    }

    private void Finish_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var s = _bootstrap.Settings;
        s.WizardCompleted = true;
        _bootstrap.SaveSettings(s);
        DialogResult = true;
        Close();
    }

    private async void PatchUtility_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        ShowStep();
        var progress = new Progress<string>(msg => StepStatus.Text = msg);
        try
        {
            var result = await _patcher.EnsureReadyAsync(progress).ConfigureAwait(true);
            StepStatus.Text = result switch
            {
                UtilityPatchResult.Ready => "Utility bereit — Daemon läuft.",
                UtilityPatchResult.AlreadyInstalled => "Bereits installiert — Daemon starten…",
                UtilityPatchResult.Installed => "Installiert. Daemon sollte gleich laufen.",
                UtilityPatchResult.OfficialAppConflict => "Offizielle GoXLR App beenden, dann erneut versuchen.",
                UtilityPatchResult.WingetUnavailable => "winget fehlgeschlagen — GitHub-Fallback ebenfalls fehlgeschlagen.",
                UtilityPatchResult.InstallFailed => "Installation fehlgeschlagen. Manuell: winget install GoXLR-on-Linux.GoXLR-Utility",
                UtilityPatchResult.StartFailed => "Installiert, aber Daemon-Start fehlgeschlagen.",
                UtilityPatchResult.NotInstalled => "Utility weiterhin nicht gefunden.",
                _ => result.ToString()
            };
        }
        catch (Exception ex)
        {
            StepStatus.Text = $"Fehler: {ex.Message}";
        }
        finally
        {
            _busy = false;
            ShowStep();
        }
    }
}
