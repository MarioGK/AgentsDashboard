using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.Contracts.Api;












public sealed record RepositoryReliabilityMetrics(
    string RepositoryId,
    string RepositoryName,
    int TotalRuns,
    int SuccessfulRuns,
    int FailedRuns,
    double SuccessRate);
