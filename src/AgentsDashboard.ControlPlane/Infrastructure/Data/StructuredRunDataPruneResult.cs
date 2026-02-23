namespace AgentsDashboard.ControlPlane.Infrastructure.Data;






public sealed record StructuredRunDataPruneResult(
    int RunsScanned,
    int DeletedStructuredEvents,
    int DeletedDiffSnapshots,
    int DeletedToolProjections);
