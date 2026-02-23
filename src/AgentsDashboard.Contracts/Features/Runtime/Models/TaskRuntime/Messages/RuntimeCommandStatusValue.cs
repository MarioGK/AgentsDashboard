namespace AgentsDashboard.Contracts.TaskRuntime;

public enum RuntimeCommandStatusValue
{
    Unknown = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Canceled = 4,
    TimedOut = 5,
}
