namespace AgentsDashboard.ControlPlane.Services;

public enum TaskRuntimeHealthStatus
{
    Healthy = 0,
    Degraded = 1,
    Unhealthy = 2,
    Recovering = 3,
    Offline = 4,
    Quarantined = 5
}
