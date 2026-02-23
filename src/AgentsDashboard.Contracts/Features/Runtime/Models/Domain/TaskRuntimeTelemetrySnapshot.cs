namespace AgentsDashboard.Contracts.Features.Runtime.Models.Domain;

































































public sealed record TaskRuntimeTelemetrySnapshot(
    int TotalRuntimes,
    int ReadyRuntimes,
    int BusyRuntimes,
    int InactiveRuntimes,
    int FailedRuntimes,
    long TotalColdStarts,
    double AverageColdStartSeconds,
    double LastColdStartSeconds,
    long TotalInactiveTransitions,
    double AverageInactiveSeconds,
    double LastInactiveSeconds);
