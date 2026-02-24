namespace AgentsDashboard.ControlPlane.Components.Workspace.Models;

public sealed class WorkspacePendingSubmissionState
{
    public required string TaskId { get; set; }

    public required string OptimisticRunId { get; init; }

    public string StatusText { get; set; } = "Creating task...";
}
