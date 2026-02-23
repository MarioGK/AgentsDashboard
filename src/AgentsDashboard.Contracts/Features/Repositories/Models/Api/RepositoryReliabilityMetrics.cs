

namespace AgentsDashboard.Contracts.Features.Repositories.Models.Api;












public sealed record RepositoryReliabilityMetrics(
    string RepositoryId,
    string RepositoryName,
    int TotalRuns,
    int SuccessfulRuns,
    int FailedRuns,
    double SuccessRate);
