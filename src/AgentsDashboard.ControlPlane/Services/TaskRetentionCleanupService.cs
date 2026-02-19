using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;

namespace AgentsDashboard.ControlPlane.Services;


public sealed record TaskCleanupRunSummary(
    bool Executed,
    bool LeaseAcquired,
    bool AgeCleanupApplied,
    bool SizeCleanupApplied,
    bool VacuumExecuted,
    long InitialBytes,
    long FinalBytes,
    int TasksDeleted,
    int FailedTasks,
    int DeletedRows,
    string Reason);
