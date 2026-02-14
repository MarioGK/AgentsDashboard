using System.Text;
using AgentsDashboard.WorkerGateway.Models;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;

namespace AgentsDashboard.WorkerGateway.Services;

public sealed class DockerContainerService(ILogger<DockerContainerService> logger) : IDockerContainerService, IDisposable
{
    private readonly DockerClient _client = new DockerClientConfiguration().CreateClient();

    public async Task<string> CreateContainerAsync(
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
        CancellationToken cancellationToken)
    {
        var envList = env.Select(kv => $"{kv.Key}={kv.Value}").ToList();

        var hostConfig = new HostConfig
        {
            AutoRemove = true,
            NanoCPUs = (long)(cpuLimit * 1_000_000_000),
            Memory = ParseMemoryLimit(memoryLimit),
            ReadonlyRootfs = readOnlyRootFs,
            SecurityOpt = ["no-new-privileges"],
            CapDrop = new List<string> { "ALL" },
        };

        if (networkDisabled)
        {
            hostConfig.NetworkMode = "none";
        }

        if (readOnlyRootFs)
        {
            hostConfig.Tmpfs = new Dictionary<string, string> { ["/tmp"] = "rw,size=100m", ["/var/tmp"] = "rw,size=50m" };
        }

        var binds = new List<string>();
        if (!string.IsNullOrWhiteSpace(workspaceHostPath))
        {
            binds.Add($"{workspaceHostPath}:/workspace:rw");
        }
        if (!string.IsNullOrWhiteSpace(artifactsHostPath))
        {
            Directory.CreateDirectory(artifactsHostPath);
            binds.Add($"{artifactsHostPath}:/artifacts:rw");
        }
        if (binds.Count > 0)
        {
            hostConfig.Binds = binds;
        }

        var createParams = new CreateContainerParameters
        {
            Image = image,
            Cmd = command.ToList(),
            Env = envList,
            Labels = new Dictionary<string, string>(labels),
            HostConfig = hostConfig,
            WorkingDir = !string.IsNullOrWhiteSpace(workspaceHostPath) ? "/workspace" : null,
            User = "agent",
        };

        var response = await _client.Containers.CreateContainerAsync(createParams, cancellationToken);
        logger.LogInformation("Created container {ContainerId} from image {Image}", response.ID[..12], image);
        return response.ID;
    }

    public async Task<bool> StartAsync(string containerId, CancellationToken cancellationToken)
    {
        return await _client.Containers.StartContainerAsync(containerId, new ContainerStartParameters(), cancellationToken);
    }

    public async Task<long> WaitForExitAsync(string containerId, CancellationToken cancellationToken)
    {
        var response = await _client.Containers.WaitContainerAsync(containerId, cancellationToken);
        return response.StatusCode;
    }

    public async Task<string> GetLogsAsync(string containerId, CancellationToken cancellationToken)
    {
        var stream = await _client.Containers.GetContainerLogsAsync(
            containerId,
            false,
            new ContainerLogsParameters { ShowStdout = true, ShowStderr = true },
            cancellationToken);

        var output = new StringBuilder();
        var buffer = new byte[4096];

        while (true)
        {
            var result = await stream.ReadOutputAsync(buffer, 0, buffer.Length, cancellationToken);
            if (result.Count == 0) break;
            output.Append(System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count));
        }

        return output.ToString();
    }

    public async Task StreamLogsAsync(
        string containerId,
        Func<string, CancellationToken, Task> onLogChunk,
        CancellationToken cancellationToken)
    {
        var stream = await _client.Containers.GetContainerLogsAsync(
            containerId,
            true,
            new ContainerLogsParameters
            {
                ShowStdout = true,
                ShowStderr = true,
                Follow = true,
                Tail = "0",
            },
            cancellationToken);

        var buffer = new byte[4096];
        var accumulated = new StringBuilder();

        while (!cancellationToken.IsCancellationRequested)
        {
            int bytesRead;
            try
            {
                var result = await stream.ReadOutputAsync(buffer, 0, buffer.Length, cancellationToken);
                bytesRead = result.Count;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (ex is TimeoutException or IOException)
            {
                logger.LogDebug(ex, "Log stream ended for container {ContainerId}", containerId[..12]);
                break;
            }

            if (bytesRead == 0)
            {
                if (accumulated.Length > 0)
                {
                    await onLogChunk(accumulated.ToString(), cancellationToken);
                    accumulated.Clear();
                }
                break;
            }

            accumulated.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));

            if (accumulated.Length >= 4096)
            {
                await onLogChunk(accumulated.ToString(), cancellationToken);
                accumulated.Clear();
            }
        }

        if (accumulated.Length > 0)
        {
            try
            {
                await onLogChunk(accumulated.ToString(), CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    public async Task RemoveAsync(string containerId, CancellationToken cancellationToken)
    {
        try
        {
            await _client.Containers.RemoveContainerAsync(
                containerId,
                new ContainerRemoveParameters { Force = true },
                cancellationToken);
        }
        catch (DockerContainerNotFoundException)
        {
            // Already removed (auto-remove)
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to remove container {ContainerId}", containerId[..12]);
        }
    }

    public async Task<bool> VerifyContainerLabelsAsync(string containerId, string expectedRunId, CancellationToken cancellationToken)
    {
        try
        {
            var inspect = await _client.Containers.InspectContainerAsync(containerId, cancellationToken);
            return inspect.Config.Labels.TryGetValue("orchestrator.run-id", out var runId) && runId == expectedRunId;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<OrchestratorContainerInfo>> ListOrchestratorContainersAsync(CancellationToken cancellationToken)
    {
        var containers = await _client.Containers.ListContainersAsync(
            new ContainersListParameters { All = true },
            cancellationToken);

        var result = new List<OrchestratorContainerInfo>();

        foreach (var container in containers)
        {
            if (container.Labels is null)
                continue;

            if (!container.Labels.TryGetValue("orchestrator.run-id", out var runId))
                continue;

            container.Labels.TryGetValue("orchestrator.task-id", out var taskId);
            container.Labels.TryGetValue("orchestrator.repo-id", out var repoId);
            container.Labels.TryGetValue("orchestrator.project-id", out var projectId);

            result.Add(new OrchestratorContainerInfo
            {
                ContainerId = container.ID,
                RunId = runId,
                TaskId = taskId ?? string.Empty,
                RepoId = repoId ?? string.Empty,
                ProjectId = projectId ?? string.Empty,
                State = container.State,
                Image = container.Image,
                CreatedAt = container.Created
            });
        }

        return result;
    }

    public async Task<bool> RemoveContainerForceAsync(string containerId, CancellationToken cancellationToken)
    {
        try
        {
            await _client.Containers.RemoveContainerAsync(
                containerId,
                new ContainerRemoveParameters { Force = true },
                cancellationToken);
            logger.LogInformation("Force removed orphaned container {ContainerId}", containerId[..Math.Min(12, containerId.Length)]);
            return true;
        }
        catch (DockerContainerNotFoundException)
        {
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to force remove container {ContainerId}", containerId[..Math.Min(12, containerId.Length)]);
            return false;
        }
    }

    public async Task<ContainerKillResult> KillContainerByRunIdAsync(string runId, string reason, bool force, CancellationToken cancellationToken)
    {
        try
        {
            var containers = await ListOrchestratorContainersAsync(cancellationToken);
            var container = containers.FirstOrDefault(c => 
                string.Equals(c.RunId, runId, StringComparison.OrdinalIgnoreCase));

            if (container is null)
            {
                logger.LogWarning("No container found for run {RunId}", runId);
                return new ContainerKillResult(false, string.Empty, $"No container found for run {runId}");
            }

            var containerId = container.ContainerId;
            logger.LogWarning("Killing container {ContainerId} for run {RunId}. Reason: {Reason}, Force: {Force}",
                containerId[..Math.Min(12, containerId.Length)], runId, reason, force);

            if (force)
            {
                var removed = await RemoveContainerForceAsync(containerId, cancellationToken);
                return new ContainerKillResult(removed, containerId, removed ? string.Empty : "Failed to force remove container");
            }

            try
            {
                await _client.Containers.StopContainerAsync(
                    containerId,
                    new ContainerStopParameters { WaitBeforeKillSeconds = 5 },
                    cancellationToken);

                logger.LogInformation("Stopped container {ContainerId} for run {RunId}", containerId[..12], runId);
                return new ContainerKillResult(true, containerId, string.Empty);
            }
            catch (DockerContainerNotFoundException)
            {
                return new ContainerKillResult(false, string.Empty, "Container not found");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error killing container for run {RunId}", runId);
            return new ContainerKillResult(false, string.Empty, ex.Message);
        }
    }

    private static long ParseMemoryLimit(string memoryLimit)
    {
        var trimmed = memoryLimit.Trim().ToLowerInvariant();
        if (trimmed.EndsWith('g'))
            return (long)(double.Parse(trimmed[..^1]) * 1024 * 1024 * 1024);
        if (trimmed.EndsWith('m'))
            return (long)(double.Parse(trimmed[..^1]) * 1024 * 1024);
        return long.TryParse(trimmed, out var bytes) ? bytes : 2L * 1024 * 1024 * 1024;
    }

    public void Dispose() => _client.Dispose();
}
