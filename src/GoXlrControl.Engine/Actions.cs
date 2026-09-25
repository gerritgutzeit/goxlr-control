using GoXlrControl.Config;

namespace GoXlrControl.Engine;

public interface IActionExecutor
{
    ActionType ActionType { get; }
    Task ExecuteAsync(ActionRef action, CancellationToken cancellationToken = default);
}

public sealed class ActionDispatcher
{
    private readonly Dictionary<ActionType, IActionExecutor> _executors;

    public ActionDispatcher(IEnumerable<IActionExecutor> executors)
    {
        _executors = executors.ToDictionary(e => e.ActionType);
    }

    public async Task DispatchAsync(ActionRef? action, CancellationToken ct = default)
    {
        if (action is null || action.Type == ActionType.None)
            return;

        if (!_executors.TryGetValue(action.Type, out var executor))
            throw new InvalidOperationException($"Kein Executor für Action {action.Type}.");

        await executor.ExecuteAsync(action, ct).ConfigureAwait(false);
    }
}

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
