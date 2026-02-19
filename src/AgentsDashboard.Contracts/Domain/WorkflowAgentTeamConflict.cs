namespace AgentsDashboard.Contracts.Domain;

































































public sealed class WorkflowAgentTeamConflict
{
    public string FilePath { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public List<string> LaneLabels { get; set; } = [];
    public List<string> HunkHeaders { get; set; } = [];
}
