using AgentsDashboard.TaskRuntimeGateway.Models;

namespace AgentsDashboard.TaskRuntimeGateway.Services;

public interface IDockerContainerService
{
    Task<string> CreateContainerAsync(
        string image,
        IList<string> command,
        IDictionary<string, string> env,
        IDictionary<string, string> labels,
        string? workspaceHostPath,
        string? artifactsHostPath,
        double cpuLimit,
        string memoryLimit,
        bool networkDisabled,
        bool readOnlyRootFs,
        CancellationToken cancellationToken);

    Task<bool> StartAsync(string containerId, CancellationToken cancellationToken);
    Task<long> WaitForExitAsync(string containerId, CancellationToken cancellationToken);
    Task<string> GetLogsAsync(string containerId, CancellationToken cancellationToken);
    Task StreamLogsAsync(string containerId, Func<string, CancellationToken, Task> onLogChunk, CancellationToken cancellationToken);
    Task RemoveAsync(string containerId, CancellationToken cancellationToken);
    Task<List<OrchestratorContainerInfo>> ListOrchestratorContainersAsync(CancellationToken cancellationToken);
    Task<bool> RemoveContainerForceAsync(string containerId, CancellationToken cancellationToken);
    Task<Contracts.Domain.ContainerMetrics?> GetContainerStatsAsync(string containerId, CancellationToken cancellationToken);
}
