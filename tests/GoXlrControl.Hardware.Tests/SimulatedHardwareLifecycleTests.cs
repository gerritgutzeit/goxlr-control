using FluentAssertions;
using GoXlrControl.Hardware.Abstractions;

namespace GoXlrControl.Hardware.Tests;

public class SimulatedHardwareLifecycleTests
{
    [Fact]
    public async Task Start_Stop_Start_EmitsConnectOncePerStart()
    {
        await using var sut = new SimulatedHardwareProvider();
        var connects = 0;
        sut.ConnectionChanged += (_, s) =>
        {
            if (s == HardwareConnectionState.Connected)
                Interlocked.Increment(ref connects);
        };

        await sut.StartAsync();
        await sut.StopAsync();
        await sut.StartAsync();

        connects.Should().Be(2);
        sut.ConnectionState.Should().Be(HardwareConnectionState.Connected);
    }

    [Fact]
    public async Task DoubleStart_DoesNotEmitSecondConnect()
    {
        await using var sut = new SimulatedHardwareProvider();
        var connects = 0;
        sut.ConnectionChanged += (_, s) =>
        {
            if (s == HardwareConnectionState.Connected)
                Interlocked.Increment(ref connects);
        };

        await sut.StartAsync();
        await sut.StartAsync();

        connects.Should().Be(1);
    }

    [Fact]
    public async Task Stop_WhileStopped_IsIdempotent()
    {
        await using var sut = new SimulatedHardwareProvider();
        await sut.StopAsync();
        await sut.StopAsync();
        sut.ConnectionState.Should().Be(HardwareConnectionState.Disconnected);
    }

    [Fact]
    public async Task Stop_ReleasesHeldButtons()
    {
        await using var sut = new SimulatedHardwareProvider();
        await sut.StartAsync();

        ButtonStateChanged? last = null;
        sut.ButtonChanged += (_, e) => last = e;
        sut.SetButton(HardwareButtonId.Cough, true);
        last.Should().NotBeNull();
        last!.IsPressed.Should().BeTrue();

        await sut.StopAsync();

        last.IsPressed.Should().BeFalse();
        last.Button.Should().Be(HardwareButtonId.Cough);
        sut.ConnectionState.Should().Be(HardwareConnectionState.Disconnected);
    }
}
