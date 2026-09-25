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
        if (status["mixers"] is not JsonObject mixers)
            return Array.Empty<HardwareDeviceInfo>();

        var list = new List<HardwareDeviceInfo>();
        var version = GetDaemonVersion(status);
        foreach (var (serial, mixer) in mixers)
        {
            if (mixer is not JsonObject mixerObj) continue;
            var hw = mixerObj["hardware"] as JsonObject;
            var usb = hw?["usb_device"] as JsonObject;
            list.Add(new HardwareDeviceInfo(
                serial,
                hw?["device_type"]?.GetValue<string>() ?? "Unknown",
                usb?["product_name"]?.GetValue<string>() ?? "GoXLR",
                mixerObj["profile_name"]?.GetValue<string>(),
                version));
        }

        return list;
    }

    public static IEnumerable<FaderSnapshot> ParseFaders(JsonNode status, string serial)
    {
        if (status["mixers"]?[serial] is not JsonObject mixer) yield break;

        foreach (var fader in FaderNames)
        {
            var faderStatus = mixer["fader_status"] as JsonObject;
            var channel = (faderStatus?[fader] as JsonObject)?["channel"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(channel)) continue;
            var volumes = (mixer["levels"] as JsonObject)?["volumes"] as JsonObject;
            var raw = volumes?[channel]?.GetValue<byte>() ?? (byte)0;
            yield return new FaderSnapshot(
                Enum.Parse<FaderId>(fader),
                channel,
                raw,
                raw / 255.0);
        }
    }

    public static IEnumerable<ButtonSnapshot> ParseButtons(JsonNode status, string serial)
    {
        if (status["mixers"]?[serial]?["button_down"] is not JsonObject buttonDown)
            yield break;

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
