namespace AgentsDashboard.ControlPlane.Configuration;

public sealed class DeadRunDetectionConfig
{
    public int CheckIntervalSeconds { get; set; } = 60;
    public int StaleRunThresholdMinutes { get; set; } = 30;
    public int ZombieRunThresholdMinutes { get; set; } = 120;
    public int MaxRunAgeHours { get; set; } = 24;
    public bool EnableAutoTermination { get; set; } = true;
    public bool ForceKillOnTimeout { get; set; } = true;
}
