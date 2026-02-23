namespace AgentsDashboard.ControlPlane.Features.RuntimeOrchestration.Services;

public sealed record OrchestratorHealthSnapshot(
    int RunningTaskRuntimes,
    int ReadyTaskRuntimes,
    int BusyTaskRuntimes,
    int DrainingTaskRuntimes,
    int DegradedTaskRuntimes,
    int UnhealthyTaskRuntimes,
    bool ScaleOutPaused,
    DateTime? ScaleOutCooldownUntilUtc,
    int StartAttemptsInWindow,
    int FailedStartsInWindow,
    DateTime? LastRemediationAtUtc,
    int RecentRemediationFailures,
    bool ReadinessBlocked);
