namespace AgentsDashboard.Contracts.Domain;

public sealed class RunQuestionRequestDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RepositoryId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public string SourceToolCallId { get; set; } = string.Empty;
    public string SourceToolName { get; set; } = string.Empty;
    public long SourceSequence { get; set; }
    public string SourceSchemaVersion { get; set; } = string.Empty;
    public RunQuestionRequestStatus Status { get; set; } = RunQuestionRequestStatus.Pending;
    public List<RunQuestionItemDocument> Questions { get; set; } = [];
    public List<RunQuestionAnswerDocument> Answers { get; set; } = [];
    public string AnsweredRunId { get; set; } = string.Empty;
    public DateTime? AnsweredAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
