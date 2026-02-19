namespace AgentsDashboard.ControlPlane.Services;

public sealed record OrchestratorHealthSnapshot(
    int RunningTaskRuntimes,
    int ReadyTaskRuntimes,
    int BusyTaskRuntimes,
    int DrainingTaskRuntimes,
    bool ScaleOutPaused,
    DateTime? ScaleOutCooldownUntilUtc,
    int StartAttemptsInWindow,
    int FailedStartsInWindow);
