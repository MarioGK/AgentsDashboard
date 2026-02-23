namespace AgentsDashboard.ControlPlane.Infrastructure.BackgroundWork;

public enum BackgroundWorkState
{
    Pending = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4,
}
