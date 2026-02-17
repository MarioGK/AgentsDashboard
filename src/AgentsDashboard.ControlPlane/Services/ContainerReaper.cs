using Docker.DotNet;
using Docker.DotNet.Models;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class ContainerReaper(ILogger<ContainerReaper> logger) : IContainerReaper
{
    private readonly DockerClient _dockerClient = new DockerClientConfiguration().CreateClient();

    public async Task<ContainerKillResult> KillContainerAsync(string runId, string reason, bool force, CancellationToken cancellationToken)
    {
        logger.ZLogWarning("Requesting container kill for run {RunId}. Reason: {Reason}, Force: {Force}", runId, reason, force);

        try
        {
            var containers = await ListRunContainersAsync(cancellationToken);
            var target = containers.FirstOrDefault(x => string.Equals(x.RunId, runId, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                return new ContainerKillResult
                {
                    Killed = false,
                    Error = $"No container found for run {runId}"
                };
            }

            if (force)
            {
                await _dockerClient.Containers.RemoveContainerAsync(target.ContainerId, new ContainerRemoveParameters { Force = true }, cancellationToken);
            }
            else
            {
                await _dockerClient.Containers.StopContainerAsync(target.ContainerId, new ContainerStopParameters(), cancellationToken);
            }

            return new ContainerKillResult
            {
                Killed = true,
                ContainerId = target.ContainerId,
                Error = string.Empty
            };
        }
        catch (DockerContainerNotFoundException)
        {
            return new ContainerKillResult { Killed = false, Error = "Container not found" };
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, "Exception while killing container for run {RunId}", runId);
            return new ContainerKillResult
            {
                Killed = false,
                Error = ex.Message
            };
        }
    }

    public async Task<int> ReapOrphanedContainersAsync(IEnumerable<string> activeRunIds, CancellationToken cancellationToken)
    {
        var active = new HashSet<string>(activeRunIds, StringComparer.OrdinalIgnoreCase);
        logger.ZLogInformation("Scanning for orphaned containers with {Count} active run IDs", active.Count);

        try
        {
            var runContainers = await ListRunContainersAsync(cancellationToken);
            var orphans = runContainers
                .Where(x => !active.Contains(x.RunId))
                .ToList();

            foreach (var orphan in orphans)
            {
                try
                {
                    await _dockerClient.Containers.RemoveContainerAsync(orphan.ContainerId, new ContainerRemoveParameters { Force = true }, cancellationToken);
                    logger.ZLogInformation("Removed orphaned container {ContainerId} for run {RunId}", orphan.ContainerId[..Math.Min(12, orphan.ContainerId.Length)], orphan.RunId);
                }
                catch (DockerContainerNotFoundException)
                {
                }
            }

            return orphans.Count;
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, "Error during container reaping");
            return 0;
        }
    }

    private async Task<List<RunContainer>> ListRunContainersAsync(CancellationToken cancellationToken)
    {
        var containers = await _dockerClient.Containers.ListContainersAsync(new ContainersListParameters { All = true }, cancellationToken);
        var result = new List<RunContainer>();

        foreach (var container in containers)
        {
            if (container.Labels is null)
            {
                continue;
            }

            if (!container.Labels.TryGetValue("orchestrator.run-id", out var runId) || string.IsNullOrWhiteSpace(runId))
            {
                continue;
            }

            result.Add(new RunContainer(container.ID, runId));
        }

        return result;
    }

    private sealed record RunContainer(string ContainerId, string RunId);
}
