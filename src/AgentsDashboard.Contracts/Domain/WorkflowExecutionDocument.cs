namespace AgentsDashboard.Contracts.Domain;

































































public sealed class WorkflowExecutionDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string WorkflowId { get; set; } = string.Empty;
    public string RepositoryId { get; set; } = string.Empty;
    public WorkflowExecutionState State { get; set; } = WorkflowExecutionState.Running;
    public int CurrentStageIndex { get; set; }
    public List<WorkflowStageResult> StageResults { get; set; } = [];
    public string PendingApprovalStageId { get; set; } = string.Empty;
    public string ApprovedBy { get; set; } = string.Empty;
    public string FailureReason { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
}
