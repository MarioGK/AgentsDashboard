namespace AgentsDashboard.Contracts.Domain;

































































public sealed class RunDiffSnapshotDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RunId { get; set; } = string.Empty;
    public string RepositoryId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public long Sequence { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string DiffStat { get; set; } = string.Empty;
    public string DiffPatch { get; set; } = string.Empty;
    public string SchemaVersion { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
