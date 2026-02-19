using System.ComponentModel.DataAnnotations;

namespace AgentsDashboard.ControlPlane.Configuration;

public sealed class OrchestratorOptions : IValidatableObject
{
    public const string SectionName = "Orchestrator";

    public string LiteDbPath { get; set; } = "/data/litedb/orchestrator.db";
    public string ArtifactsRootPath { get; set; } = "/data/artifacts";
    public TaskRuntimePoolConfig TaskRuntimes { get; set; } = new();
    public int SchedulerIntervalSeconds { get; set; } = 10;
    public int MaxGlobalConcurrentRuns { get; set; } = 50;
    public int PerProjectConcurrencyLimit { get; set; } = 10;
    public int PerRepoConcurrencyLimit { get; set; } = 5;
    public RetryDefaultsConfig RetryDefaults { get; set; } = new();
    public TtlDaysConfig TtlDays { get; set; } = new();
    public DeadRunDetectionConfig DeadRunDetection { get; set; } = new();
    public StageTimeoutConfig StageTimeout { get; set; } = new();
    public RateLimitConfig RateLimit { get; set; } = new();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(LiteDbPath))
            yield return new ValidationResult("LiteDbPath is required", [nameof(LiteDbPath)]);

        if (string.IsNullOrWhiteSpace(ArtifactsRootPath))
            yield return new ValidationResult("ArtifactsRootPath is required", [nameof(ArtifactsRootPath)]);

        if (SchedulerIntervalSeconds < 1 || SchedulerIntervalSeconds > 300)
            yield return new ValidationResult("SchedulerIntervalSeconds must be between 1 and 300", [nameof(SchedulerIntervalSeconds)]);

        if (MaxGlobalConcurrentRuns < 1)
            yield return new ValidationResult("MaxGlobalConcurrentRuns must be at least 1", [nameof(MaxGlobalConcurrentRuns)]);

        if (PerProjectConcurrencyLimit < 1)
            yield return new ValidationResult("PerProjectConcurrencyLimit must be at least 1", [nameof(PerProjectConcurrencyLimit)]);

        if (PerRepoConcurrencyLimit < 1)
            yield return new ValidationResult("PerRepoConcurrencyLimit must be at least 1", [nameof(PerRepoConcurrencyLimit)]);

        if (PerProjectConcurrencyLimit > MaxGlobalConcurrentRuns)
            yield return new ValidationResult("PerProjectConcurrencyLimit cannot exceed MaxGlobalConcurrentRuns", [nameof(PerProjectConcurrencyLimit)]);

        if (PerRepoConcurrencyLimit > PerProjectConcurrencyLimit)
            yield return new ValidationResult("PerRepoConcurrencyLimit cannot exceed PerProjectConcurrencyLimit", [nameof(PerRepoConcurrencyLimit)]);

        if (TaskRuntimes.MaxTaskRuntimes < 1 || TaskRuntimes.MaxTaskRuntimes > 256)
            yield return new ValidationResult("TaskRuntimes.MaxTaskRuntimes must be between 1 and 256", [$"{nameof(TaskRuntimes)}.{nameof(TaskRuntimes.MaxTaskRuntimes)}"]);

        if (TaskRuntimes.ParallelSlotsPerTaskRuntime < 1 || TaskRuntimes.ParallelSlotsPerTaskRuntime > 128)
            yield return new ValidationResult("TaskRuntimes.ParallelSlotsPerTaskRuntime must be between 1 and 128", [$"{nameof(TaskRuntimes)}.{nameof(TaskRuntimes.ParallelSlotsPerTaskRuntime)}"]);

        if (TaskRuntimes.IdleTimeoutMinutes < 1 || TaskRuntimes.IdleTimeoutMinutes > 1440)
            yield return new ValidationResult("TaskRuntimes.IdleTimeoutMinutes must be between 1 and 1440", [$"{nameof(TaskRuntimes)}.{nameof(TaskRuntimes.IdleTimeoutMinutes)}"]);

        if (TaskRuntimes.StartupTimeoutSeconds < 5 || TaskRuntimes.StartupTimeoutSeconds > 300)
            yield return new ValidationResult("TaskRuntimes.StartupTimeoutSeconds must be between 5 and 300", [$"{nameof(TaskRuntimes)}.{nameof(TaskRuntimes.StartupTimeoutSeconds)}"]);

        if (string.IsNullOrWhiteSpace(TaskRuntimes.ContainerImage))
            yield return new ValidationResult("TaskRuntimes.ContainerImage is required", [$"{nameof(TaskRuntimes)}.{nameof(TaskRuntimes.ContainerImage)}"]);

        if (string.IsNullOrWhiteSpace(TaskRuntimes.ContainerNamePrefix))
            yield return new ValidationResult("TaskRuntimes.ContainerNamePrefix is required", [$"{nameof(TaskRuntimes)}.{nameof(TaskRuntimes.ContainerNamePrefix)}"]);

        if (string.IsNullOrWhiteSpace(TaskRuntimes.DockerNetwork))
            yield return new ValidationResult("TaskRuntimes.DockerNetwork is required", [$"{nameof(TaskRuntimes)}.{nameof(TaskRuntimes.DockerNetwork)}"]);

        if (TaskRuntimes.PressureSampleWindowSeconds < 5 || TaskRuntimes.PressureSampleWindowSeconds > 600)
            yield return new ValidationResult("TaskRuntimes.PressureSampleWindowSeconds must be between 5 and 600", [$"{nameof(TaskRuntimes)}.{nameof(TaskRuntimes.PressureSampleWindowSeconds)}"]);

        if (TaskRuntimes.CpuScaleOutThresholdPercent < 1 || TaskRuntimes.CpuScaleOutThresholdPercent > 100)
            yield return new ValidationResult("TaskRuntimes.CpuScaleOutThresholdPercent must be between 1 and 100", [$"{nameof(TaskRuntimes)}.{nameof(TaskRuntimes.CpuScaleOutThresholdPercent)}"]);

        if (TaskRuntimes.MemoryScaleOutThresholdPercent < 1 || TaskRuntimes.MemoryScaleOutThresholdPercent > 100)
            yield return new ValidationResult("TaskRuntimes.MemoryScaleOutThresholdPercent must be between 1 and 100", [$"{nameof(TaskRuntimes)}.{nameof(TaskRuntimes.MemoryScaleOutThresholdPercent)}"]);
    }
}

public enum TaskRuntimeConnectivityMode
{
    AutoDetect = 0,
    DockerDnsOnly = 1,
    HostPortOnly = 2
}

public sealed class TaskRuntimePoolConfig
{
    public int MaxTaskRuntimes { get; set; } = 100;
    public int ParallelSlotsPerTaskRuntime { get; set; } = 1;
    public int IdleTimeoutMinutes { get; set; } = 5;
    public int StartupTimeoutSeconds { get; set; } = 60;
    public string ContainerImage { get; set; } = "agentsdashboard-task-runtime-gateway:latest";
    public string ContainerNamePrefix { get; set; } = "task-runtime-gateway";
    public string DockerNetwork { get; set; } = "agentsdashboard";
    public TaskRuntimeConnectivityMode ConnectivityMode { get; set; } = TaskRuntimeConnectivityMode.AutoDetect;
    public bool EnablePressureScaling { get; set; } = true;
    public int CpuScaleOutThresholdPercent { get; set; } = 85;
    public int MemoryScaleOutThresholdPercent { get; set; } = 85;
    public int PressureSampleWindowSeconds { get; set; } = 30;
}

public sealed class RetryDefaultsConfig
{
    public int MaxAttempts { get; set; } = 3;
    public int BackoffBaseSeconds { get; set; } = 10;
    public double BackoffMultiplier { get; set; } = 2.0;
}

public sealed class TtlDaysConfig
{
    public int Logs { get; set; } = 30;
    public int Runs { get; set; } = 90;
}

public sealed class DeadRunDetectionConfig
{
    public int CheckIntervalSeconds { get; set; } = 60;
    public int StaleRunThresholdMinutes { get; set; } = 30;
    public int ZombieRunThresholdMinutes { get; set; } = 120;
    public int MaxRunAgeHours { get; set; } = 24;
    public bool EnableAutoTermination { get; set; } = true;
    public bool ForceKillOnTimeout { get; set; } = true;
}

public sealed class StageTimeoutConfig
{
    public int DefaultTaskStageTimeoutMinutes { get; set; } = 60;
    public int DefaultApprovalStageTimeoutHours { get; set; } = 24;
    public int DefaultParallelStageTimeoutMinutes { get; set; } = 90;
    public int MaxStageTimeoutHours { get; set; } = 48;
}

public sealed class RateLimitConfig
{
    public int WebhookPermitLimit { get; set; } = 30;
    public int WebhookWindowSeconds { get; set; } = 60;
    public int GlobalPermitLimit { get; set; } = 100;
    public int GlobalWindowSeconds { get; set; } = 60;
    public int BurstPermitLimit { get; set; } = 20;
    public int BurstWindowSeconds { get; set; } = 1;
}
