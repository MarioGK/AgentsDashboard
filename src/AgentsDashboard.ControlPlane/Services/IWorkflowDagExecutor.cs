using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.ControlPlane.Services;

public interface IWorkflowDagExecutor
{
    Task<WorkflowExecutionV2Document> ExecuteWorkflowAsync(
        WorkflowV2Document workflow,
        string projectId,
        Dictionary<string, System.Text.Json.JsonElement>? initialContext,
        string triggeredBy,
        CancellationToken cancellationToken);

    Task<WorkflowExecutionV2Document?> ApproveWorkflowNodeAsync(
        string executionId,
        string approvedBy,
        bool approved,
        CancellationToken cancellationToken);

    Task<WorkflowExecutionV2Document?> CancelExecutionAsync(
        string executionId,
        CancellationToken cancellationToken);

    Task<WorkflowExecutionV2Document> ReplayFromDeadLetterAsync(
        WorkflowDeadLetterDocument deadLetter,
        string triggeredBy,
        CancellationToken cancellationToken);
}
