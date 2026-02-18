namespace AgentsDashboard.TaskRuntimeGateway.Configuration;

public sealed class TaskRuntimeOptions
{
    public const string SectionName = "TaskRuntime";

    public string TaskRuntimeId { get; set; } = Environment.MachineName;
    public int MaxSlots { get; set; } = 1;
    public bool UseDocker { get; set; } = true;
    public string DefaultImage { get; set; } = "ghcr.io/mariogk/ai-harness:latest";
    public Dictionary<string, string> HarnessImages { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["codex"] = "ghcr.io/mariogk/harness-codex:latest",
        ["opencode"] = "ghcr.io/mariogk/harness-opencode:latest",
        ["claude code"] = "ghcr.io/mariogk/harness-claudecode:latest",
        ["claude-code"] = "ghcr.io/mariogk/harness-claudecode:latest",
        ["zai"] = "ghcr.io/mariogk/harness-zai:latest",
    };
    public List<string> AllowedImages { get; set; } =
    [
        "ghcr.io/mariogk/ai-harness:*",
        "ghcr.io/mariogk/harness-codex:*",
        "ghcr.io/mariogk/harness-opencode:*",
        "ghcr.io/mariogk/harness-claudecode:*",
        "ghcr.io/mariogk/harness-zai:*",
    ];
    public List<string> SecretEnvPatterns { get; set; } =
    [
        "*_API_KEY",
        "*_TOKEN",
        "*_SECRET",
        "*_PASSWORD",
        "GH_TOKEN",
        "GITHUB_TOKEN",
        "ANTHROPIC_API_KEY",
        "CODEX_API_KEY",
        "OPENCODE_API_KEY",
        "Z_AI_API_KEY",
    ];
    public int DefaultTimeoutSeconds { get; set; } = 600;
    public string ArtifactStoragePath { get; set; } = "/data/artifacts";
}
