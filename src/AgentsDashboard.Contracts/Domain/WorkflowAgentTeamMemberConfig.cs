namespace AgentsDashboard.Contracts.Domain;

































































public sealed class WorkflowAgentTeamMemberConfig
{
    public string Name { get; set; } = string.Empty;
    public string Harness { get; set; } = "codex";
    public HarnessExecutionMode Mode { get; set; } = HarnessExecutionMode.Default;
    public string RolePrompt { get; set; } = string.Empty;
    public string ModelOverride { get; set; } = string.Empty;
    public int? TimeoutSeconds { get; set; }
}
