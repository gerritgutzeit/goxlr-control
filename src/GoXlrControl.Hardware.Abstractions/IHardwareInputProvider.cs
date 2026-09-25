namespace GoXlrControl.Hardware.Abstractions;

public enum HardwareConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    UtilityMissing,
    OfficialAppConflict,
    NoDevice,
    HttpDisabled
}

public enum FaderId
{
    A,
    B,
    C,
    D
}

public enum HardwareButtonId
{
    Fader1Mute,
    Fader2Mute,
    Fader3Mute,
    Fader4Mute,
    Bleep,
    Cough
}

public sealed record HardwareDeviceInfo(
    string SerialNumber,
    string DeviceType,
    string ProductName,
    string? ProfileName,
    string? DaemonVersion);

public sealed record FaderValueChanged(
    string SerialNumber,
    FaderId Fader,
    string ChannelName,
    double NormalizedValue,
    byte RawVolume,
    DateTimeOffset Timestamp,
    bool IsInitial);

public sealed record ButtonStateChanged(
    string SerialNumber,
    HardwareButtonId Button,
    bool IsPressed,
    DateTimeOffset Timestamp,
    bool IsInitial);

public interface IHardwareInputProvider : IAsyncDisposable
{
    HardwareConnectionState ConnectionState { get; }
    IReadOnlyList<HardwareDeviceInfo> Devices { get; }
    event EventHandler<HardwareConnectionState>? ConnectionChanged;
    event EventHandler<FaderValueChanged>? FaderChanged;
    event EventHandler<ButtonStateChanged>? ButtonChanged;
    event EventHandler<string>? DiagnosticMessage;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
