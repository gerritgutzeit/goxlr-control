using GoXlrControl.Hardware.Abstractions;

namespace GoXlrControl.Hardware;

/// <summary>
/// Simulated Mini for UI development and automated tests without physical hardware.
/// </summary>
public sealed class SimulatedHardwareProvider : IHardwareInputProvider
{
    private readonly double[] _faders = [0.5, 0.5, 0.5, 0.5];
    private readonly bool[] _buttons = new bool[6];
    private CancellationTokenSource? _cts;

    public HardwareConnectionState ConnectionState { get; private set; } = HardwareConnectionState.Disconnected;

    public IReadOnlyList<HardwareDeviceInfo> Devices { get; private set; } =
    [
        new("SIM-MINI-0001", "Mini", "Simulated GoXLR Mini", "SimProfile", "sim-1.0")
    ];

    public event EventHandler<HardwareConnectionState>? ConnectionChanged;
    public event EventHandler<FaderValueChanged>? FaderChanged;
    public event EventHandler<ButtonStateChanged>? ButtonChanged;
    public event EventHandler<string>? DiagnosticMessage;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConnectionState = HardwareConnectionState.Connected;
        ConnectionChanged?.Invoke(this, ConnectionState);
        DiagnosticMessage?.Invoke(this, "Simulated hardware connected.");

        var serial = Devices[0].SerialNumber;
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 4; i++)
        {
            FaderChanged?.Invoke(this, new FaderValueChanged(
                serial, (FaderId)i, ChannelFor(i), _faders[i], (byte)(_faders[i] * 255), now, true));
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _cts?.Cancel();
        ConnectionState = HardwareConnectionState.Disconnected;
        ConnectionChanged?.Invoke(this, ConnectionState);
        return Task.CompletedTask;
    }

    public void SetFader(FaderId fader, double normalized)
    {
        normalized = Math.Clamp(normalized, 0, 1);
        _faders[(int)fader] = normalized;
        FaderChanged?.Invoke(this, new FaderValueChanged(
            Devices[0].SerialNumber, fader, ChannelFor((int)fader),
            normalized, (byte)(normalized * 255), DateTimeOffset.UtcNow, false));
    }

    public void SetButton(HardwareButtonId button, bool pressed)
    {
        _buttons[(int)button] = pressed;
        ButtonChanged?.Invoke(this, new ButtonStateChanged(
            Devices[0].SerialNumber, button, pressed, DateTimeOffset.UtcNow, false));
    }

    private static string ChannelFor(int index) => index switch
    {
        0 => "System",
        1 => "Game",
        2 => "Chat",
        _ => "Music"
    };

    public ValueTask DisposeAsync() => new(StopAsync());
}
