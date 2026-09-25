using GoXlrControl.Hardware.Abstractions;

namespace GoXlrControl.Hardware;

/// <summary>
/// Simulated Mini for UI development and automated tests without physical hardware.
/// </summary>
public sealed class SimulatedHardwareProvider : IHardwareInputProvider, IHardwareOutputController
{
    private readonly double[] _faders = [0.5, 0.5, 0.5, 0.5];
    private readonly bool[] _buttons = new bool[6];
    private CancellationTokenSource? _cts;
    private readonly Dictionary<FaderId, (FaderDisplayStyle Style, string C1, string C2)> _faderLights = new();
    private readonly Dictionary<HardwareButtonId, (string C1, string C2)> _buttonLights = new();

    public HardwareConnectionState ConnectionState { get; private set; } = HardwareConnectionState.Disconnected;

    public IReadOnlyList<HardwareDeviceInfo> Devices { get; private set; } =
    [
        new("SIM-MINI-0001", "Mini", "Simulated GoXLR Mini", "SimProfile", "sim-1.0")
    ];

    public bool CanSendCommands => ConnectionState == HardwareConnectionState.Connected;
    public string? ActiveSerial => Devices[0].SerialNumber;

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

    public Task SetFaderDisplayStyleAsync(FaderId fader, FaderDisplayStyle style, CancellationToken ct = default)
    {
        var prev = _faderLights.TryGetValue(fader, out var existing)
            ? existing
            : (FaderDisplayStyle.TwoColour, "222222", "111111");
        _faderLights[fader] = (style, prev.Item2, prev.Item3);
        DiagnosticMessage?.Invoke(this, $"[sim] Fader {fader} style={style}");
        return Task.CompletedTask;
    }

    public Task SetFaderColoursAsync(FaderId fader, string colourOneHex, string colourTwoHex, CancellationToken ct = default)
    {
        var c1 = GoXlrCommandBuilder.NormalizeHex(colourOneHex);
        var c2 = GoXlrCommandBuilder.NormalizeHex(colourTwoHex);
        var style = _faderLights.TryGetValue(fader, out var existing) ? existing.Item1 : FaderDisplayStyle.TwoColour;
        _faderLights[fader] = (style, c1, c2);
        DiagnosticMessage?.Invoke(this, $"[sim] Fader {fader} colours={c1}/{c2}");
        return Task.CompletedTask;
    }

    public Task SetAllFaderColoursAsync(string colourOneHex, string colourTwoHex, CancellationToken ct = default)
    {
        foreach (FaderId f in Enum.GetValues<FaderId>())
            _ = SetFaderColoursAsync(f, colourOneHex, colourTwoHex, ct);
        return Task.CompletedTask;
    }

    public Task SetButtonColoursAsync(HardwareButtonId button, string colourOneHex, string colourTwoHex, CancellationToken ct = default)
    {
        var c1 = GoXlrCommandBuilder.NormalizeHex(colourOneHex);
        var c2 = GoXlrCommandBuilder.NormalizeHex(colourTwoHex);
        _buttonLights[button] = (c1, c2);
        DiagnosticMessage?.Invoke(this, $"[sim] Button {button} colours={c1}/{c2}");
        return Task.CompletedTask;
    }

    public Task SetButtonOffStyleAsync(HardwareButtonId button, LightingOffStyle style, CancellationToken ct = default)
    {
        DiagnosticMessage?.Invoke(this, $"[sim] Button {button} off-style={style}");
        return Task.CompletedTask;
    }

    public Task SetAnimationModeAsync(AnimationMode mode, CancellationToken ct = default)
    {
        DiagnosticMessage?.Invoke(this, $"[sim] AnimationMode={mode}");
        return Task.CompletedTask;
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
