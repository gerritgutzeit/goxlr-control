namespace GoXlrControl.Hardware.Abstractions;

public enum FaderDisplayStyle
{
    TwoColour,
    Gradient,
    Meter,
    GradientMeter
}

/// <summary>How a button looks when the GoXLR considers it "off" (e.g. unmuted mute-button).</summary>
public enum LightingOffStyle
{
    Dimmed,
    Colour2,
    DimmedColour2
}

public enum AnimationMode
{
    RetroRainbow,
    RainbowDark,
    RainbowBright,
    Simple,
    Ripple,
    None
}

/// <summary>
/// Writes lighting/commands to GoXLR via Utility. Separate from input events.
/// </summary>
public interface IHardwareOutputController
{
    bool CanSendCommands { get; }
    string? ActiveSerial { get; }

    Task SetFaderDisplayStyleAsync(FaderId fader, FaderDisplayStyle style, CancellationToken ct = default);
    Task SetFaderColoursAsync(FaderId fader, string colourOneHex, string colourTwoHex, CancellationToken ct = default);
    Task SetAllFaderColoursAsync(string colourOneHex, string colourTwoHex, CancellationToken ct = default);
    Task SetButtonColoursAsync(HardwareButtonId button, string colourOneHex, string colourTwoHex, CancellationToken ct = default);
    Task SetButtonOffStyleAsync(HardwareButtonId button, LightingOffStyle style, CancellationToken ct = default);
    Task SetAnimationModeAsync(AnimationMode mode, CancellationToken ct = default);
}

public sealed record FaderLightingState(FaderId Fader, FaderDisplayStyle Style, string ColourOne, string ColourTwo);
public sealed record ButtonLightingState(HardwareButtonId Button, string ColourOne, string ColourTwo);
