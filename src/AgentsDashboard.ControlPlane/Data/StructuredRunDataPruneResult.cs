namespace AgentsDashboard.ControlPlane.Data;






public sealed record StructuredRunDataPruneResult(
    int RunsScanned,
    int DeletedStructuredEvents,
    int DeletedDiffSnapshots,
    int DeletedToolProjections);
