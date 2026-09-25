using GoXlrControl.Config;
using GoXlrControl.Hardware.Abstractions;

namespace GoXlrControl.Engine;

public static class FaderValueMapper
{
    public static double NormalizeRaw(byte raw) => raw / 255.0;

    public static double ApplyBinding(double normalizedHardware, FaderBinding binding)
    {
        var v = Math.Clamp(normalizedHardware, 0, 1);
        if (binding.Invert)
            v = 1.0 - v;

        v = ApplyCurve(v, binding.Curve);
        var min = Math.Clamp(binding.Min, 0, 1);
        var max = Math.Clamp(binding.Max, 0, 1);
        if (max < min) (min, max) = (max, min);
        return min + v * (max - min);
    }

    public static double ApplyCurve(double v, string curve) =>
        curve.ToLowerInvariant() switch
        {
            "logarithmic" or "log" => Math.Pow(v, 2.0),
            "s-curve" or "scurve" => v * v * (3 - 2 * v),
            _ => v
        };
}

public sealed class SoftTakeoverTracker
{
    private bool _engaged;
    private double _lastHardware = double.NaN;

    public bool Engaged => _engaged;

    public void Reset()
    {
        _engaged = false;
        _lastHardware = double.NaN;
    }

    /// <summary>
    /// Returns true when the hardware value should be applied to the target.
    /// </summary>
    public bool ShouldApply(double hardware, double currentTarget, SyncMode mode, double deadZone)
    {
        if (mode == SyncMode.Absolute)
        {
            _engaged = true;
            _lastHardware = hardware;
            return true;
        }

        if (_engaged)
        {
            _lastHardware = hardware;
            return true;
        }

        if (Math.Abs(hardware - currentTarget) <= deadZone)
        {
            _engaged = true;
            _lastHardware = hardware;
            return true;
        }

        if (!double.IsNaN(_lastHardware))
        {
            // crossed the target between samples
            var crossed = (_lastHardware - currentTarget) * (hardware - currentTarget) <= 0;
            if (crossed)
            {
                _engaged = true;
                _lastHardware = hardware;
                return true;
            }
        }

        _lastHardware = hardware;
        return false;
    }
}

public sealed class ButtonDebouncer
{
    private readonly Dictionary<HardwareButtonId, (bool Pressed, DateTimeOffset ChangedAt)> _state = new();
    private readonly TimeSpan _debounce;

    public ButtonDebouncer(TimeSpan? debounce = null)
    {
        _debounce = debounce ?? TimeSpan.FromMilliseconds(40);
    }

    public bool TryUpdate(HardwareButtonId button, bool pressed, DateTimeOffset timestamp, out bool acceptedPressed)
    {
        acceptedPressed = pressed;
        if (_state.TryGetValue(button, out var prev))
        {
            if (prev.Pressed == pressed)
                return false;
            if (timestamp - prev.ChangedAt < _debounce)
                return false;
        }

        _state[button] = (pressed, timestamp);
        return true;
    }
}
