using FluentAssertions;
using GoXlrControl.Abstractions;
using GoXlrControl.Config;
using GoXlrControl.Engine;
using GoXlrControl.Hardware.Abstractions;

namespace GoXlrControl.Engine.Tests;

public class ControllerEngineIntegrationTests
{
    [Fact]
    public async Task SoftTakeover_DoesNotWriteUntilCrossed()
    {
        var hardware = new FakeHardware();
        var volume = new RecordingVolumeSink { MasterVolume = 0.8 };
        var engine = CreateEngine(hardware, volume, SyncMode.SoftTakeover);
        engine.Start();

        hardware.RaiseFader(FaderId.A, 0.1, isInitial: true);
        hardware.RaiseFader(FaderId.A, 0.2);
        await Task.Delay(40);

        volume.MasterWrites.Should().BeEmpty();
        engine.HasSoftTakeoverPending(FaderId.A).Should().BeTrue();

        hardware.RaiseFader(FaderId.A, 0.85);
        await Task.Delay(40);

        volume.MasterWrites.Should().ContainSingle().Which.Should().BeApproximately(0.85, 0.01);
        await engine.DisposeAsync();
    }

    [Fact]
    public async Task Absolute_WritesAfterCoalesce_LatestWins()
    {
        var hardware = new FakeHardware();
        var volume = new RecordingVolumeSink { MasterVolume = 0.5 };
        var engine = CreateEngine(hardware, volume, SyncMode.Absolute);
        engine.Start();

        hardware.RaiseFader(FaderId.A, 0.1, isInitial: true);
        hardware.RaiseFader(FaderId.A, 0.3);
        hardware.RaiseFader(FaderId.A, 0.6);
        hardware.RaiseFader(FaderId.A, 0.9);
        await Task.Delay(40);

        volume.MasterWrites.Should().ContainSingle().Which.Should().BeApproximately(0.9, 0.01);
        await engine.DisposeAsync();
    }

    [Fact]
    public async Task Pause_SuppressesWrites()
    {
        var hardware = new FakeHardware();
        var volume = new RecordingVolumeSink { MasterVolume = 0.5 };
        var engine = CreateEngine(hardware, volume, SyncMode.Absolute);
        engine.Start();
        engine.IsPaused = true;

        hardware.RaiseFader(FaderId.A, 0.1, isInitial: true);
        hardware.RaiseFader(FaderId.A, 0.9);
        await Task.Delay(40);

        volume.MasterWrites.Should().BeEmpty();
        await engine.DisposeAsync();
    }

    [Fact]
    public async Task ConnectionConnected_ResetsSoftTakeover()
    {
        var hardware = new FakeHardware();
        var volume = new RecordingVolumeSink { MasterVolume = 0.8 };
        var engine = CreateEngine(hardware, volume, SyncMode.SoftTakeover);
        engine.Start();

        hardware.RaiseFader(FaderId.A, 0.1, isInitial: true);
        hardware.RaiseFader(FaderId.A, 0.2);
        await Task.Delay(40);
        hardware.RaiseFader(FaderId.A, 0.85);
        await Task.Delay(40);
        volume.MasterWrites.Should().NotBeEmpty();
        engine.IsSoftTakeoverEngaged(FaderId.A).Should().BeTrue();

        hardware.RaiseConnection(HardwareConnectionState.Connected);
        engine.IsSoftTakeoverEngaged(FaderId.A).Should().BeFalse();

        volume.MasterWrites.Clear();
        hardware.RaiseFader(FaderId.A, 0.2);
        await Task.Delay(40);
        volume.MasterWrites.Should().BeEmpty("soft-takeover should block after reconnect reset");
        await engine.DisposeAsync();
    }

    private static ControllerEngine CreateEngine(
        FakeHardware hardware, RecordingVolumeSink volume, SyncMode syncMode)
    {
        var profile = ControllerProfile.CreateDefault("EngineTest");
        profile.Faders[0].SyncMode = syncMode;
        profile.Faders[0].Target = new FaderTarget { Kind = FaderTargetKind.MasterVolume };
        profile.Faders[0].DeadZone = 0.02;

        var engine = new ControllerEngine(hardware, volume, new ActionDispatcher(Array.Empty<IActionExecutor>()));
        engine.SetProfile(profile);
        return engine;
    }

    private sealed class FakeHardware : IHardwareInputProvider
    {
        public HardwareConnectionState ConnectionState { get; private set; } = HardwareConnectionState.Disconnected;
        public IReadOnlyList<HardwareDeviceInfo> Devices { get; } = Array.Empty<HardwareDeviceInfo>();
        public event EventHandler<HardwareConnectionState>? ConnectionChanged;
        public event EventHandler<FaderValueChanged>? FaderChanged;
#pragma warning disable CS0067 // Required by interface; unused in these tests
        public event EventHandler<ButtonStateChanged>? ButtonChanged;
        public event EventHandler<string>? DiagnosticMessage;
#pragma warning restore CS0067

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void RaiseConnection(HardwareConnectionState state)
        {
            ConnectionState = state;
            ConnectionChanged?.Invoke(this, state);
        }

        public void RaiseFader(FaderId fader, double normalized, bool isInitial = false)
        {
            FaderChanged?.Invoke(this, new FaderValueChanged(
                "TEST", fader, "System", normalized, (byte)(normalized * 255),
                DateTimeOffset.UtcNow, isInitial));
        }
    }

    private sealed class RecordingVolumeSink : IVolumeSink
    {
        public double MasterVolume { get; set; } = 0.5;
        public List<double> MasterWrites { get; } = new();

        public Task SetMasterVolumeAsync(double normalized, CancellationToken ct = default)
        {
            MasterWrites.Add(normalized);
            MasterVolume = normalized;
            return Task.CompletedTask;
        }

        public Task ToggleMasterMuteAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task SetEndpointVolumeAsync(string deviceId, double normalized, CancellationToken ct = default) => Task.CompletedTask;
        public Task ToggleEndpointMuteAsync(string deviceId, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetApplicationVolumeAsync(AppIdentity identity, double normalized, bool aggregate, CancellationToken ct = default) => Task.CompletedTask;
        public Task ToggleApplicationMuteAsync(AppIdentity identity, CancellationToken ct = default) => Task.CompletedTask;
        public Task<double> GetMasterVolumeAsync(CancellationToken ct = default) => Task.FromResult(MasterVolume);
        public Task<double?> GetApplicationVolumeAsync(AppIdentity identity, CancellationToken ct = default) => Task.FromResult<double?>(0.5);
        public Task<bool> GetMasterMuteAsync(CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool?> GetApplicationMuteAsync(AppIdentity identity, CancellationToken ct = default) => Task.FromResult<bool?>(false);
        public Task<double> GetMasterPeakAsync(CancellationToken ct = default) => Task.FromResult(0.0);
        public Task<double> GetApplicationPeakAsync(AppIdentity identity, CancellationToken ct = default) => Task.FromResult(0.0);
    }
}
