using System.IO;
using System.Text;
using System.Windows;
using GoXlrControl.Diagnostics;
using GoXlrControl.Hardware;
using GoXlrControl.Hardware.Abstractions;

namespace GoXlrControl.DiagHost;

public partial class MainWindow : Window
{
    private readonly DiagnosticLog _log = new();
    private IHardwareInputProvider? _provider;
    private readonly Dictionary<FaderId, string> _faderLines = new();

    public MainWindow()
    {
        InitializeComponent();
        StatusText.Text = "Stopped";
        _log.EntryAdded += (_, line) => Dispatcher.Invoke(() =>
        {
            LogList.Items.Insert(0, line);
            if (LogList.Items.Count > 500) LogList.Items.RemoveAt(LogList.Items.Count - 1);
        });
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        await StopProviderAsync();
        var useSim = MessageBox.Show(
            "Simulated Hardware verwenden?\n\nYes = Simulation\nNo = GoXLR Utility",
            "Provider", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

        _provider = useSim ? new SimulatedHardwareProvider() : new GoXlrUtilityProvider();
        _provider.ConnectionChanged += (_, state) => Dispatcher.Invoke(() =>
        {
            StatusText.Text = state.ToString();
            _log.Info($"Connection: {state}");
            RefreshDevice();
        });
        _provider.DiagnosticMessage += (_, msg) => _log.Info(msg);
        _provider.FaderChanged += (_, ev) => Dispatcher.Invoke(() =>
        {
            _faderLines[ev.Fader] =
                $"{ev.Timestamp:HH:mm:ss.fff} {(ev.IsInitial ? "INIT" : "CHG ")} {ev.Fader} ch={ev.ChannelName} raw={ev.RawVolume,3} norm={ev.NormalizedValue:0.000}";
            FaderText.Text = string.Join(Environment.NewLine, _faderLines.OrderBy(kv => kv.Key).Select(kv => kv.Value));
            if (!ev.IsInitial)
                _log.Info($"Fader {ev.Fader}: {ev.RawVolume}/255 ({ev.ChannelName})");
        });
        _provider.ButtonChanged += (_, ev) => Dispatcher.Invoke(() =>
        {
            ButtonText.Text =
                $"{ev.Timestamp:HH:mm:ss.fff} {(ev.IsInitial ? "INIT" : "CHG ")} {ev.Button} pressed={ev.IsPressed}";
            if (!ev.IsInitial)
                _log.Info($"Button {ev.Button}: {(ev.IsPressed ? "DOWN" : "UP")}");
        });

        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        await _provider.StartAsync();
        RefreshDevice();
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        await StopProviderAsync();
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        StatusText.Text = "Stopped";
    }

    private void Simulate_Click(object sender, RoutedEventArgs e)
    {
        if (_provider is not SimulatedHardwareProvider sim)
        {
            MessageBox.Show("Nur im Simulated-Modus verfügbar.");
            return;
        }

        _ = Task.Run(async () =>
        {
            for (var step = 0; step <= 20; step++)
            {
                var v = step / 20.0;
                sim.SetFader(FaderId.A, v);
                sim.SetFader(FaderId.B, 1 - v);
                await Task.Delay(30);
            }

            sim.SetButton(HardwareButtonId.Fader1Mute, true);
            await Task.Delay(80);
            sim.SetButton(HardwareButtonId.Fader1Mute, false);
        });
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            $"goxlr-diag-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        var report = _log.ExportReport(new Dictionary<string, string>
        {
            ["ProviderState"] = _provider?.ConnectionState.ToString() ?? "none",
            ["Devices"] = string.Join(", ", _provider?.Devices.Select(d => $"{d.DeviceType}:{d.SerialNumber}") ?? Array.Empty<string>())
        });
        File.WriteAllText(path, report, Encoding.UTF8);
        MessageBox.Show($"Exportiert: {path}");
    }

    private void RefreshDevice()
    {
        if (_provider is null)
        {
            DeviceText.Text = "—";
            return;
        }

        if (_provider.Devices.Count == 0)
        {
            DeviceText.Text = "Kein Gerät";
            return;
        }

        var d = _provider.Devices[0];
        DeviceText.Text = $"{d.ProductName}\nType: {d.DeviceType}\nSerial: {d.SerialNumber}\nProfile: {d.ProfileName}\nUtility: {d.DaemonVersion}";
    }

    private async Task StopProviderAsync()
    {
        if (_provider is null) return;
        await _provider.StopAsync();
        await _provider.DisposeAsync();
        _provider = null;
    }

    protected override async void OnClosed(EventArgs e)
    {
        await StopProviderAsync();
        base.OnClosed(e);
    }
}
