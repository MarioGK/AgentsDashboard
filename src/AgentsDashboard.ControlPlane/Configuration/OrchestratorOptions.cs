namespace AgentsDashboard.ControlPlane.Configuration;

public sealed class OrchestratorOptions
{
    public const string SectionName = "Orchestrator";

    public string MongoConnectionString { get; set; } = "mongodb://localhost:27017";
    public string MongoDatabase { get; set; } = "agentsdashboard";
    public string WorkerGrpcAddress { get; set; } = "http://localhost:5201";
    public int SchedulerIntervalSeconds { get; set; } = 10;
    public int MaxGlobalConcurrentRuns { get; set; } = 50;
    public int PerProjectConcurrencyLimit { get; set; } = 10;
    public int PerRepoConcurrencyLimit { get; set; } = 5;
    public RetryDefaultsConfig RetryDefaults { get; set; } = new();
    public TtlDaysConfig TtlDays { get; set; } = new();
    public DeadRunDetectionConfig DeadRunDetection { get; set; } = new();
    public StageTimeoutConfig StageTimeout { get; set; } = new();
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
