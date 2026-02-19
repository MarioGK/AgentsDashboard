namespace AgentsDashboard.Contracts.Domain;

































































public sealed class OrchestratorSettings
{
    public int MaxActiveTaskRuntimes { get; set; } = 100;
    public int DefaultTaskParallelRuns { get; set; } = 1;
    public int TaskRuntimeInactiveTimeoutMinutes { get; set; } = 15;
    public int MinWorkers { get; set; } = 4;
    public int MaxWorkers { get; set; } = 100;
    public int MaxProcessesPerWorker { get; set; } = 1;
    public int ReserveWorkers { get; set; } = 0;
    public int MaxQueueDepth { get; set; } = 200;
    public int QueueWaitTimeoutSeconds { get; set; } = 300;
    public string TaskPromptPrefix { get; set; } = string.Empty;
    public string TaskPromptSuffix { get; set; } = string.Empty;
    public string GlobalRunRules { get; set; } = string.Empty;
    public string McpConfigJson { get; set; } = string.Empty;

    public TaskRuntimeImagePolicy TaskRuntimeImagePolicy { get; set; } = TaskRuntimeImagePolicy.PreferLocal;
    public string TaskRuntimeImageRegistry { get; set; } = string.Empty;
    public string TaskRuntimeCanaryImage { get; set; } = string.Empty;
    public string WorkerDockerBuildContextPath { get; set; } = string.Empty;
    public string WorkerDockerfilePath { get; set; } = string.Empty;
    public int MaxConcurrentPulls { get; set; } = 2;
    public int MaxConcurrentBuilds { get; set; } = 1;
    public int ImagePullTimeoutSeconds { get; set; } = 120;
    public int ImageBuildTimeoutSeconds { get; set; } = 600;
    public int TaskRuntimeImageCacheTtlMinutes { get; set; } = 240;
    public int ImageFailureCooldownMinutes { get; set; } = 15;
    public int CanaryPercent { get; set; } = 10;

    public int MaxWorkerStartAttemptsPer10Min { get; set; } = 30;
    public int MaxFailedStartsPer10Min { get; set; } = 10;
    public int CooldownMinutes { get; set; } = 15;
    public int ContainerStartTimeoutSeconds { get; set; } = 60;
    public int ContainerStopTimeoutSeconds { get; set; } = 30;
    public int HealthProbeIntervalSeconds { get; set; } = 10;
    public int ContainerRestartLimit { get; set; } = 3;
    public ContainerUnhealthyAction ContainerUnhealthyAction { get; set; } = ContainerUnhealthyAction.Recreate;
    public int OrchestratorErrorBurstThreshold { get; set; } = 20;
    public int OrchestratorErrorCoolDownMinutes { get; set; } = 10;

    public bool EnableDraining { get; set; } = true;
    public int DrainTimeoutSeconds { get; set; } = 120;
    public bool EnableAutoRecycle { get; set; } = true;
    public int RecycleAfterRuns { get; set; } = 200;
    public int RecycleAfterUptimeMinutes { get; set; } = 720;
    public bool EnableContainerAutoCleanup { get; set; } = true;

    public string WorkerCpuLimit { get; set; } = string.Empty;
    public int WorkerMemoryLimitMb { get; set; } = 0;
    public int WorkerPidsLimit { get; set; } = 0;
    public int WorkerFileDescriptorLimit { get; set; } = 0;
    public int RunHardTimeoutSeconds { get; set; } = 3600;
    public int MaxRunLogMb { get; set; } = 50;
    public bool EnableTaskAutoCleanup { get; set; } = true;
    public int CleanupIntervalMinutes { get; set; } = 10;
    public int TaskRetentionDays { get; set; } = 180;
    public int DisabledTaskInactivityDays { get; set; } = 30;
    public int CleanupProtectedDays { get; set; } = 14;
    public bool CleanupExcludeWorkflowReferencedTasks { get; set; } = true;
    public bool CleanupExcludeTasksWithOpenFindings { get; set; } = true;
    public int DbSizeSoftLimitGb { get; set; } = 100;
    public int DbSizeTargetGb { get; set; } = 90;
    public int MaxTasksDeletedPerTick { get; set; } = 50;
    public bool EnableVacuumAfterPressureCleanup { get; set; } = false;
    public int VacuumMinDeletedRows { get; set; } = 10000;
}
