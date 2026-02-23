

namespace AgentsDashboard.ControlPlane.Components.Workspace;

public static class WorkspaceStatusTextFormatter
{
    public static string FormatTaskStateLabel(TaskDocument task, RunDocument? latestRun)
    {
        if (latestRun is null)
        {
            return task.Enabled ? "Waiting" : "Paused";
        }

        return latestRun.State switch
        {
            RunState.Running => "Running now",
            RunState.Queued => "Queued up",
            RunState.PendingApproval => "Needs input",
            RunState.Succeeded => "Done",
            RunState.Failed => "Needs attention",
            RunState.Cancelled => "Stopped",
            RunState.Obsolete => "Archived",
            _ => "Waiting",
        };
    }
}
