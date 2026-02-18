using System.Collections.Generic;
using AgentsDashboard.Contracts.Domain;
using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;

[MessagePackObject]
public record DispatchInputPart
{
    [Key(0)] public required string Type { get; init; }
    [Key(1)] public string Text { get; set; } = string.Empty;
    [Key(2)] public string ImageRef { get; set; } = string.Empty;
    [Key(3)] public string MimeType { get; set; } = string.Empty;
    [Key(4)] public int? Width { get; init; }
    [Key(5)] public int? Height { get; init; }
    [Key(6)] public long SizeBytes { get; init; }
    [Key(7)] public string Alt { get; set; } = string.Empty;
}

[MessagePackObject]
public record DispatchImageAttachment
{
    [Key(0)] public required string Id { get; init; }
    [Key(1)] public required string FileName { get; init; }
    [Key(2)] public required string MimeType { get; init; }
    [Key(3)] public long SizeBytes { get; init; }
    [Key(4)] public required string StoragePath { get; init; }
    [Key(5)] public required string Sha256 { get; init; }
    [Key(6)] public int? Width { get; init; }
    [Key(7)] public int? Height { get; init; }
}

// Request/Reply for DispatchJob
[MessagePackObject]
public record DispatchJobRequest
{
    [Key(0)] public required string RunId { get; init; }
    [Key(1)] public required string RepositoryId { get; init; }
    [Key(2)] public required string TaskId { get; init; }
    [Key(3)] public required string HarnessType { get; init; }
    [Key(4)] public required string ImageTag { get; init; }
    [Key(5)] public required string CloneUrl { get; init; }
    [Key(6)] public string? Branch { get; init; }
    [Key(7)] public string? CommitSha { get; init; }
    [Key(8)] public string? WorkingDirectory { get; init; }
    [Key(9)] public required string Instruction { get; init; }
    [Key(10)] public Dictionary<string, string>? EnvironmentVars { get; init; }
    [Key(11)] public Dictionary<string, string>? Secrets { get; init; }
    [Key(12)] public string? ConcurrencyKey { get; init; }
    [Key(13)] public int TimeoutSeconds { get; init; }
    [Key(14)] public int RetryCount { get; init; }
    [Key(15)] public List<string>? ArtifactPatterns { get; init; }
    [Key(16)] public List<string>? LinkedFailureRuns { get; init; }
    [Key(17)] public string? CustomArgs { get; init; }
    [Key(18)] public DateTimeOffset DispatchedAt { get; init; }
    [Key(19)] public Dictionary<string, string>? ContainerLabels { get; init; }
    [Key(20)] public int Attempt { get; set; } = 1;
    [Key(21)] public double? SandboxProfileCpuLimit { get; init; }
    [Key(22)] public long? SandboxProfileMemoryLimit { get; init; }
    [Key(23)] public bool SandboxProfileNetworkDisabled { get; set; } = false;
    [Key(24)] public bool SandboxProfileReadOnlyRootFs { get; set; } = false;
    [Key(25)] public int? ArtifactPolicyMaxArtifacts { get; init; }
    [Key(26)] public long? ArtifactPolicyMaxTotalSizeBytes { get; init; }
    [Key(27)] public HarnessExecutionMode Mode { get; set; } = HarnessExecutionMode.Default;
    [Key(28)] public string StructuredProtocolVersion { get; set; } = string.Empty;
    [Key(29)] public List<DispatchInputPart>? InputParts { get; init; }
    [Key(30)] public List<DispatchImageAttachment>? ImageAttachments { get; init; }
    [Key(31)] public bool PreferNativeMultimodal { get; set; } = true;
    [Key(32)] public string MultimodalFallbackPolicy { get; set; } = "auto-text-reference";
    [Key(33)] public string SessionProfileId { get; set; } = string.Empty;
    [Key(34)] public string InstructionStackHash { get; set; } = string.Empty;
    [Key(35)] public string McpConfigSnapshotJson { get; set; } = string.Empty;
    [Key(36)] public string AutomationRunId { get; set; } = string.Empty;
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
    [Key(0)] public required string TaskRuntimeId { get; init; }
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
    [Key(0)] public required string TaskRuntimeId { get; init; }
}

[MessagePackObject]
public record ReconcileOrphanedContainersReply
{
    [Key(0)] public bool Success { get; init; }
    [Key(1)] public string? ErrorMessage { get; init; }
    [Key(2)] public int ReconciledCount { get; init; }
    [Key(3)] public List<string>? ContainerIds { get; init; }
}

[MessagePackObject]
public record GetHarnessToolsRequest
{
    [Key(0)] public required string RequestId { get; init; }
}

[MessagePackObject]
public record HarnessToolStatus
{
    [Key(0)] public required string Command { get; init; }
    [Key(1)] public required string DisplayName { get; init; }
    [Key(2)] public required string Status { get; init; }
    [Key(3)] public string? Version { get; init; }
}

[MessagePackObject]
public record GetHarnessToolsReply
{
    [Key(0)] public bool Success { get; init; }
    [Key(1)] public string? ErrorMessage { get; init; }
    [Key(2)] public List<HarnessToolStatus> Tools { get; set; } = [];
    [Key(3)] public DateTimeOffset CheckedAt { get; init; }
}
