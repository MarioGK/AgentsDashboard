namespace AgentsDashboard.Contracts.Domain;

































































public sealed class WorkflowAgentTeamDiffResult
{
    public int MergedFiles { get; set; }
    public int ConflictCount { get; set; }
    public int Additions { get; set; }
    public int Deletions { get; set; }
    public string MergedDiffStat { get; set; } = string.Empty;
    public string MergedPatch { get; set; } = string.Empty;
    public List<WorkflowAgentTeamLaneDiff> LaneDiffs { get; set; } = [];
    public List<WorkflowAgentTeamConflict> Conflicts { get; set; } = [];
}
