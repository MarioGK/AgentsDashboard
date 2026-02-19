namespace AgentsDashboard.Contracts.Domain;

































































public sealed class WorkflowStageConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public WorkflowStageType Type { get; set; }
    public string? TaskId { get; set; }
    public string PromptOverride { get; set; } = string.Empty;
    public string CommandOverride { get; set; } = string.Empty;
    public int? DelaySeconds { get; set; }
    public List<string>? ParallelStageIds { get; set; }
    public List<WorkflowAgentTeamMemberConfig>? AgentTeamMembers { get; set; }
    public WorkflowSynthesisStageConfig? Synthesis { get; set; }
    public int? TimeoutMinutes { get; set; }
    public int Order { get; set; }
}
