using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.WorkerGateway.Models;

namespace AgentsDashboard.WorkerGateway.Services;

public interface IHarnessExecutor
{
    Task<HarnessResultEnvelope> ExecuteAsync(
        QueuedJob job,
        Func<string, CancellationToken, Task>? onLogChunk,
        CancellationToken cancellationToken);
}
