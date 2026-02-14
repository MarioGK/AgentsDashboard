namespace AgentsDashboard.WorkerGateway.Adapters;

public sealed class HarnessExecutionContext
{
    public required string RunId { get; init; }
    public required string Harness { get; init; }
    public required string Prompt { get; init; }
    public required string Command { get; init; }
    public required string Image { get; init; }
    public required string WorkspacePath { get; init; }
    public required string GitUrl { get; init; }
    public required string ArtifactsHostPath { get; init; } = string.Empty;
    public required IDictionary<string, string> Env { get; init; }
    public required IDictionary<string, string> ContainerLabels { get; init; }
    public int TimeoutSeconds { get; init; }
    public int Attempt { get; init; }
    public double CpuLimit { get; init; } = 1.5;
    public string MemoryLimit { get; init; } = "2g";
    public bool NetworkDisabled { get; init; }
    public bool ReadOnlyRootFs { get; init; }
}
