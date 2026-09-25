using GoXlrControl.Config;

namespace GoXlrControl.Abstractions;

/// <summary>
/// Port for Windows audio volume/mute/peak control.
/// Implemented by Audio; consumed by Engine and Integrations.
/// </summary>
public interface IVolumeSink
{
    Task SetMasterVolumeAsync(double normalized, CancellationToken ct = default);
    Task ToggleMasterMuteAsync(CancellationToken ct = default);
    Task SetEndpointVolumeAsync(string deviceId, double normalized, CancellationToken ct = default);
    Task ToggleEndpointMuteAsync(string deviceId, CancellationToken ct = default);
    Task SetApplicationVolumeAsync(AppIdentity identity, double normalized, bool aggregate, CancellationToken ct = default);
    Task ToggleApplicationMuteAsync(AppIdentity identity, CancellationToken ct = default);
    Task<double> GetMasterVolumeAsync(CancellationToken ct = default);
    Task<double?> GetApplicationVolumeAsync(AppIdentity identity, CancellationToken ct = default);
    Task<bool> GetMasterMuteAsync(CancellationToken ct = default);
    Task<bool?> GetApplicationMuteAsync(AppIdentity identity, CancellationToken ct = default);
    Task<double> GetMasterPeakAsync(CancellationToken ct = default);
    Task<double> GetApplicationPeakAsync(AppIdentity identity, CancellationToken ct = default);
}

public sealed class NullVolumeSink : IVolumeSink
{
    public Task SetMasterVolumeAsync(double normalized, CancellationToken ct = default) => Task.CompletedTask;
    public Task ToggleMasterMuteAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task SetEndpointVolumeAsync(string deviceId, double normalized, CancellationToken ct = default) => Task.CompletedTask;
    public Task ToggleEndpointMuteAsync(string deviceId, CancellationToken ct = default) => Task.CompletedTask;
    public Task SetApplicationVolumeAsync(AppIdentity identity, double normalized, bool aggregate, CancellationToken ct = default) => Task.CompletedTask;
    public Task ToggleApplicationMuteAsync(AppIdentity identity, CancellationToken ct = default) => Task.CompletedTask;
    public Task<double> GetMasterVolumeAsync(CancellationToken ct = default) => Task.FromResult(0.5);
    public Task<double?> GetApplicationVolumeAsync(AppIdentity identity, CancellationToken ct = default) => Task.FromResult<double?>(0.5);
    public Task<bool> GetMasterMuteAsync(CancellationToken ct = default) => Task.FromResult(false);
    public Task<bool?> GetApplicationMuteAsync(AppIdentity identity, CancellationToken ct = default) => Task.FromResult<bool?>(false);
    public Task<double> GetMasterPeakAsync(CancellationToken ct = default) => Task.FromResult(0.0);
    public Task<double> GetApplicationPeakAsync(AppIdentity identity, CancellationToken ct = default) => Task.FromResult(0.0);
}
