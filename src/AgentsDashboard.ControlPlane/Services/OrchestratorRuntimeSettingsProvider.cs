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
