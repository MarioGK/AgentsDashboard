using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.TaskRuntimeGateway.Models;

namespace AgentsDashboard.TaskRuntimeGateway.Services;

public interface IHarnessExecutor
{
    Task<HarnessResultEnvelope> ExecuteAsync(
        QueuedJob job,
        Func<string, CancellationToken, Task>? onLogChunk,
        CancellationToken cancellationToken);
}
