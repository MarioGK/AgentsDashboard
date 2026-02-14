using AgentsDashboard.ControlPlane.Configuration;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class DockerWorkerLifecycleManager(
    IOptions<OrchestratorOptions> options,
    ILogger<DockerWorkerLifecycleManager> logger) : IWorkerLifecycleManager
{
    private readonly OrchestratorOptions _options = options.Value;
    private readonly DockerClient _dockerClient = new DockerClientConfiguration().CreateClient();
    private DateTime _lastActivityUtc = DateTime.UtcNow;

    public async Task<bool> EnsureWorkerRunningAsync(CancellationToken cancellationToken)
    {
        try
        {
            var container = await FindWorkerContainerAsync(cancellationToken);
            if (container is not null)
            {
                if (string.Equals(container.State, "running", StringComparison.OrdinalIgnoreCase))
                {
                    _lastActivityUtc = DateTime.UtcNow;
                    return true;
                }

                await _dockerClient.Containers.StartContainerAsync(container.ID, new ContainerStartParameters(), cancellationToken);
                _lastActivityUtc = DateTime.UtcNow;
                return true;
            }

            var createResponse = await _dockerClient.Containers.CreateContainerAsync(
                new CreateContainerParameters
                {
                    Image = _options.WorkerContainerImage,
                    Name = _options.WorkerContainerName,
                    Env =
                    [
                        "Worker__UseDocker=true",
                        $"Worker__ControlPlaneUrl={ResolveControlPlaneUrl()}",
                        $"Worker__DefaultImage={Environment.GetEnvironmentVariable("WORKER_DEFAULT_IMAGE") ?? "ghcr.io/mariogk/ai-harness:latest"}",
                        $"CODEX_API_KEY={Environment.GetEnvironmentVariable("CODEX_API_KEY") ?? string.Empty}",
                        $"OPENCODE_API_KEY={Environment.GetEnvironmentVariable("OPENCODE_API_KEY") ?? string.Empty}",
                        $"ANTHROPIC_API_KEY={Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? string.Empty}",
                        $"Z_AI_API_KEY={Environment.GetEnvironmentVariable("Z_AI_API_KEY") ?? string.Empty}",
                    ],
                    HostConfig = new HostConfig
                    {
                        Binds =
                        [
                            "/var/run/docker.sock:/var/run/docker.sock",
                            "artifacts-data:/artifacts",
                            "workspaces-data:/workspaces"
                        ],
                        RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.No }
                    },
                    NetworkingConfig = new NetworkingConfig
                    {
                        EndpointsConfig = new Dictionary<string, EndpointSettings>
                        {
                            [_options.WorkerDockerNetwork] = new()
                        }
                    }
                },
                cancellationToken);

            await _dockerClient.Containers.StartContainerAsync(createResponse.ID, new ContainerStartParameters(), cancellationToken);
            _lastActivityUtc = DateTime.UtcNow;
            logger.LogInformation("Started on-demand worker container {ContainerName}", _options.WorkerContainerName);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to ensure worker container is running");
            return false;
        }
    }

    public Task RecordDispatchActivityAsync(CancellationToken cancellationToken)
    {
        _lastActivityUtc = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    public async Task StopWorkerIfIdleAsync(CancellationToken cancellationToken)
    {
        var idleFor = DateTime.UtcNow - _lastActivityUtc;
        if (idleFor < TimeSpan.FromMinutes(_options.WorkerIdleTimeoutMinutes))
            return;

        var container = await FindWorkerContainerAsync(cancellationToken);
        if (container is null || !string.Equals(container.State, "running", StringComparison.OrdinalIgnoreCase))
            return;

        await _dockerClient.Containers.StopContainerAsync(container.ID, new ContainerStopParameters(), cancellationToken);
        logger.LogInformation("Stopped idle worker container {ContainerName} after {IdleMinutes} minutes",
            _options.WorkerContainerName, _options.WorkerIdleTimeoutMinutes);
    }

    private async Task<ContainerListResponse?> FindWorkerContainerAsync(CancellationToken cancellationToken)
    {
        var containers = await _dockerClient.Containers.ListContainersAsync(
            new ContainersListParameters { All = true },
            cancellationToken);

        return containers.FirstOrDefault(c =>
            c.Names.Any(n => string.Equals(n.Trim('/'), _options.WorkerContainerName, StringComparison.OrdinalIgnoreCase)));
    }

    private string ResolveControlPlaneUrl()
    {
        var inDockerUrl = Environment.GetEnvironmentVariable("WORKER_CONTROL_PLANE_URL");
        if (!string.IsNullOrWhiteSpace(inDockerUrl))
            return inDockerUrl;

        return "http://control-plane:8080";
    }
}
