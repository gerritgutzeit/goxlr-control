using FluentAssertions;
using GoXlrControl.Config;
using GoXlrControl.Engine;

namespace GoXlrControl.Engine.Tests;

public class FaderValueMapperTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(255, 1)]
    [InlineData(128, 128 / 255.0)]
    public void NormalizeRaw_Works(byte raw, double expected)
    {
        FaderValueMapper.NormalizeRaw(raw).Should().BeApproximately(expected, 1e-9);
    }

    [Fact]
    public void ApplyBinding_RespectsInvertAndRange()
    {
        var binding = new FaderBinding { Invert = true, Min = 0.2, Max = 0.8, Curve = "Linear" };
        var result = FaderValueMapper.ApplyBinding(0.0, binding);
        result.Should().BeApproximately(0.8, 1e-9);
    }
}

public class SoftTakeoverTests
{
    [Fact]
    public void Absolute_AlwaysApplies()
    {
        var t = new SoftTakeoverTracker();
        t.ShouldApply(0.9, 0.1, SyncMode.Absolute, 0.02).Should().BeTrue();
    }

    [Fact]
    public void Soft_EngagesWhenCrossing()
    {
        var t = new SoftTakeoverTracker();
        t.ShouldApply(0.1, 0.5, SyncMode.SoftTakeover, 0.02).Should().BeFalse();
        t.ShouldApply(0.6, 0.5, SyncMode.SoftTakeover, 0.02).Should().BeTrue();
        t.Engaged.Should().BeTrue();
    }

    [Fact]
    public void Soft_EngagesInsideDeadZone()
    {
        var t = new SoftTakeoverTracker();
        t.ShouldApply(0.51, 0.5, SyncMode.SoftTakeover, 0.02).Should().BeTrue();
    }
}

public class ButtonDebouncerTests
{
    [Fact]
    public void RejectsBounceWithinWindow()
    {
        var d = new ButtonDebouncer(TimeSpan.FromMilliseconds(40));
        var t0 = DateTimeOffset.UtcNow;
        d.TryUpdate(Hardware.Abstractions.HardwareButtonId.Bleep, true, t0, out _).Should().BeTrue();
        d.TryUpdate(Hardware.Abstractions.HardwareButtonId.Bleep, false, t0.AddMilliseconds(10), out _).Should().BeFalse();
        d.TryUpdate(Hardware.Abstractions.HardwareButtonId.Bleep, false, t0.AddMilliseconds(50), out _).Should().BeTrue();
    }
}
