using LiteDB;

namespace AgentsDashboard.TaskRuntime.Features.Execution.Services;

public sealed class TaskRuntimeRunLedgerDocument
{
    [BsonId]
    public string RunId { get; set; } = string.Empty;

    public string TaskId { get; set; } = string.Empty;
    public TaskRuntimeExecutionState State { get; set; } = TaskRuntimeExecutionState.Unknown;
    public string Summary { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public string RequestJson { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
