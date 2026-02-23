namespace AgentsDashboard.ControlPlane.Services;

public sealed record TaskRuntimeHealthSnapshot(
    DateTime GeneratedAtUtc,
    int TotalRuntimes,
    int HealthyRuntimes,
    int DegradedRuntimes,
    int UnhealthyRuntimes,
    int RecoveringRuntimes,
    int OfflineRuntimes,
    int QuarantinedRuntimes,
    bool ReadinessBlocked,
    DateTime? ReadinessBlockedSinceUtc,
    DateTime? LastRemediationAtUtc,
    int RecentRemediationFailures,
    IReadOnlyList<TaskRuntimeHealthRuntimeSnapshot> Runtimes,
    IReadOnlyList<TaskRuntimeHealthIncident> Incidents)
{
    public static TaskRuntimeHealthSnapshot Empty { get; } = new(
        GeneratedAtUtc: DateTime.MinValue,
        TotalRuntimes: 0,
        HealthyRuntimes: 0,
        DegradedRuntimes: 0,
        UnhealthyRuntimes: 0,
        RecoveringRuntimes: 0,
        OfflineRuntimes: 0,
        QuarantinedRuntimes: 0,
        ReadinessBlocked: false,
        ReadinessBlockedSinceUtc: null,
        LastRemediationAtUtc: null,
        RecentRemediationFailures: 0,
        Runtimes: [],
        Incidents: []);
}
