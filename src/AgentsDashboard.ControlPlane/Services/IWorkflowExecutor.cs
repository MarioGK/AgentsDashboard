using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.ControlPlane.Services;

public interface IWorkflowExecutor
{
    Task<WorkflowExecutionDocument> ExecuteWorkflowAsync(
        WorkflowDocument workflow,
        string projectId,
        CancellationToken cancellationToken);

    Task<WorkflowExecutionDocument?> ApproveWorkflowStageAsync(
        string executionId,
        string approvedBy,
        CancellationToken cancellationToken);
}
