using System.ComponentModel.DataAnnotations;

namespace AgentsDashboard.ControlPlane.Configuration;

public sealed class OrchestratorOptions : IValidatableObject
{
    public const string SectionName = "Orchestrator";

    public string SqliteConnectionString { get; set; } = "Data Source=/data/orchestrator.db";
    public string ArtifactsRootPath { get; set; } = "/data/artifacts";
    public WorkerPoolConfig Workers { get; set; } = new();
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
        if (string.IsNullOrWhiteSpace(SqliteConnectionString))
            yield return new ValidationResult("SqliteConnectionString is required", [nameof(SqliteConnectionString)]);

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

        if (Workers.MaxWorkers < 1 || Workers.MaxWorkers > 256)
            yield return new ValidationResult("Workers.MaxWorkers must be between 1 and 256", [$"{nameof(Workers)}.{nameof(Workers.MaxWorkers)}"]);

        if (Workers.SlotsPerWorker < 1 || Workers.SlotsPerWorker > 128)
            yield return new ValidationResult("Workers.SlotsPerWorker must be between 1 and 128", [$"{nameof(Workers)}.{nameof(Workers.SlotsPerWorker)}"]);

        if (Workers.IdleTimeoutMinutes < 1 || Workers.IdleTimeoutMinutes > 1440)
            yield return new ValidationResult("Workers.IdleTimeoutMinutes must be between 1 and 1440", [$"{nameof(Workers)}.{nameof(Workers.IdleTimeoutMinutes)}"]);

        if (Workers.StartupTimeoutSeconds < 5 || Workers.StartupTimeoutSeconds > 300)
            yield return new ValidationResult("Workers.StartupTimeoutSeconds must be between 5 and 300", [$"{nameof(Workers)}.{nameof(Workers.StartupTimeoutSeconds)}"]);

        if (string.IsNullOrWhiteSpace(Workers.ContainerImage))
            yield return new ValidationResult("Workers.ContainerImage is required", [$"{nameof(Workers)}.{nameof(Workers.ContainerImage)}"]);

        if (string.IsNullOrWhiteSpace(Workers.ContainerNamePrefix))
            yield return new ValidationResult("Workers.ContainerNamePrefix is required", [$"{nameof(Workers)}.{nameof(Workers.ContainerNamePrefix)}"]);

        if (string.IsNullOrWhiteSpace(Workers.DockerNetwork))
            yield return new ValidationResult("Workers.DockerNetwork is required", [$"{nameof(Workers)}.{nameof(Workers.DockerNetwork)}"]);

        if (Workers.PressureSampleWindowSeconds < 5 || Workers.PressureSampleWindowSeconds > 600)
            yield return new ValidationResult("Workers.PressureSampleWindowSeconds must be between 5 and 600", [$"{nameof(Workers)}.{nameof(Workers.PressureSampleWindowSeconds)}"]);

        if (Workers.CpuScaleOutThresholdPercent < 1 || Workers.CpuScaleOutThresholdPercent > 100)
            yield return new ValidationResult("Workers.CpuScaleOutThresholdPercent must be between 1 and 100", [$"{nameof(Workers)}.{nameof(Workers.CpuScaleOutThresholdPercent)}"]);

        if (Workers.MemoryScaleOutThresholdPercent < 1 || Workers.MemoryScaleOutThresholdPercent > 100)
            yield return new ValidationResult("Workers.MemoryScaleOutThresholdPercent must be between 1 and 100", [$"{nameof(Workers)}.{nameof(Workers.MemoryScaleOutThresholdPercent)}"]);
    }
}

public enum WorkerConnectivityMode
{
    AutoDetect = 0,
    DockerDnsOnly = 1,
    HostPortOnly = 2
}

public sealed class WorkerPoolConfig
{
    public int MaxWorkers { get; set; } = 100;
    public int SlotsPerWorker { get; set; } = 1;
    public int IdleTimeoutMinutes { get; set; } = 5;
    public int StartupTimeoutSeconds { get; set; } = 60;
    public string ContainerImage { get; set; } = "agentsdashboard-worker-gateway:latest";
    public string ContainerNamePrefix { get; set; } = "worker-gateway";
    public string DockerNetwork { get; set; } = "agentsdashboard";
    public WorkerConnectivityMode ConnectivityMode { get; set; } = WorkerConnectivityMode.AutoDetect;
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
