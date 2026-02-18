using AgentsDashboard.Contracts.Worker;

namespace AgentsDashboard.WorkerGateway.Services.HarnessRuntimes;

public sealed record HarnessRunRequest
{
    public required string RunId { get; init; }
    public required string TaskId { get; init; }
    public required string Harness { get; init; }
    public required string Mode { get; init; }
    public required string Prompt { get; init; }
    public required string WorkspacePath { get; init; }
    public required Dictionary<string, string> Environment { get; init; }
    public required TimeSpan Timeout { get; init; }
    public string Command { get; init; } = string.Empty;
    public bool UseDocker { get; init; } = true;
    public string ArtifactsHostPath { get; init; } = string.Empty;
    public Dictionary<string, string> ContainerLabels { get; init; } = [];
    public double CpuLimit { get; init; } = 1.5;
    public string MemoryLimit { get; init; } = "2g";
    public bool NetworkDisabled { get; init; }
    public bool ReadOnlyRootFs { get; init; }
    public IReadOnlyList<DispatchInputPart> InputParts { get; init; } = [];
    public IReadOnlyList<DispatchImageAttachment> ImageAttachments { get; init; } = [];
    public bool PreferNativeMultimodal { get; init; } = true;
    public string MultimodalFallbackPolicy { get; init; } = "auto-text-reference";
    public string SessionProfileId { get; init; } = string.Empty;
    public string InstructionStackHash { get; init; } = string.Empty;
    public string McpConfigSnapshotJson { get; init; } = string.Empty;
    public string AutomationRunId { get; init; } = string.Empty;
}
