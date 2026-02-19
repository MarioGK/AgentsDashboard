using AgentsDashboard.Contracts.Domain;
using MudBlazor;

namespace AgentsDashboard.ControlPlane.Components;

public enum TaskRunStatus
{
    Inactive = 0,
    Idle = 1,
    Queued = 2,
    Running = 3,
    PendingApproval = 4,
    Succeeded = 5,
    Failed = 6,
    Cancelled = 7,
    Obsolete = 8
}



public sealed record TaskStatusChipVisual(
    TaskRunStatus Status,
    string Label,
    Color Color,
    bool IsWorking,
    string Tooltip,
    DateTime? LatestRunTimestampUtc,
    string Summary);
