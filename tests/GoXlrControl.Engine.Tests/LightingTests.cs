using FluentAssertions;
using GoXlrControl.Engine;
using GoXlrControl.Hardware;
using GoXlrControl.Hardware.Abstractions;

namespace GoXlrControl.Engine.Tests;

public class LightingFeedbackTests
{
    [Fact]
    public void PeakToColours_Silent_IsDim()
    {
        var (c1, c2) = LightingFeedbackService.PeakToColours(0);
        c1.Should().Be("0E2838");
        c2.Length.Should().Be(6);
    }

    [Fact]
    public void PeakToColours_Hot_MovesTowardRed()
    {
        var (low, _) = LightingFeedbackService.PeakToColours(0.2);
        var (high, _) = LightingFeedbackService.PeakToColours(1.0);
        int.Parse(low[..2], System.Globalization.NumberStyles.HexNumber)
            .Should().BeLessThan(int.Parse(high[..2], System.Globalization.NumberStyles.HexNumber));
    }
}

public class GoXlrCommandBuilderTests
{
    [Fact]
    public void SetFaderColours_JsonShape()
    {
        var node = GoXlrCommandBuilder.SetFaderColours("SER", FaderId.A, "#2ec4b6", "0f3d38");
        var json = node.ToJsonString();
        json.Should().Contain("SetFaderColours");
        json.Should().Contain("2EC4B6");
        json.Should().Contain("\"A\"");
        json.Should().Contain("SER");
    }

    [Fact]
    public void SetButtonOffStyle_And_AnimationMode_JsonShape()
    {
        var off = GoXlrCommandBuilder.SetButtonOffStyle("SER", HardwareButtonId.Cough, LightingOffStyle.Colour2);
        off.ToJsonString().Should().Contain("SetButtonOffStyle").And.Contain("Colour2").And.Contain("Cough");

        var anim = GoXlrCommandBuilder.SetAnimationMode("SER", AnimationMode.None);
        anim.ToJsonString().Should().Contain("SetAnimationMode").And.Contain("None");
    }

    [Fact]
    public void NormalizeHex_RejectsInvalid()
    {
        var act = () => GoXlrCommandBuilder.NormalizeHex("xyz");
        act.Should().Throw<ArgumentException>();
    }
}
