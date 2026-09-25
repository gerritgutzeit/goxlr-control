using System.Runtime.InteropServices;
using GoXlrControl.Config;
using GoXlrControl.Engine;
using Microsoft.Extensions.Logging;

namespace GoXlrControl.Integrations;

public sealed class SendInputShortcutService
{
    private readonly ILogger<SendInputShortcutService>? _logger;

    public SendInputShortcutService(ILogger<SendInputShortcutService>? logger = null) => _logger = logger;

    public void SendChord(string chord)
    {
        var keys = ParseChord(chord);
        if (keys.Count == 0)
            throw new ArgumentException($"Leerer oder ungültiger Shortcut: '{chord}'");

        var inputs = new List<INPUT>();
        foreach (var key in keys)
            inputs.Add(CreateKey(key, keyUp: false));
        for (var i = keys.Count - 1; i >= 0; i--)
            inputs.Add(CreateKey(keys[i], keyUp: true));

        var sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
        if (sent != inputs.Count)
        {
            _logger?.LogWarning("SendInput sendete {Sent}/{Expected} Events", sent, inputs.Count);
            throw new InvalidOperationException("SendInput konnte den Shortcut nicht vollständig senden (UIPI?).");
        }
    }

    public static List<ushort> ParseChord(string chord)
    {
        var result = new List<ushort>();
        foreach (var part in chord.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            result.Add(part.ToLowerInvariant() switch
            {
                "ctrl" or "control" => 0x11,
                "alt" or "menu" => 0x12,
                "shift" => 0x10,
                "win" or "lwin" => 0x5B,
                "enter" or "return" => 0x0D,
                "space" => 0x20,
                "tab" => 0x09,
                "esc" or "escape" => 0x1B,
                var s when s.Length == 1 && char.IsLetterOrDigit(s[0]) =>
                    (ushort)char.ToUpperInvariant(s[0]),
                var s when s.StartsWith("f", StringComparison.OrdinalIgnoreCase) &&
                           int.TryParse(s[1..], out var fn) && fn is >= 1 and <= 24 =>
                    (ushort)(0x70 + fn - 1),
                _ => throw new ArgumentException($"Unbekannte Taste: {part}")
            });
        }

        return result;
    }

    private static INPUT CreateKey(ushort vk, bool keyUp) => new()
    {
        type = 1,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                wScan = 0,
                dwFlags = keyUp ? 0x0002u : 0u,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        }
    };

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}

public sealed class MediaKeyService
{
    private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;
    private const byte VK_MEDIA_NEXT = 0xB0;
    private const byte VK_MEDIA_PREV = 0xB1;

    public void PlayPause() => Tap(VK_MEDIA_PLAY_PAUSE);
    public void Next() => Tap(VK_MEDIA_NEXT);
    public void Previous() => Tap(VK_MEDIA_PREV);

    private static void Tap(byte vk)
    {
        keybd_event(vk, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, 2, UIntPtr.Zero);
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
}

public sealed class ProcessLaunchService
{
    public void Launch(string pathOrCommand)
    {
        if (string.IsNullOrWhiteSpace(pathOrCommand))
            throw new ArgumentException("Pfad fehlt.");
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = pathOrCommand,
            UseShellExecute = true
        });
    }
}
