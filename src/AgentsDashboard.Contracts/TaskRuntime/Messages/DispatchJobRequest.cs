using System.Collections.Generic;
using AgentsDashboard.Contracts.Domain;
using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;
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
}
