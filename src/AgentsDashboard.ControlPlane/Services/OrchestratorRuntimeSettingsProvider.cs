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
    int MaxActiveTaskRuntimes,
    int DefaultTaskParallelRuns,
    int TaskRuntimeInactiveTimeoutMinutes,
    int MinWorkers,
    int MaxWorkers,
    int MaxProcessesPerWorker,
    int ReserveWorkers,
    int MaxQueueDepth,
    int QueueWaitTimeoutSeconds,
    TaskRuntimeImagePolicy TaskRuntimeImagePolicy,
    string ContainerImage,
    string ContainerNamePrefix,
    string DockerNetwork,
    TaskRuntimeConnectivityMode ConnectivityMode,
    string TaskRuntimeImageRegistry,
    string TaskRuntimeCanaryImage,
    string WorkerDockerBuildContextPath,
    string WorkerDockerfilePath,
    int MaxConcurrentPulls,
    int MaxConcurrentBuilds,
    int ImagePullTimeoutSeconds,
    int ImageBuildTimeoutSeconds,
    int TaskRuntimeImageCacheTtlMinutes,
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
        var taskRuntimeDefaults = options.Value.TaskRuntimes;
        var orchestrator = saved.Orchestrator ?? new OrchestratorSettings();

        var maxWorkers = ClampPositive(orchestrator.MaxWorkers, 1, 256, taskRuntimeDefaults.MaxTaskRuntimes);
        var minWorkers = ClampPositive(orchestrator.MinWorkers, 1, maxWorkers, 1);
        var maxProcessesPerWorker = 1;
        var maxActiveTaskRuntimes = ClampPositive(orchestrator.MaxActiveTaskRuntimes, 1, 512, maxWorkers);
        maxActiveTaskRuntimes = Math.Min(maxWorkers, maxActiveTaskRuntimes);
        var defaultTaskParallelRuns = ClampPositive(orchestrator.DefaultTaskParallelRuns, 1, 64, 1);
        var taskRuntimeInactiveTimeoutMinutes = ClampPositive(orchestrator.TaskRuntimeInactiveTimeoutMinutes, 1, 1440, 15);

        var resolved = new OrchestratorRuntimeSettings(
            MaxActiveTaskRuntimes: maxActiveTaskRuntimes,
            DefaultTaskParallelRuns: defaultTaskParallelRuns,
            TaskRuntimeInactiveTimeoutMinutes: taskRuntimeInactiveTimeoutMinutes,
            MinWorkers: minWorkers,
            MaxWorkers: maxActiveTaskRuntimes,
            MaxProcessesPerWorker: maxProcessesPerWorker,
            ReserveWorkers: ClampAllowZero(orchestrator.ReserveWorkers, 0, 128, 0),
            MaxQueueDepth: ClampPositive(orchestrator.MaxQueueDepth, 1, 50000, 200),
            QueueWaitTimeoutSeconds: ClampPositive(orchestrator.QueueWaitTimeoutSeconds, 5, 7200, 300),
            TaskRuntimeImagePolicy: orchestrator.TaskRuntimeImagePolicy,
            ContainerImage: string.IsNullOrWhiteSpace(taskRuntimeDefaults.ContainerImage) ? "agentsdashboard-task-runtime-gateway:latest" : taskRuntimeDefaults.ContainerImage,
            ContainerNamePrefix: string.IsNullOrWhiteSpace(taskRuntimeDefaults.ContainerNamePrefix) ? "task-runtime-gateway" : taskRuntimeDefaults.ContainerNamePrefix,
            DockerNetwork: string.IsNullOrWhiteSpace(taskRuntimeDefaults.DockerNetwork) ? "agentsdashboard" : taskRuntimeDefaults.DockerNetwork,
            ConnectivityMode: taskRuntimeDefaults.ConnectivityMode,
            TaskRuntimeImageRegistry: orchestrator.TaskRuntimeImageRegistry ?? string.Empty,
            TaskRuntimeCanaryImage: orchestrator.TaskRuntimeCanaryImage?.Trim() ?? string.Empty,
            WorkerDockerBuildContextPath: orchestrator.WorkerDockerBuildContextPath ?? string.Empty,
            WorkerDockerfilePath: orchestrator.WorkerDockerfilePath ?? string.Empty,
            MaxConcurrentPulls: ClampPositive(orchestrator.MaxConcurrentPulls, 1, 16, 2),
            MaxConcurrentBuilds: ClampPositive(orchestrator.MaxConcurrentBuilds, 1, 8, 1),
            ImagePullTimeoutSeconds: ClampPositive(orchestrator.ImagePullTimeoutSeconds, 10, 3600, 120),
            ImageBuildTimeoutSeconds: ClampPositive(orchestrator.ImageBuildTimeoutSeconds, 30, 7200, 600),
            TaskRuntimeImageCacheTtlMinutes: ClampPositive(orchestrator.TaskRuntimeImageCacheTtlMinutes, 1, 10080, 240),
            ImageFailureCooldownMinutes: ClampPositive(orchestrator.ImageFailureCooldownMinutes, 1, 240, 15),
            CanaryPercent: ClampAllowZero(orchestrator.CanaryPercent, 0, 100, 10),
            MaxWorkerStartAttemptsPer10Min: ClampPositive(orchestrator.MaxWorkerStartAttemptsPer10Min, 1, 1000, 30),
            MaxFailedStartsPer10Min: ClampPositive(orchestrator.MaxFailedStartsPer10Min, 1, 1000, 10),
            CooldownMinutes: ClampPositive(orchestrator.CooldownMinutes, 1, 240, 15),
            ContainerStartTimeoutSeconds: ClampPositive(orchestrator.ContainerStartTimeoutSeconds, 5, 600, taskRuntimeDefaults.StartupTimeoutSeconds),
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
            EnablePressureScaling: taskRuntimeDefaults.EnablePressureScaling,
            CpuScaleOutThresholdPercent: taskRuntimeDefaults.CpuScaleOutThresholdPercent,
            MemoryScaleOutThresholdPercent: taskRuntimeDefaults.MemoryScaleOutThresholdPercent,
            PressureSampleWindowSeconds: taskRuntimeDefaults.PressureSampleWindowSeconds);

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
