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
