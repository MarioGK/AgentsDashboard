namespace AgentsDashboard.Contracts.Features.Runtime.Models.TaskRuntime.Messages;

public enum RuntimeCommandStatusValue
{
    Unknown = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Canceled = 4,
    TimedOut = 5,
}
