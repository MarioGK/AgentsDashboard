using System.ComponentModel.DataAnnotations;

namespace AgentsDashboard.ControlPlane.Configuration;


public enum TaskRuntimeConnectivityMode
{
    AutoDetect = 0,
    DockerDnsOnly = 1,
    HostPortOnly = 2
}





public sealed class OrchestratorOptions : IValidatableObject
{
    public const string SectionName = "Orchestrator";
    public const string DefaultLiteDbPath = "data/litedb/orchestrator.db";
    public const string DefaultArtifactsRootPath = "data/artifacts";

    public string LiteDbPath { get; set; } = DefaultLiteDbPath;
    public string ArtifactsRootPath { get; set; } = DefaultArtifactsRootPath;
    public TaskRuntimePoolConfig TaskRuntimes { get; set; } = new();
    public int SchedulerIntervalSeconds { get; set; } = 10;
    public int MaxGlobalConcurrentRuns { get; set; } = 50;
    public int PerProjectConcurrencyLimit { get; set; } = 10;
    public int PerRepoConcurrencyLimit { get; set; } = 5;
    public RetryDefaultsConfig RetryDefaults { get; set; } = new();
    public TtlDaysConfig TtlDays { get; set; } = new();
    public DeadRunDetectionConfig DeadRunDetection { get; set; } = new();
    public StageTimeoutConfig StageTimeout { get; set; } = new();
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
