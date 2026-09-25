using GoXlrControl.Engine;

namespace GoXlrControl.App.ViewModels;

/// <summary>Formats Discord voice snapshots for UI bindings.</summary>
internal static class DiscordStatusPresentation
{
    public static string FormatTriState(DiscordTriState s) => s switch
    {
        DiscordTriState.On => "On",
        DiscordTriState.Off => "Off",
        _ => "Unknown"
    };

    public static string FormatReliability(DiscordStatusReliability r) => r switch
    {
        DiscordStatusReliability.Confirmed => "Confirmed",
        DiscordStatusReliability.Mirrored => "Mirrored (LED)",
        DiscordStatusReliability.CommandSent => "Command sent",
        _ => "Unknown"
    };
}
