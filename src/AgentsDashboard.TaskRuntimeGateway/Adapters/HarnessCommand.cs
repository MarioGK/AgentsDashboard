namespace AgentsDashboard.TaskRuntimeGateway.Adapters;

public sealed class HarnessCommand
{
    public required string FileName { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public IDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
    public string? WorkingDirectory { get; init; }
    public bool UseShellExecute { get; init; }
}
