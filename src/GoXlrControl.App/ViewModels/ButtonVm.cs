using CommunityToolkit.Mvvm.ComponentModel;
using GoXlrControl.Config;

namespace GoXlrControl.App.ViewModels;

public partial class ButtonVm : ObservableObject
{
    public string ButtonId { get; private set; } = "";

    [ObservableProperty] private string hardwareLabel = "";
    [ObservableProperty] private string hardwareHint = "";
    [ObservableProperty] private string actionTitle = "";
    [ObservableProperty] private string actionDetail = "";
    [ObservableProperty] private string ledHint = "";
    [ObservableProperty] private string listLine = "";

    public static ButtonVm FromBinding(ButtonBinding binding)
    {
        var vm = new ButtonVm();
        vm.RefreshFrom(binding);
        return vm;
    }

    public void RefreshFrom(ButtonBinding binding)
    {
        ButtonId = binding.ButtonId;
        (HardwareLabel, HardwareHint) = DescribeHardware(binding.ButtonId);
        (ActionTitle, ActionDetail, LedHint) = DescribeAction(binding.OnPress.Type);
        ListLine = $"{HardwareLabel}  ·  {ActionTitle}";
    }

    private static (string Label, string Hint) DescribeHardware(string buttonId) => buttonId switch
    {
        "Fader1Mute" => ("Mute A", "Taste unter Fader A — physische Mute-Taste links"),
        "Fader2Mute" => ("Mute B", "Taste unter Fader B"),
        "Fader3Mute" => ("Mute C", "Taste unter Fader C"),
        "Fader4Mute" => ("Mute D", "Taste unter Fader D"),
        "Bleep" => ("Bleep", "Bleep-Taste (Mini: oft Media / SFX)"),
        "Cough" => ("Cough", "Cough-Taste — Standard für Discord Mic-Mute"),
        _ => (buttonId, "GoXLR-Hardwaretaste")
    };

    private static (string Title, string Detail, string Led) DescribeAction(ActionType type) => type switch
    {
        ActionType.ToggleMasterMute => (
            "Windows Master-Mute",
            "Schaltet die Windows-Wiedergabe-Lautstärke stumm (nicht Discord-Mikrofon).",
            "LED: Rot wenn Windows stumm, sonst Türkis/Grau."),
        ActionType.DiscordMute => (
            "Discord Mic-Mute",
            "Sendet den Discord-Mute-Shortcut (Toggle). LED folgt dem lokalen Mirror.",
            "LED: Rot = Mirror stumm, Türkis = offen, Amber = noch unbekannt."),
        ActionType.DiscordDeafen => (
            "Discord Deafen (Headset)",
            "Sendet den Discord-Deafen-Shortcut (Toggle). Deafen mutet in Discord auch das Mic.",
            "LED: Rot = Mirror deafen an, Türkis = aus, Amber = unbekannt."),
        ActionType.MediaPlayPause => (
            "Media Play/Pause",
            "System-Media-Taste Play/Pause.",
            "LED: keine Discord-/Mute-Anzeige."),
        ActionType.MediaNext => (
            "Media Next",
            "Nächster Titel (System-Media).",
            "LED: keine Discord-/Mute-Anzeige."),
        ActionType.MediaPrevious => (
            "Media Previous",
            "Vorheriger Titel (System-Media).",
            "LED: keine Discord-/Mute-Anzeige."),
        ActionType.None => (
            "Keine Aktion",
            "Control Studio sendet nichts. Die GoXLR-Firmware kann die Taste trotzdem intern nutzen.",
            "LED: grau / unverändert."),
        _ => (
            type.ToString(),
            "Zugewiesene Aktion.",
            "LED: abhängig von der Aktion.")
    };
}
