using System.Text.Json.Nodes;
using GoXlrControl.Hardware.Abstractions;

namespace GoXlrControl.Hardware;

/// <summary>
/// Builds DaemonRequest JSON for GoXLR Utility lighting commands.
/// </summary>
public static class GoXlrCommandBuilder
{
    public static JsonNode SetFaderDisplayStyle(string serial, FaderId fader, FaderDisplayStyle style) =>
        Command(serial, new JsonObject
        {
            ["SetFaderDisplayStyle"] = new JsonArray(fader.ToString(), style.ToString())
        });

    public static JsonNode SetFaderColours(string serial, FaderId fader, string colourOne, string colourTwo) =>
        Command(serial, new JsonObject
        {
            ["SetFaderColours"] = new JsonArray(fader.ToString(), NormalizeHex(colourOne), NormalizeHex(colourTwo))
        });

    public static JsonNode SetAllFaderColours(string serial, string colourOne, string colourTwo) =>
        Command(serial, new JsonObject
        {
            ["SetAllFaderColours"] = new JsonArray(NormalizeHex(colourOne), NormalizeHex(colourTwo))
        });

    public static JsonNode SetButtonColours(string serial, HardwareButtonId button, string colourOne, string colourTwo) =>
        Command(serial, new JsonObject
        {
            ["SetButtonColours"] = new JsonArray(button.ToString(), NormalizeHex(colourOne), NormalizeHex(colourTwo))
        });

    public static JsonNode SetButtonOffStyle(string serial, HardwareButtonId button, LightingOffStyle style) =>
        Command(serial, new JsonObject
        {
            ["SetButtonOffStyle"] = new JsonArray(button.ToString(), ToApiOffStyle(style))
        });

    public static JsonNode SetAnimationMode(string serial, AnimationMode mode) =>
        Command(serial, new JsonObject
        {
            ["SetAnimationMode"] = ToApiAnimationMode(mode)
        });

    private static string ToApiOffStyle(LightingOffStyle style) => style switch
    {
        LightingOffStyle.DimmedColour2 => "DimmedColour2",
        LightingOffStyle.Colour2 => "Colour2",
        _ => "Dimmed"
    };

    private static string ToApiAnimationMode(AnimationMode mode) => mode switch
    {
        AnimationMode.RetroRainbow => "RetroRainbow",
        AnimationMode.RainbowDark => "RainbowDark",
        AnimationMode.RainbowBright => "RainbowBright",
        AnimationMode.Simple => "Simple",
        AnimationMode.Ripple => "Ripple",
        _ => "None"
    };

    private static JsonNode Command(string serial, JsonNode goxlrCommand) =>
        new JsonObject
        {
            ["Command"] = new JsonArray(serial, goxlrCommand)
        };

    public static string NormalizeHex(string hex)
    {
        var h = hex.Trim().TrimStart('#');
        if (h.Length != 6)
            throw new ArgumentException($"Farbe muss RRGGBB sein: '{hex}'");
        return h.ToUpperInvariant();
    }
}
