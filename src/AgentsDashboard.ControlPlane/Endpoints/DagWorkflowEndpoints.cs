using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Services;

namespace AgentsDashboard.ControlPlane.Endpoints;

public static class DagWorkflowEndpoints
{
    public static IEndpointRouteBuilder MapDagWorkflowApi(this IEndpointRouteBuilder app)
    {
        var readApi = app.MapGroup("/api").RequireRateLimiting("GlobalPolicy").DisableAntiforgery();
        var writeApi = app.MapGroup("/api").RequireRateLimiting("GlobalPolicy").DisableAntiforgery();
        var webhookApi = app.MapGroup("/api").RequireRateLimiting("WebhookPolicy").DisableAntiforgery();

        // --- Agents ---

        readApi.MapGet("/repositories/{repositoryId}/agents", async (string repositoryId, OrchestratorStore store, CancellationToken ct) =>
            Results.Ok(await store.ListAgentsByRepositoryAsync(repositoryId, ct)));

        readApi.MapGet("/agents/{agentId}", async (string agentId, OrchestratorStore store, CancellationToken ct) =>
        {
            var agent = await store.GetAgentAsync(agentId, ct);
            return agent is null ? Results.NotFound(new { message = "Agent not found" }) : Results.Ok(agent);
        });

        writeApi.MapPost("/agents", async (CreateAgentRequest request, OrchestratorStore store, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required"] });

            var repo = await store.GetRepositoryAsync(request.RepositoryId, ct);
            if (repo is null)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["repositoryId"] = ["Repository not found"] });

            var agent = new AgentDocument
            {
                RepositoryId = request.RepositoryId,
                Name = request.Name,
                Description = request.Description,
                Harness = request.Harness,
                Prompt = request.Prompt,
                Command = request.Command,
                AutoCreatePullRequest = request.AutoCreatePullRequest,
                Enabled = request.Enabled,
                RetryPolicy = request.RetryPolicy ?? new RetryPolicyConfig(),
                Timeouts = request.Timeouts ?? new TimeoutConfig(),
                SandboxProfile = request.SandboxProfile ?? new SandboxProfileConfig(),
                ArtifactPolicy = request.ArtifactPolicy ?? new ArtifactPolicyConfig(),
                ArtifactPatterns = request.ArtifactPatterns ?? [],
                InstructionFiles = request.InstructionFiles ?? []
            };

            return Results.Ok(await store.CreateAgentAsync(agent, ct));
        });

        writeApi.MapPut("/agents/{agentId}", async (string agentId, UpdateAgentRequest request, OrchestratorStore store, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required"] });

            var agent = new AgentDocument
            {
                Name = request.Name,
                Description = request.Description,
                Harness = request.Harness,
                Prompt = request.Prompt,
                Command = request.Command,
                AutoCreatePullRequest = request.AutoCreatePullRequest,
                Enabled = request.Enabled,
                RetryPolicy = request.RetryPolicy ?? new RetryPolicyConfig(),
                Timeouts = request.Timeouts ?? new TimeoutConfig(),
                SandboxProfile = request.SandboxProfile ?? new SandboxProfileConfig(),
                ArtifactPolicy = request.ArtifactPolicy ?? new ArtifactPolicyConfig(),
                ArtifactPatterns = request.ArtifactPatterns ?? [],
                InstructionFiles = request.InstructionFiles ?? []
            };

            var updated = await store.UpdateAgentAsync(agentId, agent, ct);
            return updated is null ? Results.NotFound(new { message = "Agent not found" }) : Results.Ok(updated);
        });

        writeApi.MapDelete("/agents/{agentId}", async (string agentId, OrchestratorStore store, CancellationToken ct) =>
        {
            var deleted = await store.DeleteAgentAsync(agentId, ct);
            return deleted ? Results.Ok(new { message = "Agent deleted" }) : Results.NotFound(new { message = "Agent not found" });
        });

        // --- Graph Workflows V2 ---

        readApi.MapGet("/workflows-v2", async (OrchestratorStore store, CancellationToken ct) =>
            Results.Ok(await store.ListAllWorkflowsV2Async(ct)));

        readApi.MapGet("/repositories/{repositoryId}/workflows-v2", async (string repositoryId, OrchestratorStore store, CancellationToken ct) =>
            Results.Ok(await store.ListWorkflowsV2ByRepositoryAsync(repositoryId, ct)));

        readApi.MapGet("/workflows-v2/{workflowId}", async (string workflowId, OrchestratorStore store, CancellationToken ct) =>
        {
            var workflow = await store.GetWorkflowV2Async(workflowId, ct);
            return workflow is null ? Results.NotFound(new { message = "Workflow not found" }) : Results.Ok(workflow);
        });

        writeApi.MapPost("/workflows-v2", async (CreateWorkflowV2Request request, OrchestratorStore store, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required"] });

            var repo = await store.GetRepositoryAsync(request.RepositoryId, ct);
            if (repo is null)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["repositoryId"] = ["Repository not found"] });

            var idMap = new Dictionary<string, string>();
            var nodes = new List<WorkflowNodeConfig>();

            foreach (var nr in request.Nodes)
            {
                var nodeId = Guid.NewGuid().ToString("N");
                idMap[nr.TempId] = nodeId;
                nodes.Add(new WorkflowNodeConfig
                {
                    Id = nodeId,
                    Name = nr.Name,
                    Type = nr.Type,
                    AgentId = nr.AgentId,
                    DelaySeconds = nr.DelaySeconds,
                    TimeoutMinutes = nr.TimeoutMinutes,
                    RetryPolicy = nr.RetryPolicy,
                    InputMappings = nr.InputMappings ?? [],
                    OutputMappings = nr.OutputMappings ?? [],
                    PositionX = nr.PositionX,
                    PositionY = nr.PositionY
                });
            }

            var edges = request.Edges.Select(er => new WorkflowEdgeConfig
            {
                SourceNodeId = idMap.GetValueOrDefault(er.SourceNodeTempId, er.SourceNodeTempId),
                TargetNodeId = idMap.GetValueOrDefault(er.TargetNodeTempId, er.TargetNodeTempId),
                Condition = er.Condition,
                Priority = er.Priority,
                Label = er.Label
            }).ToList();

            var trigger = request.Trigger is not null
                ? new WorkflowV2TriggerConfig
                {
                    Type = request.Trigger.Type,
                    CronExpression = request.Trigger.CronExpression,
                    WebhookEventFilter = request.Trigger.WebhookEventFilter
                }
                : new WorkflowV2TriggerConfig();

            var workflow = new WorkflowV2Document
            {
                RepositoryId = request.RepositoryId,
                Name = request.Name,
                Description = request.Description,
                Nodes = nodes,
                Edges = edges,
                Trigger = trigger,
                WebhookToken = Guid.NewGuid().ToString("N"),
                Enabled = request.Enabled,
                MaxConcurrentNodes = request.MaxConcurrentNodes
            };

            var validation = await WorkflowDagValidator.ValidateAsync(workflow, store, ct);
            if (!validation.IsValid)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["graph"] = validation.Errors.ToArray() });

            return Results.Ok(await store.CreateWorkflowV2Async(workflow, ct));
        });

        writeApi.MapPut("/workflows-v2/{workflowId}", async (string workflowId, UpdateWorkflowV2Request request, OrchestratorStore store, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required"] });

            var existing = await store.GetWorkflowV2Async(workflowId, ct);
            if (existing is null)
                return Results.NotFound(new { message = "Workflow not found" });

            var idMap = new Dictionary<string, string>();
            var nodes = new List<WorkflowNodeConfig>();

            foreach (var nr in request.Nodes)
            {
                var nodeId = Guid.NewGuid().ToString("N");
                idMap[nr.TempId] = nodeId;
                nodes.Add(new WorkflowNodeConfig
                {
                    Id = nodeId,
                    Name = nr.Name,
                    Type = nr.Type,
                    AgentId = nr.AgentId,
                    DelaySeconds = nr.DelaySeconds,
                    TimeoutMinutes = nr.TimeoutMinutes,
                    RetryPolicy = nr.RetryPolicy,
                    InputMappings = nr.InputMappings ?? [],
                    OutputMappings = nr.OutputMappings ?? [],
                    PositionX = nr.PositionX,
                    PositionY = nr.PositionY
                });
            }

            var edges = request.Edges.Select(er => new WorkflowEdgeConfig
            {
                SourceNodeId = idMap.GetValueOrDefault(er.SourceNodeTempId, er.SourceNodeTempId),
                TargetNodeId = idMap.GetValueOrDefault(er.TargetNodeTempId, er.TargetNodeTempId),
                Condition = er.Condition,
                Priority = er.Priority,
                Label = er.Label
            }).ToList();

            var trigger = request.Trigger is not null
                ? new WorkflowV2TriggerConfig
                {
                    Type = request.Trigger.Type,
                    CronExpression = request.Trigger.CronExpression,
                    WebhookEventFilter = request.Trigger.WebhookEventFilter
                }
                : existing.Trigger;

            var updated = new WorkflowV2Document
            {
                Name = request.Name,
                Description = request.Description,
                Nodes = nodes,
                Edges = edges,
                Trigger = trigger,
                Enabled = request.Enabled,
                MaxConcurrentNodes = request.MaxConcurrentNodes
            };

            var validation = await WorkflowDagValidator.ValidateAsync(
                new WorkflowV2Document
                {
                    Id = workflowId,
                    RepositoryId = existing.RepositoryId,
                    Nodes = nodes,
                    Edges = edges,
                    Trigger = trigger
                }, store, ct);

            if (!validation.IsValid)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["graph"] = validation.Errors.ToArray() });

            var result = await store.UpdateWorkflowV2Async(workflowId, updated, ct);
            return result is null ? Results.NotFound(new { message = "Workflow not found" }) : Results.Ok(result);
        });

        writeApi.MapDelete("/workflows-v2/{workflowId}", async (string workflowId, OrchestratorStore store, CancellationToken ct) =>
        {
            var deleted = await store.DeleteWorkflowV2Async(workflowId, ct);
            return deleted ? Results.Ok(new { message = "Workflow deleted" }) : Results.NotFound(new { message = "Workflow not found" });
        });

        // --- Executions V2 ---

        writeApi.MapPost("/workflows-v2/{workflowId}/execute", async (string workflowId, ExecuteWorkflowV2Request request, OrchestratorStore store, IWorkflowDagExecutor executor, CancellationToken ct) =>
        {
            var workflow = await store.GetWorkflowV2Async(workflowId, ct);
            if (workflow is null)
                return Results.NotFound(new { message = "Workflow not found" });

            var repo = await store.GetRepositoryAsync(workflow.RepositoryId, ct);
            if (repo is null)
                return Results.NotFound(new { message = "Repository not found" });

            var execution = await executor.ExecuteWorkflowAsync(workflow, repo.ProjectId, request.InitialContext, request.TriggeredBy, ct);
            return Results.Ok(execution);
        });

        readApi.MapGet("/workflows-v2/{workflowId}/executions", async (string workflowId, OrchestratorStore store, CancellationToken ct) =>
            Results.Ok(await store.ListExecutionsV2ByWorkflowAsync(workflowId, ct)));

        readApi.MapGet("/workflows-v2/executions/{executionId}", async (string executionId, OrchestratorStore store, CancellationToken ct) =>
        {
            var execution = await store.GetExecutionV2Async(executionId, ct);
            return execution is null ? Results.NotFound(new { message = "Execution not found" }) : Results.Ok(execution);
        });

        writeApi.MapPost("/workflows-v2/executions/{executionId}/cancel", async (string executionId, IWorkflowDagExecutor executor, CancellationToken ct) =>
        {
            var execution = await executor.CancelExecutionAsync(executionId, ct);
            return execution is null ? Results.NotFound(new { message = "Execution not found" }) : Results.Ok(execution);
        });

        writeApi.MapPost("/workflows-v2/executions/{executionId}/approve", async (string executionId, ApproveWorkflowV2NodeRequest request, IWorkflowDagExecutor executor, CancellationToken ct) =>
        {
            var execution = await executor.ApproveWorkflowNodeAsync(executionId, request.ApprovedBy, request.Approved, ct);
            return execution is null ? Results.NotFound(new { message = "Execution not found" }) : Results.Ok(execution);
        });

        // --- Dead Letters ---

        readApi.MapGet("/workflow-deadletters", async (OrchestratorStore store, CancellationToken ct) =>
            Results.Ok(await store.ListUnreplayedDeadLettersAsync(ct)));

        readApi.MapGet("/workflow-deadletters/{deadLetterId}", async (string deadLetterId, OrchestratorStore store, CancellationToken ct) =>
        {
            var dl = await store.GetDeadLetterAsync(deadLetterId, ct);
            return dl is null ? Results.NotFound(new { message = "Dead letter not found" }) : Results.Ok(dl);
        });

        writeApi.MapPost("/workflow-deadletters/{deadLetterId}/replay", async (string deadLetterId, ReplayDeadLetterRequest request, OrchestratorStore store, IWorkflowDagExecutor executor, CancellationToken ct) =>
        {
            var dl = await store.GetDeadLetterAsync(deadLetterId, ct);
            if (dl is null)
                return Results.NotFound(new { message = "Dead letter not found" });

            if (dl.Replayed)
                return Results.Conflict(new { message = "Dead letter has already been replayed" });

            var execution = await executor.ReplayFromDeadLetterAsync(dl, request.TriggeredBy, ct);
            return Results.Ok(execution);
        });

        // --- Webhook trigger for V2 workflows ---

        webhookApi.MapPost("/webhooks/workflows-v2/{workflowId}/{token}", async (string workflowId, string token, OrchestratorStore store, IWorkflowDagExecutor executor, CancellationToken ct) =>
        {
            var workflow = await store.GetWorkflowV2Async(workflowId, ct);
            if (workflow is null || workflow.WebhookToken != token)
                return Results.NotFound();

            if (!workflow.Enabled)
                return Results.BadRequest(new { message = "Workflow is disabled" });

            var repo = await store.GetRepositoryAsync(workflow.RepositoryId, ct);
            if (repo is null)
                return Results.NotFound(new { message = "Repository not found" });

            var execution = await executor.ExecuteWorkflowAsync(workflow, repo.ProjectId, null, "webhook", ct);
            return Results.Ok(new { executionId = execution.Id });
        });

        return app;
    }
}
