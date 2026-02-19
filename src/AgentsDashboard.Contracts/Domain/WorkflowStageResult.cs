namespace AgentsDashboard.Contracts.Domain;

































































public sealed class WorkflowStageResult
{
    public string StageId { get; set; } = string.Empty;
    public string StageName { get; set; } = string.Empty;
    public WorkflowStageType StageType { get; set; }
    public bool Succeeded { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<string> RunIds { get; set; } = [];
    public WorkflowAgentTeamDiffResult? AgentTeamDiff { get; set; }
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAtUtc { get; set; }
}
