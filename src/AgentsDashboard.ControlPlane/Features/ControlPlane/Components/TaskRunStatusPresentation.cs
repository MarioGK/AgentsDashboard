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

public sealed record StatusChipVisual(string Label, Color Color, bool IsWorking);

public sealed record TaskStatusChipVisual(
    TaskRunStatus Status,
    string Label,
    Color Color,
    bool IsWorking,
    string Tooltip,
    DateTime? LatestRunTimestampUtc,
    string Summary);

public static class TaskRunStatusPresentation
{
    public static StatusChipVisual FromRunState(RunState state) => state switch
    {
        RunState.Queued => FromTaskStatus(TaskRunStatus.Queued),
        RunState.Running => FromTaskStatus(TaskRunStatus.Running),
        RunState.PendingApproval => FromTaskStatus(TaskRunStatus.PendingApproval),
        RunState.Succeeded => FromTaskStatus(TaskRunStatus.Succeeded),
        RunState.Failed => FromTaskStatus(TaskRunStatus.Failed),
        RunState.Cancelled => FromTaskStatus(TaskRunStatus.Cancelled),
        RunState.Obsolete => FromTaskStatus(TaskRunStatus.Obsolete),
        _ => FromTaskStatus(TaskRunStatus.Obsolete)
    };

    public static TaskStatusChipVisual FromTaskAndLatestRun(TaskDocument task, RunDocument? latestRun)
    {
        var status = ResolveTaskStatus(task, latestRun);
        var statusVisual = FromTaskStatus(status);
        var latestRunTimestampUtc = GetLatestRunTimestampUtc(latestRun);
        var summary = latestRun?.Summary?.Trim() ?? string.Empty;
        var tooltip = BuildTooltip(task.Enabled, statusVisual.Label, latestRunTimestampUtc, summary);

        return new TaskStatusChipVisual(
            status,
            statusVisual.Label,
            statusVisual.Color,
            statusVisual.IsWorking,
            tooltip,
            latestRunTimestampUtc,
            summary);
    }

    public static StatusChipVisual FromTaskStatus(TaskRunStatus status) => status switch
    {
        TaskRunStatus.Inactive => new("Inactive", Color.Default, false),
        TaskRunStatus.Idle => new("Idle", Color.Default, false),
        TaskRunStatus.Queued => new("Queued", Color.Warning, true),
        TaskRunStatus.Running => new("Running", Color.Info, true),
        TaskRunStatus.PendingApproval => new("PendingApproval", Color.Secondary, true),
        TaskRunStatus.Succeeded => new("Succeeded", Color.Success, false),
        TaskRunStatus.Failed => new("Failed", Color.Error, false),
        TaskRunStatus.Cancelled => new("Cancelled", Color.Default, false),
        TaskRunStatus.Obsolete => new("Obsolete", Color.Secondary, false),
        _ => new("Obsolete", Color.Secondary, false)
    };

    public static DateTime? GetLatestRunTimestampUtc(RunDocument? run)
        => run is null ? null : run.EndedAtUtc ?? run.StartedAtUtc ?? run.CreatedAtUtc;

    private static TaskRunStatus ResolveTaskStatus(TaskDocument task, RunDocument? latestRun)
    {
        if (!task.Enabled)
        {
            if (latestRun is null)
            {
                return TaskRunStatus.Obsolete;
            }

            if (latestRun.State is RunState.Queued or RunState.Running or RunState.PendingApproval)
            {
                return latestRun.State switch
                {
                    RunState.Queued => TaskRunStatus.Queued,
                    RunState.Running => TaskRunStatus.Running,
                    _ => TaskRunStatus.PendingApproval
                };
            }

            return TaskRunStatus.Obsolete;
        }

        if (latestRun is null)
        {
            return TaskRunStatus.Inactive;
        }

        return latestRun.State switch
        {
            RunState.Queued => TaskRunStatus.Queued,
            RunState.Running => TaskRunStatus.Running,
            RunState.PendingApproval => TaskRunStatus.PendingApproval,
            RunState.Succeeded => TaskRunStatus.Inactive,
            RunState.Failed => TaskRunStatus.Inactive,
            RunState.Cancelled => TaskRunStatus.Inactive,
            RunState.Obsolete => TaskRunStatus.Inactive,
            _ => TaskRunStatus.Obsolete
        };
    }

    private static string BuildTooltip(bool isTaskEnabled, string label, DateTime? latestRunTimestampUtc, string summary)
    {
        if (latestRunTimestampUtc is null)
        {
            return isTaskEnabled ? "No runs recorded yet." : "Task is disabled. No runs recorded yet.";
        }

        var latestRunText = latestRunTimestampUtc.Value.ToLocalTime().ToString("g");
        var statusPart = isTaskEnabled ? $"Status: {label}." : $"Status: {label}. Task is disabled.";
        var summaryPart = string.IsNullOrWhiteSpace(summary) ? string.Empty : $" Summary: {summary}";

        return $"Latest run: {latestRunText}. {statusPart}{summaryPart}".Trim();
    }
}
