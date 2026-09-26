using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using GoXlrControl.Config;

namespace GoXlrControl.App.ViewModels;

public partial class FaderVm : ObservableObject
{
    public FaderVm(string id)
    {
        Id = id;
        HardwareLabel = DescribeHardwareLabel(id);
        HardwareHint = DescribeHardwareHint(id, "—");
        ApplyAccent(FaderAccentColours.DefaultFor(id));
    }

    public string Id { get; }

    [ObservableProperty] private string label = "";
    [ObservableProperty] private string channel = "—";
    [ObservableProperty] private double hardwareValue;
    [ObservableProperty] private double targetValue;
    [ObservableProperty] private byte raw;
    [ObservableProperty] private string targetSummary = "—";
    [ObservableProperty] private string syncMode = "Absolute";
    [ObservableProperty] private bool softTakeoverPending;
    [ObservableProperty] private string hardwareLabel = "";
    [ObservableProperty] private string hardwareHint = "";
    [ObservableProperty] private string targetTitle = "";
    [ObservableProperty] private string targetDetail = "";
    [ObservableProperty] private string accentColour = "2EC4B6";
    [ObservableProperty] private Brush accentBrush = Brushes.Teal;
    [ObservableProperty] private Brush accentDimBrush = Brushes.DarkSlateGray;

    public void RefreshFrom(FaderBinding binding)
    {
        Label = string.IsNullOrWhiteSpace(binding.Label) ? binding.FaderId : binding.Label;
        HardwareLabel = DescribeHardwareLabel(Id);
        HardwareHint = DescribeHardwareHint(Id, Channel);
        TargetTitle = DescribeTargetTitle(binding.Target);
        TargetSummary = TargetTitle;
        TargetDetail = DescribeTargetDetail(binding);
        SyncMode = binding.SyncMode.ToString();
        ApplyAccent(FaderAccentColours.Resolve(binding));
    }

    public void ApplyAccent(string hex)
    {
        var resolved = FaderAccentColours.Normalize(hex) ?? FaderAccentColours.DefaultFor(Id);
        AccentColour = resolved;
        var color = ParseColor(resolved);
        AccentBrush = new SolidColorBrush(color);
        AccentDimBrush = new SolidColorBrush(Color.FromRgb(
            (byte)(color.R / 3),
            (byte)(color.G / 3),
            (byte)(color.B / 3)));
    }

    partial void OnChannelChanged(string value) =>
        HardwareHint = DescribeHardwareHint(Id, value);

    private static Color ParseColor(string hex)
    {
        hex = hex.Trim().TrimStart('#');
        return Color.FromRgb(
            Convert.ToByte(hex[..2], 16),
            Convert.ToByte(hex[2..4], 16),
            Convert.ToByte(hex[4..6], 16));
    }

    private static string DescribeHardwareLabel(string faderId) => $"Fader {faderId}";

    private static string DescribeHardwareHint(string faderId, string channel)
    {
        var placement = faderId switch
        {
            "A" => "Physischer Fader ganz links",
            "B" => "Zweiter Fader von links",
            "C" => "Dritter Fader von links",
            "D" => "Physischer Fader ganz rechts",
            _ => "GoXLR-Hardwarefader"
        };
        var ch = string.IsNullOrWhiteSpace(channel) ? "—" : channel;
        return $"{placement} · Utility-Kanal {ch}";
    }

    private static string DescribeTargetTitle(FaderTarget t) => t.Kind switch
    {
        FaderTargetKind.None => "Nicht zugewiesen",
        FaderTargetKind.MasterVolume => "Master Volume",
        FaderTargetKind.EndpointVolume => $"Endpoint {t.DeviceId}",
        FaderTargetKind.Application => t.Application?.DisplayName ?? "App",
        FaderTargetKind.ApplicationGroup => $"Gruppe ({t.Applications.Count})",
        FaderTargetKind.DiscordPlayback => "Discord Playback",
        _ => t.Kind.ToString()
    };

    private static string DescribeTargetDetail(FaderBinding binding)
    {
        var sync = binding.SyncMode == Config.SyncMode.Absolute
            ? "Sync Absolute: setzt die Windows-Lautstärke sofort."
            : "Sync Soft-Takeover: greift erst, wenn der Fader die aktuelle Windows-Position kreuzt (kein Sprung).";

        var target = binding.Target.Kind switch
        {
            FaderTargetKind.None =>
                "Kein Windows-Ziel zugewiesen. Der Fader steuert nichts in Control Studio.",
            FaderTargetKind.MasterVolume =>
                "Steuert die Windows-Master-Wiedergabelautstärke.",
            FaderTargetKind.Application =>
                $"Steuert die Lautstärke von {binding.Target.Application?.DisplayName ?? "der App"}.",
            FaderTargetKind.ApplicationGroup =>
                $"Steuert eine Gruppe von {binding.Target.Applications.Count} Apps.",
            FaderTargetKind.EndpointVolume =>
                $"Steuert das Wiedergabegerät {binding.Target.DeviceId}.",
            FaderTargetKind.DiscordPlayback =>
                "Steuert die Discord-Wiedergabelautstärke.",
            _ => "Zugewiesenes Lautstärkeziel."
        };

        return $"{target} {sync}";
    }
}
