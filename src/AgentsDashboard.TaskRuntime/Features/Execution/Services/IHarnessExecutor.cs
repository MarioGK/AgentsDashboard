


namespace AgentsDashboard.TaskRuntime.Features.Execution.Services;

public interface IHarnessExecutor
{
    Task<HarnessResultEnvelope> ExecuteAsync(
        QueuedJob job,
        Func<string, CancellationToken, Task>? onLogChunk,
        CancellationToken cancellationToken);
}
