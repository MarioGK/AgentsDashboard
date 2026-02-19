namespace AgentsDashboard.Contracts.Domain;

































































public sealed class WorkflowAgentTeamLaneDiff
{
    public string LaneLabel { get; set; } = string.Empty;
    public string Harness { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public bool Succeeded { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string DiffStat { get; set; } = string.Empty;
    public string DiffPatch { get; set; } = string.Empty;
    public int FilesChanged { get; set; }
    public int Additions { get; set; }
    public int Deletions { get; set; }
    public List<string> FilePaths { get; set; } = [];
}
