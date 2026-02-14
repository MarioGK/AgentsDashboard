using System.Text.Json;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class WorkflowDagExecutor(
    IOrchestratorStore store,
    RunDispatcher dispatcher,
    IContainerReaper containerReaper,
    IRunEventPublisher publisher,
    IOptions<OrchestratorOptions> options,
    ILogger<WorkflowDagExecutor> logger) : IWorkflowDagExecutor
{
    public async Task<WorkflowExecutionV2Document> ExecuteWorkflowAsync(
        WorkflowV2Document workflow,
        string projectId,
        Dictionary<string, JsonElement>? initialContext,
        string triggeredBy,
        CancellationToken cancellationToken)
    {
        var execution = new WorkflowExecutionV2Document
        {
            WorkflowV2Id = workflow.Id,
            RepositoryId = workflow.RepositoryId,
            ProjectId = projectId,
            State = WorkflowV2ExecutionState.Running,
            Context = initialContext ?? [],
            TriggeredBy = triggeredBy,
            StartedAtUtc = DateTime.UtcNow
        };

        execution = await store.CreateExecutionV2Async(execution, cancellationToken);

        logger.LogInformation("Starting DAG workflow execution {ExecutionId} for workflow {WorkflowId}",
            execution.Id, workflow.Id);

        await publisher.PublishWorkflowV2ExecutionStateAsync(execution, cancellationToken);

        _ = Task.Run(async () =>
        {
            try
            {
                await ExecuteDagAsync(execution, workflow, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled exception in DAG execution {ExecutionId}", execution.Id);
                await store.MarkExecutionV2CompletedAsync(
                    execution.Id,
                    WorkflowV2ExecutionState.Failed,
                    $"Unhandled exception: {ex.Message}",
                    cancellationToken);
            }
        }, cancellationToken);

        return execution;
    }

    private async Task ExecuteDagAsync(
        WorkflowExecutionV2Document execution,
        WorkflowV2Document workflow,
        CancellationToken cancellationToken)
    {
        var startNode = workflow.Nodes.FirstOrDefault(n => n.Type == WorkflowNodeType.Start);
        if (startNode is null)
        {
            await store.MarkExecutionV2CompletedAsync(execution.Id, WorkflowV2ExecutionState.Failed, "No Start node found", cancellationToken);
            return;
        }

        var startResult = new WorkflowNodeResult
        {
            NodeId = startNode.Id,
            NodeName = startNode.Name,
            NodeType = WorkflowNodeType.Start,
            State = WorkflowNodeState.Succeeded,
            StartedAtUtc = DateTime.UtcNow,
            EndedAtUtc = DateTime.UtcNow
        };

        execution.NodeResults.Add(startResult);
        execution.CurrentNodeId = startNode.Id;
        await store.UpdateExecutionV2Async(execution, cancellationToken);
        await publisher.PublishWorkflowV2NodeStateAsync(execution, startResult, cancellationToken);

        await AdvanceFromNodeAsync(execution, workflow, startNode, startResult, cancellationToken);
    }

    private async Task AdvanceFromNodeAsync(
        WorkflowExecutionV2Document execution,
        WorkflowV2Document workflow,
        WorkflowNodeConfig completedNode,
        WorkflowNodeResult completedResult,
        CancellationToken cancellationToken)
    {
        var outEdges = workflow.Edges
            .Where(e => e.SourceNodeId == completedNode.Id)
            .OrderBy(e => e.Priority)
            .ToList();

        if (outEdges.Count == 0)
        {
            if (completedNode.Type == WorkflowNodeType.End)
                return;

            await store.MarkExecutionV2CompletedAsync(execution.Id, WorkflowV2ExecutionState.Failed, $"Node '{completedNode.Name}' has no outgoing edges", cancellationToken);
            return;
        }

        var run = completedResult.RunId is not null
            ? await store.GetRunAsync(completedResult.RunId, cancellationToken)
            : null;

        WorkflowEdgeConfig? matchedEdge = null;
        foreach (var edge in outEdges)
        {
            if (EdgeConditionEvaluator.Evaluate(edge.Condition, completedResult, run, execution.Context))
            {
                matchedEdge = edge;
                break;
            }
        }

        if (matchedEdge is null)
        {
            logger.LogWarning("No edge condition matched for node {NodeName} in execution {ExecutionId}",
                completedNode.Name, execution.Id);
            await CreateDeadLetterAsync(execution, workflow, completedNode, "No edge condition matched", completedResult.RunId, cancellationToken);
            await store.MarkExecutionV2CompletedAsync(execution.Id, WorkflowV2ExecutionState.Failed, $"No edge condition matched after node '{completedNode.Name}'", cancellationToken);
            return;
        }

        var targetNode = workflow.Nodes.FirstOrDefault(n => n.Id == matchedEdge.TargetNodeId);
        if (targetNode is null)
        {
            await store.MarkExecutionV2CompletedAsync(execution.Id, WorkflowV2ExecutionState.Failed, $"Target node '{matchedEdge.TargetNodeId}' not found", cancellationToken);
            return;
        }

        await ExecuteNodeAsync(execution, workflow, targetNode, cancellationToken);
    }

    private async Task ExecuteNodeAsync(
        WorkflowExecutionV2Document execution,
        WorkflowV2Document workflow,
        WorkflowNodeConfig node,
        CancellationToken cancellationToken)
    {
        execution.CurrentNodeId = node.Id;
        await store.UpdateExecutionV2Async(execution, cancellationToken);

        var nodeResult = new WorkflowNodeResult
        {
            NodeId = node.Id,
            NodeName = node.Name,
            NodeType = node.Type,
            State = WorkflowNodeState.Running,
            StartedAtUtc = DateTime.UtcNow
        };

        execution.NodeResults.Add(nodeResult);
        await store.UpdateExecutionV2Async(execution, cancellationToken);
        await publisher.PublishWorkflowV2NodeStateAsync(execution, nodeResult, cancellationToken);

        var retryPolicy = node.RetryPolicy ?? new RetryPolicyConfig(1, 10, 2.0);
        var maxAttempts = retryPolicy.MaxAttempts;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            nodeResult.Attempt = attempt;

            try
            {
                switch (node.Type)
                {
                    case WorkflowNodeType.Agent:
                        await ExecuteAgentNodeAsync(execution, node, nodeResult, cancellationToken);
                        break;
                    case WorkflowNodeType.Delay:
                        await ExecuteDelayNodeAsync(node, nodeResult, cancellationToken);
                        break;
                    case WorkflowNodeType.Approval:
                        await ExecuteApprovalNodeAsync(execution, node, nodeResult, cancellationToken);
                        break;
                    case WorkflowNodeType.End:
                        nodeResult.State = WorkflowNodeState.Succeeded;
                        nodeResult.Summary = "Workflow completed";
                        nodeResult.EndedAtUtc = DateTime.UtcNow;
                        await store.UpdateExecutionV2Async(execution, cancellationToken);
                        await publisher.PublishWorkflowV2NodeStateAsync(execution, nodeResult, cancellationToken);
                        await store.MarkExecutionV2CompletedAsync(execution.Id, WorkflowV2ExecutionState.Succeeded, string.Empty, cancellationToken);
                        var completedExec = await store.GetExecutionV2Async(execution.Id, cancellationToken);
                        if (completedExec is not null)
                            await publisher.PublishWorkflowV2ExecutionStateAsync(completedExec, cancellationToken);
                        return;
                    default:
                        nodeResult.State = WorkflowNodeState.Failed;
                        nodeResult.Summary = $"Unknown node type: {node.Type}";
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                nodeResult.State = WorkflowNodeState.Failed;
                nodeResult.Summary = "Cancelled";
                nodeResult.EndedAtUtc = DateTime.UtcNow;
                await store.UpdateExecutionV2Async(execution, cancellationToken);
                await store.MarkExecutionV2CompletedAsync(execution.Id, WorkflowV2ExecutionState.Cancelled, "Cancelled", cancellationToken);
                return;
            }
            catch (Exception ex)
            {
                nodeResult.State = WorkflowNodeState.Failed;
                nodeResult.Summary = ex.Message;
            }

            if (nodeResult.State == WorkflowNodeState.Succeeded)
                break;

            if (attempt < maxAttempts)
            {
                var delay = TimeSpan.FromSeconds(retryPolicy.BackoffBaseSeconds * Math.Pow(retryPolicy.BackoffMultiplier, attempt - 1));
                logger.LogWarning("Node '{NodeName}' attempt {Attempt} failed, retrying in {Delay}s",
                    node.Name, attempt, delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken);
            }
        }

        nodeResult.EndedAtUtc = DateTime.UtcNow;
        await store.UpdateExecutionV2Async(execution, cancellationToken);
        await publisher.PublishWorkflowV2NodeStateAsync(execution, nodeResult, cancellationToken);

        if (nodeResult.State == WorkflowNodeState.Succeeded)
        {
            if (node.OutputMappings.Count > 0)
            {
                var run = nodeResult.RunId is not null
                    ? await store.GetRunAsync(nodeResult.RunId, cancellationToken)
                    : null;
                WorkflowContextMapper.ApplyOutputMappings(node.OutputMappings, run, nodeResult, execution.Context);
                await store.UpdateExecutionV2Async(execution, cancellationToken);
            }

            await AdvanceFromNodeAsync(execution, workflow, node, nodeResult, cancellationToken);
        }
        else
        {
            nodeResult.State = WorkflowNodeState.DeadLettered;
            await store.UpdateExecutionV2Async(execution, cancellationToken);
            await CreateDeadLetterAsync(execution, workflow, node, nodeResult.Summary, nodeResult.RunId, cancellationToken);
            await store.MarkExecutionV2CompletedAsync(execution.Id, WorkflowV2ExecutionState.Failed, $"Node '{node.Name}' failed after {maxAttempts} attempt(s): {nodeResult.Summary}", cancellationToken);
            var failedExec = await store.GetExecutionV2Async(execution.Id, cancellationToken);
            if (failedExec is not null)
                await publisher.PublishWorkflowV2ExecutionStateAsync(failedExec, cancellationToken);
        }
    }

    private async Task ExecuteAgentNodeAsync(
        WorkflowExecutionV2Document execution,
        WorkflowNodeConfig node,
        WorkflowNodeResult nodeResult,
        CancellationToken cancellationToken)
    {
        var agent = await store.GetAgentAsync(node.AgentId!, cancellationToken);
        if (agent is null)
        {
            nodeResult.State = WorkflowNodeState.Failed;
            nodeResult.Summary = $"Agent '{node.AgentId}' not found";
            return;
        }

        var repository = await store.GetRepositoryAsync(agent.RepositoryId, cancellationToken);
        if (repository is null)
        {
            nodeResult.State = WorkflowNodeState.Failed;
            nodeResult.Summary = $"Repository '{agent.RepositoryId}' not found";
            return;
        }

        var project = await store.GetProjectAsync(repository.ProjectId, cancellationToken);
        if (project is null)
        {
            nodeResult.State = WorkflowNodeState.Failed;
            nodeResult.Summary = $"Project '{repository.ProjectId}' not found";
            return;
        }

        var prompt = agent.Prompt;
        if (node.InputMappings.Count > 0)
            prompt = WorkflowContextMapper.ApplyInputMappings(node.InputMappings, execution.Context, prompt);

        var transientTask = new TaskDocument
        {
            Name = $"[DAG] {agent.Name}",
            RepositoryId = agent.RepositoryId,
            Kind = TaskKind.OneShot,
            Harness = agent.Harness,
            Prompt = prompt,
            Command = agent.Command,
            AutoCreatePullRequest = agent.AutoCreatePullRequest,
            Enabled = true,
            RetryPolicy = new RetryPolicyConfig(1),
            Timeouts = agent.Timeouts,
            SandboxProfile = agent.SandboxProfile,
            ArtifactPolicy = agent.ArtifactPolicy,
            ArtifactPatterns = agent.ArtifactPatterns,
            InstructionFiles = agent.InstructionFiles,
            ConcurrencyLimit = 0
        };

        var run = await store.CreateRunAsync(transientTask, project.Id, cancellationToken);
        nodeResult.RunId = run.Id;

        var dispatched = await dispatcher.DispatchAsync(project, repository, transientTask, run, cancellationToken);
        if (!dispatched)
        {
            nodeResult.State = WorkflowNodeState.Failed;
            nodeResult.Summary = "Failed to dispatch run (concurrency limit or worker unavailable)";
            return;
        }

        var timeout = node.TimeoutMinutes.HasValue
            ? TimeSpan.FromMinutes(node.TimeoutMinutes.Value)
            : TimeSpan.FromSeconds(agent.Timeouts.OverallSeconds);

        var pollingInterval = TimeSpan.FromSeconds(2);
        var startTime = DateTime.UtcNow;

        while (DateTime.UtcNow - startTime < timeout)
        {
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);

            await Task.Delay(pollingInterval, cancellationToken);

            var updatedRun = await store.GetRunAsync(run.Id, cancellationToken);
            if (updatedRun is null)
            {
                nodeResult.State = WorkflowNodeState.Failed;
                nodeResult.Summary = "Run disappeared from database";
                return;
            }

            switch (updatedRun.State)
            {
                case RunState.Succeeded:
                    nodeResult.State = WorkflowNodeState.Succeeded;
                    nodeResult.Summary = updatedRun.Summary;
                    return;
                case RunState.Failed:
                    nodeResult.State = WorkflowNodeState.Failed;
                    nodeResult.Summary = $"Run failed: {updatedRun.Summary}";
                    return;
                case RunState.Cancelled:
                    nodeResult.State = WorkflowNodeState.Failed;
                    nodeResult.Summary = "Run was cancelled";
                    return;
            }
        }

        try
        {
            await containerReaper.KillContainerAsync(run.Id, "DAG node timeout", force: true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to kill container for timed-out run {RunId}", run.Id);
        }

        nodeResult.State = WorkflowNodeState.TimedOut;
        nodeResult.Summary = $"Timeout after {timeout.TotalMinutes:F0} minutes";
    }

    private static async Task ExecuteDelayNodeAsync(
        WorkflowNodeConfig node,
        WorkflowNodeResult nodeResult,
        CancellationToken cancellationToken)
    {
        var delaySeconds = node.DelaySeconds ?? 0;
        if (delaySeconds <= 0)
        {
            nodeResult.State = WorkflowNodeState.Succeeded;
            nodeResult.Summary = "No delay configured, skipped";
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
        nodeResult.State = WorkflowNodeState.Succeeded;
        nodeResult.Summary = $"Delayed for {delaySeconds} seconds";
    }

    private async Task ExecuteApprovalNodeAsync(
        WorkflowExecutionV2Document execution,
        WorkflowNodeConfig node,
        WorkflowNodeResult nodeResult,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Node '{NodeName}' requires approval by role {ApproverRole}",
            node.Name, node.ApproverRole ?? "any");

        await store.MarkExecutionV2PendingApprovalAsync(execution.Id, node.Id, cancellationToken);
        execution.State = WorkflowV2ExecutionState.PendingApproval;
        execution.PendingApprovalNodeId = node.Id;
        await publisher.PublishWorkflowV2ExecutionStateAsync(execution, cancellationToken);

        var timeout = node.TimeoutMinutes.HasValue
            ? TimeSpan.FromMinutes(node.TimeoutMinutes.Value)
            : TimeSpan.FromHours(24);

        var pollingInterval = TimeSpan.FromSeconds(5);
        var startTime = DateTime.UtcNow;

        while (DateTime.UtcNow - startTime < timeout)
        {
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);

            await Task.Delay(pollingInterval, cancellationToken);

            var updated = await store.GetExecutionV2Async(execution.Id, cancellationToken);
            if (updated is null)
            {
                nodeResult.State = WorkflowNodeState.Failed;
                nodeResult.Summary = "Execution disappeared from database";
                return;
            }

            if (updated.State == WorkflowV2ExecutionState.Running && updated.PendingApprovalNodeId == string.Empty)
            {
                nodeResult.State = WorkflowNodeState.Succeeded;
                nodeResult.Summary = $"Approved by {updated.ApprovedBy}";
                execution.State = WorkflowV2ExecutionState.Running;
                execution.ApprovedBy = updated.ApprovedBy;
                execution.PendingApprovalNodeId = string.Empty;
                return;
            }

            if (updated.State == WorkflowV2ExecutionState.Cancelled)
            {
                nodeResult.State = WorkflowNodeState.Failed;
                nodeResult.Summary = "Workflow cancelled during approval";
                throw new OperationCanceledException();
            }
        }

        nodeResult.State = WorkflowNodeState.TimedOut;
        nodeResult.Summary = $"Approval timed out after {timeout.TotalHours:F0} hours";
    }

    public async Task<WorkflowExecutionV2Document?> ApproveWorkflowNodeAsync(
        string executionId,
        string approvedBy,
        bool approved,
        CancellationToken cancellationToken)
    {
        if (!approved)
        {
            return await store.MarkExecutionV2CompletedAsync(
                executionId, WorkflowV2ExecutionState.Cancelled, "Approval rejected", cancellationToken);
        }

        return await store.ApproveExecutionV2NodeAsync(executionId, approvedBy, cancellationToken);
    }

    public async Task<WorkflowExecutionV2Document?> CancelExecutionAsync(
        string executionId,
        CancellationToken cancellationToken)
    {
        return await store.MarkExecutionV2CompletedAsync(
            executionId, WorkflowV2ExecutionState.Cancelled, "Cancelled by user", cancellationToken);
    }

    public async Task<WorkflowExecutionV2Document> ReplayFromDeadLetterAsync(
        WorkflowDeadLetterDocument deadLetter,
        string triggeredBy,
        CancellationToken cancellationToken)
    {
        var workflow = await store.GetWorkflowV2Async(deadLetter.WorkflowV2Id, cancellationToken)
            ?? throw new InvalidOperationException($"Workflow '{deadLetter.WorkflowV2Id}' not found");

        var repository = await store.GetRepositoryAsync(workflow.RepositoryId, cancellationToken)
            ?? throw new InvalidOperationException($"Repository '{workflow.RepositoryId}' not found");

        var execution = await ExecuteWorkflowAsync(
            workflow,
            repository.ProjectId,
            deadLetter.InputContextSnapshot,
            triggeredBy,
            cancellationToken);

        await store.MarkDeadLetterReplayedAsync(deadLetter.Id, execution.Id, cancellationToken);

        return execution;
    }

    private async Task CreateDeadLetterAsync(
        WorkflowExecutionV2Document execution,
        WorkflowV2Document workflow,
        WorkflowNodeConfig node,
        string failureReason,
        string? runId,
        CancellationToken cancellationToken)
    {
        var deadLetter = new WorkflowDeadLetterDocument
        {
            ExecutionId = execution.Id,
            WorkflowV2Id = workflow.Id,
            FailedNodeId = node.Id,
            FailedNodeName = node.Name,
            FailureReason = failureReason,
            InputContextSnapshot = new Dictionary<string, JsonElement>(execution.Context),
            RunId = runId,
            Attempt = execution.NodeResults.LastOrDefault(nr => nr.NodeId == node.Id)?.Attempt ?? 1
        };

        await store.CreateDeadLetterAsync(deadLetter, cancellationToken);
        logger.LogInformation("Created dead letter {DeadLetterId} for node '{NodeName}' in execution {ExecutionId}",
            deadLetter.Id, node.Name, execution.Id);
    }
}
