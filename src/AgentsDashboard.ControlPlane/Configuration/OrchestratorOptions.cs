using System.ComponentModel.DataAnnotations;

namespace AgentsDashboard.ControlPlane.Configuration;

public sealed class OrchestratorOptions : IValidatableObject
{
    public const string SectionName = "Orchestrator";

    public string SqliteConnectionString { get; set; } = "Data Source=/data/orchestrator.db";
    public string WorkerGrpcAddress { get; set; } = "http://worker-gateway:5201";
    public string WorkerContainerImage { get; set; } = "agentsdashboard-worker-gateway:latest";
    public string WorkerContainerName { get; set; } = "worker-gateway";
    public string WorkerDockerNetwork { get; set; } = "agentsdashboard";
    public int WorkerIdleTimeoutMinutes { get; set; } = 5;
    public int WorkerStartupTimeoutSeconds { get; set; } = 60;
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

        if (string.IsNullOrWhiteSpace(WorkerGrpcAddress))
            yield return new ValidationResult("WorkerGrpcAddress is required", [nameof(WorkerGrpcAddress)]);

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
    }
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
