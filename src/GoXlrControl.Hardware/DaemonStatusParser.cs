using System.Text.Json.Nodes;
using GoXlrControl.Hardware.Abstractions;

namespace GoXlrControl.Hardware;

public static class DaemonStatusParser
{
    private static readonly string[] FaderNames = ["A", "B", "C", "D"];
    private static readonly string[] MiniButtons =
        ["Fader1Mute", "Fader2Mute", "Fader3Mute", "Fader4Mute", "Bleep", "Cough"];

    public static string? GetDaemonVersion(JsonNode status) =>
        status["config"]?["daemon_version"]?.GetValue<string>();

    public static bool IsHttpEnabled(JsonNode status) =>
        status["config"]?["http_settings"]?["enabled"]?.GetValue<bool>() == true;

    public static (string Host, int Port) GetHttpEndpoint(JsonNode status)
    {
        var http = status["config"]?["http_settings"];
        var bind = http?["bind_address"]?.GetValue<string>() ?? "127.0.0.1";
        var port = http?["port"]?.GetValue<int>() ?? 14564;
        if (bind is "0.0.0.0" or "::" or "localhost")
            bind = "127.0.0.1";
        return (bind, port);
    }

    public static IReadOnlyList<HardwareDeviceInfo> ParseDevices(JsonNode status)
    {
        var mixers = status["mixers"]?.AsObject();
        if (mixers is null) return Array.Empty<HardwareDeviceInfo>();

        var list = new List<HardwareDeviceInfo>();
        var version = GetDaemonVersion(status);
        foreach (var (serial, mixer) in mixers)
        {
            if (mixer is null) continue;
            var hw = mixer["hardware"];
            list.Add(new HardwareDeviceInfo(
                serial,
                hw?["device_type"]?.GetValue<string>() ?? "Unknown",
                hw?["usb_device"]?["product_name"]?.GetValue<string>() ?? "GoXLR",
                mixer["profile_name"]?.GetValue<string>(),
                version));
        }

        return list;
    }

    public static IEnumerable<FaderSnapshot> ParseFaders(JsonNode status, string serial)
    {
        var mixer = status["mixers"]?[serial];
        if (mixer is null) yield break;

        foreach (var fader in FaderNames)
        {
            var channel = mixer["fader_status"]?[fader]?["channel"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(channel)) continue;
            var raw = mixer["levels"]?["volumes"]?[channel]?.GetValue<byte>() ?? (byte)0;
            yield return new FaderSnapshot(
                Enum.Parse<FaderId>(fader),
                channel,
                raw,
                raw / 255.0);
        }
    }

    public static IEnumerable<ButtonSnapshot> ParseButtons(JsonNode status, string serial)
    {
        var buttonDown = status["mixers"]?[serial]?["button_down"];
        if (buttonDown is null) yield break;

        foreach (var name in MiniButtons)
        {
            var pressed = buttonDown[name]?.GetValue<bool>() == true;
            if (!Enum.TryParse<HardwareButtonId>(name, out var id)) continue;
            yield return new ButtonSnapshot(id, pressed);
        }
    }
}

public readonly record struct FaderSnapshot(FaderId Fader, string ChannelName, byte RawVolume, double Normalized);
public readonly record struct ButtonSnapshot(HardwareButtonId Button, bool IsPressed);
