namespace AgentsDashboard.TaskRuntime.Services;

public sealed record McpRuntimeBootstrapResult
{
    public bool HasConfig { get; init; }
    public bool IsValid { get; init; }
    public string ConfigPath { get; init; } = string.Empty;
    public string EffectiveJson { get; init; } = string.Empty;
    public int InstallActionCount { get; init; }
    public IReadOnlyList<string> Diagnostics { get; init; } = [];
}
