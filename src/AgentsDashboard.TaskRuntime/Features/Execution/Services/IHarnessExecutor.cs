using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.TaskRuntime.Models;

namespace AgentsDashboard.TaskRuntime.Services;

public interface IHarnessExecutor
{
    Task<HarnessResultEnvelope> ExecuteAsync(
        QueuedJob job,
        Func<string, CancellationToken, Task>? onLogChunk,
        CancellationToken cancellationToken);
}
