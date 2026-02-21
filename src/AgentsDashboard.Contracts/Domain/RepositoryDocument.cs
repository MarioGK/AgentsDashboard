namespace AgentsDashboard.Contracts.Domain;

































































public class RepositoryDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string GitUrl { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
    public string DefaultBranch { get; set; } = "main";
    public string CurrentBranch { get; set; } = string.Empty;
    public string CurrentCommit { get; set; } = string.Empty;
    public int AheadCount { get; set; }
    public int BehindCount { get; set; }
    public int ModifiedCount { get; set; }
    public int StagedCount { get; set; }
    public int UntrackedCount { get; set; }
    public DateTime? LastScannedAtUtc { get; set; }
    public DateTime? LastFetchedAtUtc { get; set; }
    public DateTime? LastCloneAtUtc { get; set; }
    public DateTime? LastViewedAtUtc { get; set; }
    public string LastSyncError { get; set; } = string.Empty;
    public RepositoryTaskDefaultsConfig TaskDefaults { get; set; } = new();
    public List<InstructionFile> InstructionFiles { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
