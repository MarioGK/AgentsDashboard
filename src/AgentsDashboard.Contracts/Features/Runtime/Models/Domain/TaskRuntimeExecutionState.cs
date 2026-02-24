namespace AgentsDashboard.Contracts.Features.Runtime.Models.Domain;

public enum TaskRuntimeExecutionState
{
    Unknown = 0,
    Queued = 1,
    Running = 2,
    Succeeded = 3,
    Failed = 4,
    Cancelled = 5,
}
