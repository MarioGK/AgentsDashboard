

namespace AgentsDashboard.Contracts.Features.Repositories.Models.Api;












public sealed record ReliabilityMetrics(
    double SuccessRate7Days,
    double SuccessRate30Days,
    int TotalRuns7Days,
    int TotalRuns30Days,
    Dictionary<string, int> RunsByState,
    List<DailyFailureCount> FailureTrend14Days,
    double? AverageDurationSeconds,
    List<RepositoryReliabilityMetrics> PerRepositoryMetrics);
