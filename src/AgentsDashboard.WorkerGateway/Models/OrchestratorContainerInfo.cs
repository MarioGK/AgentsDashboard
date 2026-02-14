namespace AgentsDashboard.WorkerGateway.Models;

public sealed class OrchestratorContainerInfo
{
    public required string ContainerId { get; init; }
    public required string RunId { get; init; }
    public string TaskId { get; init; } = string.Empty;
    public string RepoId { get; init; } = string.Empty;
    public string ProjectId { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string Image { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}

public sealed record ContainerKillResult(bool Killed, string ContainerId, string Error);

public sealed class ContainerMetrics
{
    public double CpuPercent { get; set; }
    public long MemoryUsageBytes { get; set; }
    public long MemoryLimitBytes { get; set; }
    public double MemoryPercent { get; set; }
    public long NetworkRxBytes { get; set; }
    public long NetworkTxBytes { get; set; }
    public long BlockReadBytes { get; set; }
    public long BlockWriteBytes { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}
