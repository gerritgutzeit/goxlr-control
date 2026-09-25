using System.Text.Json.Nodes;
using FluentAssertions;
using GoXlrControl.Hardware;

namespace GoXlrControl.Hardware.Tests;

public class DaemonStatusParserTests
{
    private const string SampleStatus = """
    {
      "config": {
        "daemon_version": "1.2.4",
        "http_settings": { "enabled": true, "bind_address": "0.0.0.0", "cors_enabled": false, "port": 14564 }
      },
      "mixers": {
        "ABC123": {
          "hardware": {
            "device_type": "Mini",
            "serial_number": "ABC123",
            "usb_device": { "product_name": "GoXLR Mini" }
          },
          "profile_name": "Default",
          "fader_status": {
            "A": { "channel": "System" },
            "B": { "channel": "Game" },
            "C": { "channel": "Chat" },
            "D": { "channel": "Music" }
          },
          "levels": {
            "volumes": {
              "System": 128,
              "Game": 64,
              "Chat": 200,
              "Music": 10,
              "Mic": 0,
              "LineIn": 0,
              "Console": 0,
              "Sample": 0,
              "Headphones": 0,
              "MicMonitor": 0,
              "LineOut": 0
            }
          },
          "button_down": {
            "Fader1Mute": false,
            "Fader2Mute": true,
            "Fader3Mute": false,
            "Fader4Mute": false,
            "Bleep": false,
            "Cough": false
          }
        }
      }
    }
    """;

    [Fact]
    public void ParsesDevicesAndHttp()
    {
        var status = JsonNode.Parse(SampleStatus)!;
        DaemonStatusParser.IsHttpEnabled(status).Should().BeTrue();
        var (host, port) = DaemonStatusParser.GetHttpEndpoint(status);
        host.Should().Be("127.0.0.1");
        port.Should().Be(14564);

        var devices = DaemonStatusParser.ParseDevices(status);
        devices.Should().HaveCount(1);
        devices[0].DeviceType.Should().Be("Mini");
        devices[0].SerialNumber.Should().Be("ABC123");
    }

    [Fact]
    public void ParsesFadersAndButtons()
    {
        var status = JsonNode.Parse(SampleStatus)!;
        var faders = DaemonStatusParser.ParseFaders(status, "ABC123").ToList();
        faders.Should().HaveCount(4);
        faders[0].RawVolume.Should().Be(128);
        faders[0].Normalized.Should().BeApproximately(128 / 255.0, 1e-9);

        var buttons = DaemonStatusParser.ParseButtons(status, "ABC123").ToList();
        buttons.Should().Contain(b => b.Button.ToString() == "Fader2Mute" && b.IsPressed);
    }
}
