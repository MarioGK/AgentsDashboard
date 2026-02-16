using MessagePack;
using System.Collections.Generic;

namespace AgentsDashboard.Contracts.Worker;

// Request/Reply for DispatchJob
[MessagePackObject]
public record DispatchJobRequest
{
    [Key(0)] public required string RunId { get; init; }
    [Key(1)] public required string ProjectId { get; init; }
    [Key(2)] public required string RepositoryId { get; init; }
    [Key(3)] public required string TaskId { get; init; }
    [Key(4)] public required string HarnessType { get; init; }
    [Key(5)] public required string ImageTag { get; init; }
    [Key(6)] public required string CloneUrl { get; init; }
    [Key(7)] public string? Branch { get; init; }
    [Key(8)] public string? CommitSha { get; init; }
    [Key(9)] public string? WorkingDirectory { get; init; }
    [Key(10)] public required string Instruction { get; init; }
    [Key(11)] public Dictionary<string, string>? EnvironmentVars { get; init; }
    [Key(12)] public Dictionary<string, string>? Secrets { get; init; }
    [Key(13)] public string? ConcurrencyKey { get; init; }
    [Key(14)] public int TimeoutSeconds { get; init; }
    [Key(15)] public int RetryCount { get; init; }
    [Key(16)] public List<string>? ArtifactPatterns { get; init; }
    [Key(17)] public List<string>? LinkedFailureRuns { get; init; }
    [Key(18)] public string? CustomArgs { get; init; }
    [Key(19)] public DateTimeOffset DispatchedAt { get; init; }
    [Key(20)] public Dictionary<string, string>? ContainerLabels { get; init; }
    [Key(21)] public int Attempt { get; init; } = 1;
    [Key(22)] public double? SandboxProfileCpuLimit { get; init; }
    [Key(23)] public long? SandboxProfileMemoryLimit { get; init; }
    [Key(24)] public bool SandboxProfileNetworkDisabled { get; init; } = false;
    [Key(25)] public bool SandboxProfileReadOnlyRootFs { get; init; } = false;
    [Key(26)] public int? ArtifactPolicyMaxArtifacts { get; init; }
    [Key(27)] public long? ArtifactPolicyMaxTotalSizeBytes { get; init; }
}

[MessagePackObject]
public record DispatchJobReply
{
    [Key(0)] public bool Success { get; init; }
    [Key(1)] public string? ErrorMessage { get; init; }
    [Key(2)] public DateTimeOffset DispatchedAt { get; init; }
}

// Request/Reply for CancelJob
[MessagePackObject]
public record CancelJobRequest
{
    [Key(0)] public required string RunId { get; init; }
}

[MessagePackObject]
public record CancelJobReply
{
    [Key(0)] public bool Success { get; init; }
    [Key(1)] public string? ErrorMessage { get; init; }
}

// Request/Reply for KillContainer
[MessagePackObject]
public record KillContainerRequest
{
    [Key(0)] public required string ContainerId { get; init; }
}

[MessagePackObject]
public record KillContainerReply
{
    [Key(0)] public bool Success { get; init; }
    [Key(1)] public string? ErrorMessage { get; init; }
    [Key(2)] public bool WasRunning { get; init; }
}

// Request/Reply for Heartbeat
[MessagePackObject]
public record HeartbeatRequest
{
    [Key(0)] public required string WorkerId { get; init; }
    [Key(1)] public required string HostName { get; init; }
    [Key(2)] public int ActiveSlots { get; init; }
    [Key(3)] public int MaxSlots { get; init; }
    [Key(4)] public DateTimeOffset Timestamp { get; init; }
}

[MessagePackObject]
public record HeartbeatReply
{
    [Key(0)] public bool Success { get; init; }
    [Key(1)] public string? ErrorMessage { get; init; }
}

// Request/Reply for ReconcileOrphanedContainers
[MessagePackObject]
public record ReconcileOrphanedContainersRequest
{
    [Key(0)] public required string WorkerId { get; init; }
}

[MessagePackObject]
public record ReconcileOrphanedContainersReply
{
    [Key(0)] public bool Success { get; init; }
    [Key(1)] public string? ErrorMessage { get; init; }
    [Key(2)] public int ReconciledCount { get; init; }
    [Key(3)] public List<string>? ContainerIds { get; init; }
}
