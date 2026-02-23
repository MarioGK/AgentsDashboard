namespace AgentsDashboard.ControlPlane.Services;

public sealed record TaskRuntimeHealthRuntimeSnapshot(
    string RuntimeId,
    TaskRuntimeHealthStatus Status,
    string Reason,
    bool IsRunning,
    bool Online,
    DateTime? LastHeartbeatUtc,
    DateTime? LastProbeUtc,
    DateTime? LastHealthyUtc,
    DateTime? LastRemediationUtc,
    int ConsecutiveProbeFailures,
    int RestartAttempts,
    string Endpoint,
    int ActiveSlots,
    int MaxSlots);
