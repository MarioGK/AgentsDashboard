using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;

namespace AgentsDashboard.WorkerGateway.Services;

public sealed class DockerContainerService(ILogger<DockerContainerService> logger) : IDisposable
{
    private readonly DockerClient _client = new DockerClientConfiguration().CreateClient();

    public async Task<string> CreateContainerAsync(
        string image,
        IList<string> command,
        IDictionary<string, string> env,
        IDictionary<string, string> labels,
        string? workspaceHostPath,
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
        };

        if (networkDisabled)
        {
            hostConfig.NetworkMode = "none";
        }

        if (readOnlyRootFs)
        {
            hostConfig.Tmpfs = new Dictionary<string, string> { ["/tmp"] = "rw,size=100m" };
        }

        if (!string.IsNullOrWhiteSpace(workspaceHostPath))
        {
            hostConfig.Binds = [$"{workspaceHostPath}:/workspace:rw"];
        }

        var createParams = new CreateContainerParameters
        {
            Image = image,
            Cmd = command.ToList(),
            Env = envList,
            Labels = new Dictionary<string, string>(labels),
            HostConfig = hostConfig,
            WorkingDir = !string.IsNullOrWhiteSpace(workspaceHostPath) ? "/workspace" : null,
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
