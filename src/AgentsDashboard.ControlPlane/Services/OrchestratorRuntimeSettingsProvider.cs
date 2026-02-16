using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.ControlPlane.Services;

public interface IOrchestratorRuntimeSettingsProvider
{
    Task<OrchestratorRuntimeSettings> GetAsync(CancellationToken cancellationToken);
    void Invalidate();
}

public sealed record OrchestratorRuntimeSettings(
    int MinWorkers,
    int MaxWorkers,
    int MaxProcessesPerWorker,
    int ReserveWorkers,
    int MaxQueueDepth,
    int QueueWaitTimeoutSeconds,
    WorkerImagePolicy WorkerImagePolicy,
    string ContainerImage,
    string ContainerNamePrefix,
    string DockerNetwork,
    WorkerConnectivityMode ConnectivityMode,
    string WorkerImageRegistry,
    string WorkerCanaryImage,
    string WorkerDockerBuildContextPath,
    string WorkerDockerfilePath,
    int MaxConcurrentPulls,
    int MaxConcurrentBuilds,
    int ImagePullTimeoutSeconds,
    int ImageBuildTimeoutSeconds,
    int WorkerImageCacheTtlMinutes,
    int ImageFailureCooldownMinutes,
    int CanaryPercent,
    int MaxWorkerStartAttemptsPer10Min,
    int MaxFailedStartsPer10Min,
    int CooldownMinutes,
    int ContainerStartTimeoutSeconds,
    int ContainerStopTimeoutSeconds,
    int HealthProbeIntervalSeconds,
    int ContainerRestartLimit,
    ContainerUnhealthyAction ContainerUnhealthyAction,
    int OrchestratorErrorBurstThreshold,
    int OrchestratorErrorCoolDownMinutes,
    bool EnableDraining,
    int DrainTimeoutSeconds,
    bool EnableAutoRecycle,
    int RecycleAfterRuns,
    int RecycleAfterUptimeMinutes,
    bool EnableContainerAutoCleanup,
    string WorkerCpuLimit,
    int WorkerMemoryLimitMb,
    int WorkerPidsLimit,
    int WorkerFileDescriptorLimit,
    int RunHardTimeoutSeconds,
    int MaxRunLogMb,
    bool EnablePressureScaling,
    int CpuScaleOutThresholdPercent,
    int MemoryScaleOutThresholdPercent,
    int PressureSampleWindowSeconds);

public sealed class OrchestratorRuntimeSettingsProvider(
    IOrchestratorStore store,
    IOptions<OrchestratorOptions> options) : IOrchestratorRuntimeSettingsProvider
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(10);

    private readonly object _cacheLock = new();
    private OrchestratorRuntimeSettings? _cached;
    private DateTime _cachedUntilUtc = DateTime.MinValue;

    public async Task<OrchestratorRuntimeSettings> GetAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        lock (_cacheLock)
        {
            if (_cached is not null && _cachedUntilUtc > now)
            {
                return _cached;
            }
        }

        var saved = await store.GetSettingsAsync(cancellationToken);
        var workerDefaults = options.Value.Workers;
        var orchestrator = saved.Orchestrator ?? new OrchestratorSettings();

        var maxWorkers = ClampPositive(orchestrator.MaxWorkers, 1, 256, workerDefaults.MaxWorkers);
        var minWorkers = ClampPositive(orchestrator.MinWorkers, 1, maxWorkers, 1);
        var maxProcessesPerWorker = 1;

        var resolved = new OrchestratorRuntimeSettings(
            MinWorkers: minWorkers,
            MaxWorkers: maxWorkers,
            MaxProcessesPerWorker: maxProcessesPerWorker,
            ReserveWorkers: ClampAllowZero(orchestrator.ReserveWorkers, 0, 128, 0),
            MaxQueueDepth: ClampPositive(orchestrator.MaxQueueDepth, 1, 50000, 200),
            QueueWaitTimeoutSeconds: ClampPositive(orchestrator.QueueWaitTimeoutSeconds, 5, 7200, 300),
            WorkerImagePolicy: orchestrator.WorkerImagePolicy,
            ContainerImage: string.IsNullOrWhiteSpace(workerDefaults.ContainerImage) ? "agentsdashboard-worker-gateway:latest" : workerDefaults.ContainerImage,
            ContainerNamePrefix: string.IsNullOrWhiteSpace(workerDefaults.ContainerNamePrefix) ? "worker-gateway" : workerDefaults.ContainerNamePrefix,
            DockerNetwork: string.IsNullOrWhiteSpace(workerDefaults.DockerNetwork) ? "agentsdashboard" : workerDefaults.DockerNetwork,
            ConnectivityMode: workerDefaults.ConnectivityMode,
            WorkerImageRegistry: orchestrator.WorkerImageRegistry ?? string.Empty,
            WorkerCanaryImage: orchestrator.WorkerCanaryImage?.Trim() ?? string.Empty,
            WorkerDockerBuildContextPath: orchestrator.WorkerDockerBuildContextPath ?? string.Empty,
            WorkerDockerfilePath: orchestrator.WorkerDockerfilePath ?? string.Empty,
            MaxConcurrentPulls: ClampPositive(orchestrator.MaxConcurrentPulls, 1, 16, 2),
            MaxConcurrentBuilds: ClampPositive(orchestrator.MaxConcurrentBuilds, 1, 8, 1),
            ImagePullTimeoutSeconds: ClampPositive(orchestrator.ImagePullTimeoutSeconds, 10, 3600, 120),
            ImageBuildTimeoutSeconds: ClampPositive(orchestrator.ImageBuildTimeoutSeconds, 30, 7200, 600),
            WorkerImageCacheTtlMinutes: ClampPositive(orchestrator.WorkerImageCacheTtlMinutes, 1, 10080, 240),
            ImageFailureCooldownMinutes: ClampPositive(orchestrator.ImageFailureCooldownMinutes, 1, 240, 15),
            CanaryPercent: ClampAllowZero(orchestrator.CanaryPercent, 0, 100, 10),
            MaxWorkerStartAttemptsPer10Min: ClampPositive(orchestrator.MaxWorkerStartAttemptsPer10Min, 1, 1000, 30),
            MaxFailedStartsPer10Min: ClampPositive(orchestrator.MaxFailedStartsPer10Min, 1, 1000, 10),
            CooldownMinutes: ClampPositive(orchestrator.CooldownMinutes, 1, 240, 15),
            ContainerStartTimeoutSeconds: ClampPositive(orchestrator.ContainerStartTimeoutSeconds, 5, 600, workerDefaults.StartupTimeoutSeconds),
            ContainerStopTimeoutSeconds: ClampPositive(orchestrator.ContainerStopTimeoutSeconds, 1, 600, 30),
            HealthProbeIntervalSeconds: ClampPositive(orchestrator.HealthProbeIntervalSeconds, 1, 300, 10),
            ContainerRestartLimit: ClampAllowZero(orchestrator.ContainerRestartLimit, 0, 100, 3),
            ContainerUnhealthyAction: orchestrator.ContainerUnhealthyAction,
            OrchestratorErrorBurstThreshold: ClampPositive(orchestrator.OrchestratorErrorBurstThreshold, 1, 10000, 20),
            OrchestratorErrorCoolDownMinutes: ClampPositive(orchestrator.OrchestratorErrorCoolDownMinutes, 1, 240, 10),
            EnableDraining: orchestrator.EnableDraining,
            DrainTimeoutSeconds: ClampPositive(orchestrator.DrainTimeoutSeconds, 5, 7200, 120),
            EnableAutoRecycle: orchestrator.EnableAutoRecycle,
            RecycleAfterRuns: ClampPositive(orchestrator.RecycleAfterRuns, 1, 100000, 200),
            RecycleAfterUptimeMinutes: ClampPositive(orchestrator.RecycleAfterUptimeMinutes, 10, 10080, 720),
            EnableContainerAutoCleanup: orchestrator.EnableContainerAutoCleanup,
            WorkerCpuLimit: orchestrator.WorkerCpuLimit ?? string.Empty,
            WorkerMemoryLimitMb: ClampAllowZero(orchestrator.WorkerMemoryLimitMb, 0, 1048576, 0),
            WorkerPidsLimit: ClampAllowZero(orchestrator.WorkerPidsLimit, 0, 100000, 0),
            WorkerFileDescriptorLimit: ClampAllowZero(orchestrator.WorkerFileDescriptorLimit, 0, 1048576, 0),
            RunHardTimeoutSeconds: ClampPositive(orchestrator.RunHardTimeoutSeconds, 30, 86400, 3600),
            MaxRunLogMb: ClampPositive(orchestrator.MaxRunLogMb, 1, 10240, 50),
            EnablePressureScaling: workerDefaults.EnablePressureScaling,
            CpuScaleOutThresholdPercent: workerDefaults.CpuScaleOutThresholdPercent,
            MemoryScaleOutThresholdPercent: workerDefaults.MemoryScaleOutThresholdPercent,
            PressureSampleWindowSeconds: workerDefaults.PressureSampleWindowSeconds);

        lock (_cacheLock)
        {
            _cached = resolved;
            _cachedUntilUtc = DateTime.UtcNow.Add(CacheDuration);
        }

        return resolved;
    }

    public void Invalidate()
    {
        lock (_cacheLock)
        {
            _cached = null;
            _cachedUntilUtc = DateTime.MinValue;
        }
    }

    private static int ClampPositive(int value, int min, int max, int fallback)
    {
        if (value <= 0)
        {
            value = fallback;
        }

        if (value < min)
        {
            return min;
        }

        if (value > max)
        {
            return max;
        }

        return value;
    }

    private static int ClampAllowZero(int value, int min, int max, int fallback)
    {
        if (value < min)
        {
            value = fallback;
        }

        if (value < min)
        {
            return min;
        }

        if (value > max)
        {
            return max;
        }

        return value;
    }
}
