using System.Globalization;
using GoXlrControl.Abstractions;
using GoXlrControl.Config;
using GoXlrControl.Hardware.Abstractions;
using Microsoft.Extensions.Logging;

namespace GoXlrControl.Engine;

/// <summary>
/// Drives GoXLR fader/button colours from Windows audio state.
/// Uses TwoColour/Gradient only — never Firmware Meter (requires GoXLR audio path).
/// </summary>
public sealed class LightingFeedbackService : IAsyncDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(100); // 10 Hz max
    private const double ColourDelta = 8; // per RGB channel threshold

    private readonly IHardwareInputProvider _hardware;
    private readonly IHardwareOutputController _output;
    private readonly ControllerEngine _engine;
    private readonly IVolumeSink _volume;
    private readonly IDiscordIntegration _discord;
    private readonly Func<AppSettings> _settings;
    private readonly ILogger<LightingFeedbackService>? _logger;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private readonly object _colourCacheGate = new();
    private readonly Dictionary<FaderId, (string C1, string C2)> _lastFaderColours = new();
    private readonly Dictionary<FaderId, FaderDisplayStyle> _lastStyles = new();
    private readonly Dictionary<FaderId, double> _lastPeaks = new();
    private readonly Dictionary<HardwareButtonId, (string C1, string C2)> _lastButtonColours = new();
    private readonly Dictionary<HardwareButtonId, LightingOffStyle> _lastOffStyles = new();
    private AnimationMode? _lastAnimationMode;
    private string _lastDiagnosticsSummary = "Lighting idle";
    private int _forceFullRewrite;
    private static readonly HardwareButtonId[] MiniButtons =
    [
        HardwareButtonId.Fader1Mute, HardwareButtonId.Fader2Mute,
        HardwareButtonId.Fader3Mute, HardwareButtonId.Fader4Mute,
        HardwareButtonId.Bleep, HardwareButtonId.Cough
    ];

    public LightingFeedbackService(
        IHardwareInputProvider hardware,
        IHardwareOutputController output,
        ControllerEngine engine,
        IVolumeSink volume,
        IDiscordIntegration discord,
        Func<AppSettings> settings,
        ILogger<LightingFeedbackService>? logger = null)
    {
        _hardware = hardware;
        _output = output;
        _engine = engine;
        _volume = volume;
        _discord = discord;
        _settings = settings;
        _logger = logger;
    }

    public string DiagnosticsSummary { get; private set; } = "Lighting idle";
    public event EventHandler? DiagnosticsChanged;

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        _hardware.ButtonChanged += OnHardwareButtonChanged;
        _hardware.ConnectionChanged += OnHardwareConnectionChanged;
        _discord.StateChanged += OnDiscordStateChanged;
        _loop = Task.Run(() => RunAsync(_cts.Token), CancellationToken.None);
    }

    public async Task StopAsync()
    {
        _hardware.ButtonChanged -= OnHardwareButtonChanged;
        _hardware.ConnectionChanged -= OnHardwareConnectionChanged;
        _discord.StateChanged -= OnDiscordStateChanged;
        _cts?.Cancel();
        if (_loop is not null)
            await Task.WhenAny(_loop, Task.Delay(1500)).ConfigureAwait(false);
        _loop = null;
        _cts?.Dispose();
        _cts = null;
    }

    private void OnHardwareButtonChanged(object? sender, ButtonStateChanged e)
    {
        // Firmware mute flips on/off colour — force a rewrite on the next tick(s).
        if (e.IsInitial) return;
        RequestForceRewrite();
        // Firmware applies its mute palette a few ms after the press; punch through twice.
        _ = ReassertAfterMuteAsync();
    }

    private async Task ReassertAfterMuteAsync()
    {
        try
        {
            await Task.Delay(40).ConfigureAwait(false);
            RequestForceRewrite();
            await Task.Delay(120).ConfigureAwait(false);
            RequestForceRewrite();
        }
        catch
        {
            // ignore
        }
    }

    private void OnHardwareConnectionChanged(object? sender, HardwareConnectionState state)
    {
        if (state is HardwareConnectionState.Connected or HardwareConnectionState.HttpDisabled)
            RequestForceRewrite();
    }

    private void OnDiscordStateChanged(object? sender, DiscordVoiceSnapshot _) => RequestForceRewrite();

    private void RequestForceRewrite()
    {
        Interlocked.Exchange(ref _forceFullRewrite, 1);
        lock (_colourCacheGate)
        {
            _lastButtonColours.Clear();
            _lastOffStyles.Clear();
            _lastFaderColours.Clear();
            _lastStyles.Clear();
            _lastAnimationMode = null;
        }
    }

    private void PublishDiagnostics(string summary)
    {
        if (string.Equals(_lastDiagnosticsSummary, summary, StringComparison.Ordinal))
            return;
        _lastDiagnosticsSummary = summary;
        DiagnosticsSummary = summary;
        DiagnosticsChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await TickAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Lighting tick failed");
                PublishDiagnostics($"Lighting error: {ex.Message}");
            }

            try
            {
                await Task.Delay(TickInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var settings = _settings();
        if (settings.LightingMode == LightingMode.Off)
        {
            PublishDiagnostics("Lighting: Off");
            return;
        }

        if (!_output.CanSendCommands)
        {
            PublishDiagnostics($"Lighting: waiting ({_hardware.ConnectionState})");
            return;
        }

        var force = Interlocked.Exchange(ref _forceFullRewrite, 0) == 1;
        if (settings.ExclusiveLightingControl)
            await ApplyExclusiveLockAsync(force, ct).ConfigureAwait(false);

        var profile = _engine.ActiveProfile;
        var lines = new List<string>();
        if (settings.ExclusiveLightingControl)
            lines.Add("exclusive");

        foreach (var binding in profile.Faders)
        {
            if (!Enum.TryParse<FaderId>(binding.FaderId, true, out var fader))
                continue;

            var (c1, c2, style, peak) = await ResolveColoursAsync(binding, fader, settings).ConfigureAwait(false);
            lines.Add($"{fader}:{c1}/{c2} p={peak:0.00}");

            bool styleChanged;
            bool colourChanged;
            lock (_colourCacheGate)
            {
                styleChanged = force || !_lastStyles.TryGetValue(fader, out var prevStyle) || prevStyle != style;
                colourChanged = force || !_lastFaderColours.TryGetValue(fader, out var prev) ||
                                ColourChanged(prev.C1, c1) || ColourChanged(prev.C2, c2);
                if (styleChanged)
                    _lastStyles[fader] = style;
                if (colourChanged)
                    _lastFaderColours[fader] = (c1, c2);
                _lastPeaks[fader] = peak;
            }

            if (styleChanged)
                await _output.SetFaderDisplayStyleAsync(fader, style, ct).ConfigureAwait(false);
            if (colourChanged)
                await _output.SetFaderColoursAsync(fader, c1, c2, ct).ConfigureAwait(false);
        }

        await UpdateMuteButtonLightsAsync(profile, settings, force, ct).ConfigureAwait(false);
        await UpdateDiscordButtonLightsAsync(profile, settings, force, ct).ConfigureAwait(false);

        PublishDiagnostics(string.Join(" · ", lines));
    }

    /// <summary>
    /// Utility/firmware maps mute state to Dimmed/Colour2. Disable animations and use Colour2
    /// off-style with identical on/off colours so mute cannot flash the Utility profile palette.
    /// </summary>
    private async Task ApplyExclusiveLockAsync(bool force, CancellationToken ct)
    {
        try
        {
            bool setAnimation;
            var buttonsToUpdate = new List<HardwareButtonId>();
            lock (_colourCacheGate)
            {
                setAnimation = force || _lastAnimationMode != AnimationMode.None;
                if (setAnimation)
                    _lastAnimationMode = AnimationMode.None;

                foreach (var button in MiniButtons)
                {
                    if (!force && _lastOffStyles.TryGetValue(button, out var prev) && prev == LightingOffStyle.Colour2)
                        continue;
                    _lastOffStyles[button] = LightingOffStyle.Colour2;
                    buttonsToUpdate.Add(button);
                }
            }

            if (setAnimation)
                await _output.SetAnimationModeAsync(AnimationMode.None, ct).ConfigureAwait(false);

            foreach (var button in buttonsToUpdate)
                await _output.SetButtonOffStyleAsync(button, LightingOffStyle.Colour2, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Exclusive lighting lock failed");
        }
    }

    private async Task WriteButtonColourAsync(
        HardwareButtonId button, string colour, bool exclusive, bool force, CancellationToken ct)
    {
        // Pin both colour slots to the same value so firmware on/off mute cannot switch palettes.
        var c1 = colour;
        var c2 = exclusive ? colour : Darken(colour);
        try
        {
            bool write;
            lock (_colourCacheGate)
            {
                write = force || !_lastButtonColours.TryGetValue(button, out var prev) ||
                        ColourChanged(prev.C1, c1) || ColourChanged(prev.C2, c2);
                if (write)
                    _lastButtonColours[button] = (c1, c2);
            }

            if (write)
                await _output.SetButtonColoursAsync(button, c1, c2, ct).ConfigureAwait(false);
        }
        catch
        {
            // best-effort
        }
    }

    private static string Darken(string hex)
    {
        if (!TryParseRgb(hex, out var r, out var g, out var b))
            return "1E222A";
        return $"{(byte)(r / 3):X2}{(byte)(g / 3):X2}{(byte)(b / 3):X2}";
    }

    private async Task<(string C1, string C2, FaderDisplayStyle Style, double Peak)> ResolveColoursAsync(
        FaderBinding binding, FaderId fader, AppSettings settings)
    {
        const FaderDisplayStyle style = FaderDisplayStyle.TwoColour;

        if (_hardware.ConnectionState is HardwareConnectionState.Disconnected
            or HardwareConnectionState.UtilityMissing
            or HardwareConnectionState.OfficialAppConflict)
            return ("000000", "000000", style, 0);

        if (_engine.IsPaused)
            return ("1A2740", "0C1424", style, 0);

        if (binding.Target.Kind == FaderTargetKind.None)
            return ("3A3F4A", "1E222A", style, 0);

        var muted = await IsTargetMutedAsync(binding).ConfigureAwait(false);
        if (muted == true)
            return ("E85D4C", "4A1C18", style, 0);

        var pending = binding.SyncMode == SyncMode.SoftTakeover && _engine.HasSoftTakeoverPending(fader);
        if (pending)
            return ("E8A317", "4A3A10", style, 0);

        if (settings.LightingMode == LightingMode.PeakProxy)
        {
            var peak = await ResolvePeakAsync(binding).ConfigureAwait(false);
            var (c1, c2) = PeakToColours(peak);
            return (c1, c2, FaderDisplayStyle.Gradient, peak);
        }

        // Status: mapped / active
        return ("2EC4B6", "0F3D38", style, 0);
    }

    private async Task UpdateMuteButtonLightsAsync(
        ControllerProfile profile, AppSettings settings, bool force, CancellationToken ct)
    {
        if (settings.LightingMode == LightingMode.Off) return;
        var exclusive = settings.ExclusiveLightingControl;

        foreach (var binding in profile.Faders)
        {
            if (!Enum.TryParse<FaderId>(binding.FaderId, true, out var fader))
                continue;

            // Discord-bound mute buttons are handled in UpdateDiscordButtonLightsAsync.
            var buttonBinding = profile.Buttons.FirstOrDefault(b =>
                b.ButtonId.Equals(FaderMuteButton(fader).ToString(), StringComparison.OrdinalIgnoreCase));
            if (buttonBinding?.OnPress.Type is ActionType.DiscordMute or ActionType.DiscordDeafen)
                continue;

            var button = FaderMuteButton(fader);
            var muted = await IsTargetMutedAsync(binding).ConfigureAwait(false);
            var colour = muted == true
                ? "E85D4C"
                : binding.Target.Kind == FaderTargetKind.None
                    ? "3A3F4A"
                    : "2EC4B6";

            await WriteButtonColourAsync(button, colour, exclusive, force, ct).ConfigureAwait(false);
        }
    }

    private static HardwareButtonId FaderMuteButton(FaderId fader) => fader switch
    {
        FaderId.A => HardwareButtonId.Fader1Mute,
        FaderId.B => HardwareButtonId.Fader2Mute,
        FaderId.C => HardwareButtonId.Fader3Mute,
        _ => HardwareButtonId.Fader4Mute
    };

    /// <summary>
    /// Cough / Bleep / remapped mute buttons bound to DiscordMute or DiscordDeafen
    /// follow the local Discord mirror (Mirrored) — not Discord Confirmed without API approval.
    /// </summary>
    private async Task UpdateDiscordButtonLightsAsync(
        ControllerProfile profile, AppSettings settings, bool force, CancellationToken ct)
    {
        if (settings.LightingMode == LightingMode.Off) return;
        var exclusive = settings.ExclusiveLightingControl;

        var snap = _discord.Current;
        foreach (var binding in profile.Buttons)
        {
            if (!Enum.TryParse<HardwareButtonId>(binding.ButtonId, true, out var button))
                continue;

            DiscordTriState state;
            if (binding.OnPress.Type == ActionType.DiscordMute)
            {
                state = snap.Deafen == DiscordTriState.On || snap.Mute == DiscordTriState.On
                    ? DiscordTriState.On
                    : snap.Mute;
            }
            else if (binding.OnPress.Type == ActionType.DiscordDeafen)
            {
                state = snap.Deafen;
            }
            else
            {
                continue;
            }

            var colour = state switch
            {
                DiscordTriState.On => "E85D4C",
                DiscordTriState.Off => "2EC4B6",
                _ => "5C4A20"
            };

            await WriteButtonColourAsync(button, colour, exclusive, force, ct).ConfigureAwait(false);
        }
    }

    private async Task<bool?> IsTargetMutedAsync(FaderBinding binding)
    {
        try
        {
            return binding.Target.Kind switch
            {
                FaderTargetKind.MasterVolume => await _volume.GetMasterMuteAsync().ConfigureAwait(false),
                FaderTargetKind.Application when binding.Target.Application is not null =>
                    await _volume.GetApplicationMuteAsync(binding.Target.Application).ConfigureAwait(false),
                FaderTargetKind.DiscordPlayback when binding.Target.Application is not null =>
                    await _volume.GetApplicationMuteAsync(binding.Target.Application).ConfigureAwait(false),
                FaderTargetKind.EndpointVolume when binding.Target.DeviceId is not null =>
                    // approximate via master if same device; otherwise null
                    await _volume.GetMasterMuteAsync().ConfigureAwait(false),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private async Task<double> ResolvePeakAsync(FaderBinding binding)
    {
        try
        {
            return binding.Target.Kind switch
            {
                FaderTargetKind.MasterVolume => await _volume.GetMasterPeakAsync().ConfigureAwait(false),
                FaderTargetKind.Application when binding.Target.Application is not null =>
                    await _volume.GetApplicationPeakAsync(binding.Target.Application).ConfigureAwait(false),
                FaderTargetKind.DiscordPlayback when binding.Target.Application is not null =>
                    await _volume.GetApplicationPeakAsync(binding.Target.Application).ConfigureAwait(false),
                FaderTargetKind.ApplicationGroup => await PeakForGroupAsync(binding).ConfigureAwait(false),
                _ => await _volume.GetMasterPeakAsync().ConfigureAwait(false)
            };
        }
        catch
        {
            return 0;
        }
    }

    private async Task<double> PeakForGroupAsync(FaderBinding binding)
    {
        double peak = 0;
        foreach (var id in binding.Target.Applications)
            peak = Math.Max(peak, await _volume.GetApplicationPeakAsync(id).ConfigureAwait(false));
        return peak;
    }

    /// <summary>
    /// Maps peak 0..1 to two RRGGBB colours (dim teal → bright teal → red clip).
    /// </summary>
    public static (string C1, string C2) PeakToColours(double peak)
    {
        peak = Math.Clamp(peak, 0, 1);
        // perceptual-ish curve
        var level = Math.Pow(peak, 0.6);
        byte r, g, b;
        if (level < 0.85)
        {
            var t = level / 0.85;
            r = (byte)(14 + t * (46 - 14));
            g = (byte)(40 + t * (196 - 40));
            b = (byte)(56 + t * (182 - 56));
        }
        else
        {
            var t = (level - 0.85) / 0.15;
            r = (byte)(46 + t * (232 - 46));
            g = (byte)(196 + t * (93 - 196));
            b = (byte)(182 + t * (76 - 182));
        }

        var c1 = $"{r:X2}{g:X2}{b:X2}";
        var c2 = $"{(byte)(r / 3):X2}{(byte)(g / 3):X2}{(byte)(b / 3):X2}";
        return (c1, c2);
    }

    private static bool ColourChanged(string a, string b)
    {
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase)) return false;
        if (!TryParseRgb(a, out var ar, out var ag, out var ab)) return true;
        if (!TryParseRgb(b, out var br, out var bg, out var bb)) return true;
        return Math.Abs(ar - br) > ColourDelta
               || Math.Abs(ag - bg) > ColourDelta
               || Math.Abs(ab - bb) > ColourDelta;
    }

    private static bool TryParseRgb(string hex, out int r, out int g, out int b)
    {
        r = g = b = 0;
        hex = hex.Trim().TrimStart('#');
        if (hex.Length != 6) return false;
        return int.TryParse(hex[..2], NumberStyles.HexNumber, null, out r)
               && int.TryParse(hex[2..4], NumberStyles.HexNumber, null, out g)
               && int.TryParse(hex[4..6], NumberStyles.HexNumber, null, out b);
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
