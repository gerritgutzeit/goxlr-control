using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace GoXlrControl.Integrations;

public sealed class SendInputShortcutService
{
    private const uint InputKeyboard = 1;
    private const uint KeyeventfKeyup = 0x0002;
    private const uint KeyeventfExtendedkey = 0x0001;
    private static readonly TimeSpan KeyHold = TimeSpan.FromMilliseconds(40);

    private readonly ILogger<SendInputShortcutService>? _logger;

    public SendInputShortcutService(ILogger<SendInputShortcutService>? logger = null) => _logger = logger;

    public void SendChord(string chord)
    {
        var keys = ParseChord(chord);
        if (keys.Count == 0)
            throw new ArgumentException($"Leerer oder ungültiger Shortcut: '{chord}'");

        // Down all modifiers/keys, brief hold (Discord hotkeys often miss zero-dwell batches), then up.
        SendKeyBatch(keys, keyUp: false);
        Thread.Sleep(KeyHold);
        var ups = new List<ushort>(keys);
        ups.Reverse();
        SendKeyBatch(ups, keyUp: true);

        _logger?.LogInformation("Shortcut gesendet: {Chord}", chord);
    }

    private void SendKeyBatch(IReadOnlyList<ushort> keys, bool keyUp)
    {
        var inputs = new INPUT[keys.Count];
        for (var i = 0; i < keys.Count; i++)
            inputs[i] = CreateKey(keys[i], keyUp);

        var sent = SendInput((uint)inputs.Length, inputs, INPUT.Size);
        if (sent != inputs.Length)
        {
            var err = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            _logger?.LogWarning("SendInput {Sent}/{Expected}: {Error}", sent, inputs.Length, err);
            throw new InvalidOperationException(
                $"SendInput konnte den Shortcut nicht senden ({sent}/{inputs.Length}). " +
                $"Läuft Discord als Admin und diese App nicht? ({err})");
        }
    }

    /// <summary>
    /// Parses chords like "Ctrl+Shift+M". Prefer left modifiers — Discord keybinds match those.
    /// </summary>
    public static List<ushort> ParseChord(string chord)
    {
        var result = new List<ushort>();
        foreach (var part in chord.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            result.Add(part.ToLowerInvariant() switch
            {
                "ctrl" or "control" => 0x11, // VK_CONTROL (generic — Discord keybinds)
                "lctrl" or "lcontrol" => 0xA2,
                "rctrl" or "rcontrol" => 0xA3,
                "alt" or "menu" => 0x12, // VK_MENU
                "lalt" or "lmenu" => 0xA4,
                "ralt" or "rmenu" or "altgr" => 0xA5,
                "shift" => 0x10, // VK_SHIFT
                "lshift" => 0xA0,
                "rshift" => 0xA1,
                "win" or "lwin" or "super" => 0x5B,
                "rwin" => 0x5C,
                "enter" or "return" => 0x0D,
                "space" => 0x20,
                "tab" => 0x09,
                "esc" or "escape" => 0x1B,
                "backspace" or "bksp" => 0x08,
                "delete" or "del" => 0x2E,
                "insert" or "ins" => 0x2D,
                "home" => 0x24,
                "end" => 0x23,
                "pageup" or "pgup" => 0x21,
                "pagedown" or "pgdn" => 0x22,
                "up" => 0x26,
                "down" => 0x28,
                "left" => 0x25,
                "right" => 0x27,
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

    private static INPUT CreateKey(ushort vk, bool keyUp)
    {
        // Virtual-key based (RegisterHotKey / Discord keybinds). Scan is a hint only —
        // KEYEVENTF_SCANCODE alone breaks many global hotkey listeners.
        var flags = keyUp ? KeyeventfKeyup : 0u;
        if (IsExtendedKey(vk))
            flags |= KeyeventfExtendedkey;

        return new INPUT
        {
            type = InputKeyboard,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = (ushort)MapVirtualKey(vk, 0),
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
    }

    private static bool IsExtendedKey(ushort vk) => vk is
        0xA3 or 0xA5 or 0x5B or 0x5C or // RCtrl, RAlt, Win
        0x2E or 0x2D or 0x24 or 0x23 or 0x21 or 0x22 or // Del/Ins/Home/End/Pg
        0x25 or 0x26 or 0x27 or 0x28; // arrows

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
        public static int Size => Marshal.SizeOf<INPUT>();
    }

    /// <summary>
    /// Union must be sized to the largest member (MOUSEINPUT) or x64 SendInput silently fails.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
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
