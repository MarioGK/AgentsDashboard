namespace AgentsDashboard.ControlPlane.Infrastructure.Data;






public sealed record TaskCleanupQuery(
    DateTime OlderThanUtc,
    DateTime ProtectedSinceUtc,
    int Limit,
    bool OnlyWithNoActiveRuns = true,
    string? RepositoryId = null,
    int ScanLimit = 0,
    bool IncludeRetentionEligibility = true,
    bool IncludeDisabledInactiveEligibility = false,
    DateTime DisabledInactiveOlderThanUtc = default);
