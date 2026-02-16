using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AgentsDashboard.ControlPlane.Endpoints;

public static class ApiEndpoints
{
    public static IEndpointRouteBuilder MapOrchestratorApi(this IEndpointRouteBuilder app)
    {
        var readApi = app.MapGroup("/api").RequireRateLimiting("GlobalPolicy").DisableAntiforgery();
        var writeApi = app.MapGroup("/api").RequireRateLimiting("GlobalPolicy").DisableAntiforgery();

        // --- Projects ---

        readApi.MapGet("/projects", async (OrchestratorStore store, CancellationToken ct) =>
            Results.Ok(await store.ListProjectsAsync(ct)));

        readApi.MapGet("/projects/{projectId}", async (string projectId, OrchestratorStore store, CancellationToken ct) =>
        {
            var project = await store.GetProjectAsync(projectId, ct);
            return project is null ? Results.NotFound(new { message = "Project not found" }) : Results.Ok(project);
        });

        writeApi.MapPost("/projects", async (CreateProjectRequest request, OrchestratorStore store, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required"] });

            return Results.Ok(await store.CreateProjectAsync(request, ct));
        });

        writeApi.MapPut("/projects/{projectId}", async (string projectId, UpdateProjectRequest request, OrchestratorStore store, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required"] });

            var project = await store.UpdateProjectAsync(projectId, request, ct);
            return project is null ? Results.NotFound(new { message = "Project not found" }) : Results.Ok(project);
        });

        writeApi.MapDelete("/projects/{projectId}", async (string projectId, OrchestratorStore store, CancellationToken ct) =>
        {
            var deleted = await store.DeleteProjectAsync(projectId, ct);
            return deleted ? Results.Ok(new { message = "Project deleted" }) : Results.NotFound(new { message = "Project not found" });
        });

        readApi.MapGet("/projects/{projectId}/repositories", async (string projectId, OrchestratorStore store, CancellationToken ct) =>
            Results.Ok(await store.ListRepositoriesAsync(projectId, ct)));

        // --- Repositories ---

        readApi.MapGet("/repositories/{repositoryId}", async (string repositoryId, OrchestratorStore store, CancellationToken ct) =>
        {
            var repository = await store.GetRepositoryAsync(repositoryId, ct);
            return repository is null ? Results.NotFound(new { message = "Repository not found" }) : Results.Ok(repository);
        });

        writeApi.MapPost("/repositories", async (CreateRepositoryRequest request, OrchestratorStore store, CancellationToken ct) =>
        {
            var project = await store.GetProjectAsync(request.ProjectId, ct);
            if (project is null)
                return Results.NotFound(new { message = "Project not found" });

            return Results.Ok(await store.CreateRepositoryAsync(request, ct));
        });

        writeApi.MapPut("/repositories/{repositoryId}", async (string repositoryId, UpdateRepositoryRequest request, OrchestratorStore store, CancellationToken ct) =>
        {
            var repository = await store.UpdateRepositoryAsync(repositoryId, request, ct);
            return repository is null ? Results.NotFound(new { message = "Repository not found" }) : Results.Ok(repository);
        });

        writeApi.MapDelete("/repositories/{repositoryId}", async (string repositoryId, OrchestratorStore store, CancellationToken ct) =>
        {
            var deleted = await store.DeleteRepositoryAsync(repositoryId, ct);
            return deleted ? Results.Ok(new { message = "Repository deleted" }) : Results.NotFound(new { message = "Repository not found" });
        });

        // --- Repository Instructions ---

        readApi.MapGet("/repositories/{repositoryId}/instructions", async (string repositoryId, OrchestratorStore store, CancellationToken ct) =>
            Results.Ok(await store.GetRepositoryInstructionFilesAsync(repositoryId, ct)));

        writeApi.MapPut("/repositories/{repositoryId}/instructions", async (string repositoryId, UpdateRepositoryInstructionsRequest request, OrchestratorStore store, CancellationToken ct) =>
        {
            var repo = await store.UpdateRepositoryInstructionFilesAsync(repositoryId, request.InstructionFiles, ct);
            return repo is null ? Results.NotFound(new { message = "Repository not found" }) : Results.Ok(repo.InstructionFiles);
        });

        readApi.MapGet("/repositories/{repositoryId}/instructions/{instructionId}", async (string repositoryId, string instructionId, OrchestratorStore store, CancellationToken ct) =>
        {
            var instruction = await store.GetInstructionAsync(instructionId, ct);
            if (instruction is null || instruction.RepositoryId != repositoryId)
                return Results.NotFound(new { message = "Instruction not found" });
            return Results.Ok(instruction);
        });

        writeApi.MapPut("/repositories/{repositoryId}/instructions/{instructionId}", async (
            string repositoryId,
            string instructionId,
            UpdateRepositoryInstructionRequest request,
            OrchestratorStore store,
            CancellationToken ct) =>
        {
            var createRequest = new CreateRepositoryInstructionRequest(request.Name, request.Content, request.Priority, request.Enabled);
            var instruction = await store.UpsertInstructionAsync(repositoryId, instructionId, createRequest, ct);
            return Results.Ok(instruction);
        });

        writeApi.MapDelete("/repositories/{repositoryId}/instructions/{instructionId}", async (
            string repositoryId,
            string instructionId,
            OrchestratorStore store,
            CancellationToken ct) =>
        {
            var instruction = await store.GetInstructionAsync(instructionId, ct);
            if (instruction is null || instruction.RepositoryId != repositoryId)
                return Results.NotFound(new { message = "Instruction not found" });

            var deleted = await store.DeleteInstructionAsync(instructionId, ct);
            return deleted ? Results.Ok(new { message = "Instruction deleted" }) : Results.NotFound(new { message = "Instruction not found" });
        });

        // --- Tasks ---

        readApi.MapGet("/repositories/{repositoryId}/tasks", async (string repositoryId, OrchestratorStore store, CancellationToken ct) =>
            Results.Ok(await store.ListTasksAsync(repositoryId, ct)));

        readApi.MapGet("/tasks/{taskId}", async (string taskId, OrchestratorStore store, CancellationToken ct) =>
        {
            var task = await store.GetTaskAsync(taskId, ct);
            return task is null ? Results.NotFound(new { message = "Task not found" }) : Results.Ok(task);
        });

        writeApi.MapPost("/tasks", async (CreateTaskRequest request, OrchestratorStore store, CancellationToken ct) =>
        {
            var repository = await store.GetRepositoryAsync(request.RepositoryId, ct);
            if (repository is null)
                return Results.NotFound(new { message = "Repository not found" });

            if (request.Kind == TaskKind.Cron && string.IsNullOrWhiteSpace(request.CronExpression))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["cronExpression"] = ["Cron expression required for cron tasks"] });

            return Results.Ok(await store.CreateTaskAsync(request, ct));
        });

        writeApi.MapPut("/tasks/{taskId}", async (string taskId, UpdateTaskRequest request, OrchestratorStore store, CancellationToken ct) =>
        {
            if (request.Kind == TaskKind.Cron && string.IsNullOrWhiteSpace(request.CronExpression))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["cronExpression"] = ["Cron expression required for cron tasks"] });

            var task = await store.UpdateTaskAsync(taskId, request, ct);
            return task is null ? Results.NotFound(new { message = "Task not found" }) : Results.Ok(task);
        });

        writeApi.MapDelete("/tasks/{taskId}", async (string taskId, OrchestratorStore store, CancellationToken ct) =>
        {
            var deleted = await store.DeleteTaskAsync(taskId, ct);
            return deleted ? Results.Ok(new { message = "Task deleted" }) : Results.NotFound(new { message = "Task not found" });
        });

        // --- Runs ---

        readApi.MapGet("/runs", async (OrchestratorStore store, CancellationToken ct) =>
            Results.Ok(await store.ListRecentRunsAsync(ct)));

        readApi.MapGet("/runs/{runId}", async (string runId, OrchestratorStore store, CancellationToken ct) =>
        {
            var run = await store.GetRunAsync(runId, ct);
            return run is null ? Results.NotFound(new { message = "Run not found" }) : Results.Ok(run);
        });

        readApi.MapGet("/repositories/{repositoryId}/runs", async (string repositoryId, OrchestratorStore store, CancellationToken ct) =>
            Results.Ok(await store.ListRunsByRepositoryAsync(repositoryId, ct)));

        readApi.MapGet("/runs/{runId}/logs", async (string runId, OrchestratorStore store, CancellationToken ct) =>
            Results.Ok(await store.ListRunLogsAsync(runId, ct)));

        writeApi.MapPost("/runs", async (CreateRunRequest request, OrchestratorStore store, RunDispatcher dispatcher, CancellationToken ct) =>
        {
            var task = await store.GetTaskAsync(request.TaskId, ct);
            if (task is null)
                return Results.NotFound(new { message = "Task not found" });

            var repository = await store.GetRepositoryAsync(task.RepositoryId, ct);
            if (repository is null)
                return Results.NotFound(new { message = "Repository not found" });

            var project = await store.GetProjectAsync(repository.ProjectId, ct);
            if (project is null)
                return Results.NotFound(new { message = "Project not found" });

            var run = await store.CreateRunAsync(task, project.Id, ct);
            await dispatcher.DispatchAsync(project, repository, task, run, ct);

            return Results.Ok(run);
        });

        writeApi.MapPost("/runs/{runId}/cancel", async (string runId, OrchestratorStore store, RunDispatcher dispatcher, CancellationToken ct) =>
        {
            var run = await store.GetRunAsync(runId, ct);
            if (run is null)
                return Results.NotFound(new { message = "Run not found" });

            if (run.State is not (RunState.Queued or RunState.Running or RunState.PendingApproval))
                return Results.BadRequest(new { message = "Run is not in a cancellable state" });

            await dispatcher.CancelAsync(runId, ct);
            var cancelled = await store.MarkRunCancelledAsync(runId, ct);
            return cancelled is null ? Results.NotFound(new { message = "Run not found or already completed" }) : Results.Ok(cancelled);
        });

        writeApi.MapPost("/runs/{runId}/retry", async (string runId, OrchestratorStore store, RunDispatcher dispatcher, CancellationToken ct) =>
        {
            var originalRun = await store.GetRunAsync(runId, ct);
            if (originalRun is null)
                return Results.NotFound(new { message = "Run not found" });

            var task = await store.GetTaskAsync(originalRun.TaskId, ct);
            if (task is null)
                return Results.NotFound(new { message = "Task not found" });

            var repository = await store.GetRepositoryAsync(task.RepositoryId, ct);
            if (repository is null)
                return Results.NotFound(new { message = "Repository not found" });

            var project = await store.GetProjectAsync(repository.ProjectId, ct);
            if (project is null)
                return Results.NotFound(new { message = "Project not found" });

            var retryRun = await store.CreateRunAsync(task, project.Id, ct, originalRun.Attempt + 1);
            await dispatcher.DispatchAsync(project, repository, task, retryRun, ct);

            return Results.Ok(retryRun);
        });

        writeApi.MapPost("/runs/{runId}/approve", async (string runId, OrchestratorStore store, RunDispatcher dispatcher, CancellationToken ct) =>
        {
            var run = await store.GetRunAsync(runId, ct);
            if (run is null)
                return Results.NotFound(new { message = "Run not found" });

            if (run.State != RunState.PendingApproval)
                return Results.BadRequest(new { message = "Run is not pending approval" });

            var approved = await store.ApproveRunAsync(runId, ct);
            if (approved is null)
                return Results.NotFound(new { message = "Run not found or already processed" });

            var task = await store.GetTaskAsync(approved.TaskId, ct);
            if (task is null)
                return Results.NotFound(new { message = "Task not found" });

            var repository = await store.GetRepositoryAsync(task.RepositoryId, ct);
            if (repository is null)
                return Results.NotFound(new { message = "Repository not found" });

            var project = await store.GetProjectAsync(repository.ProjectId, ct);
            if (project is null)
                return Results.NotFound(new { message = "Project not found" });

            await dispatcher.DispatchAsync(project, repository, task, approved, ct);

            return Results.Ok(approved);
        });

        writeApi.MapPost("/runs/{runId}/reject", async (string runId, OrchestratorStore store, CancellationToken ct) =>
        {
            var run = await store.GetRunAsync(runId, ct);
            if (run is null)
                return Results.NotFound(new { message = "Run not found" });

            if (run.State != RunState.PendingApproval)
                return Results.BadRequest(new { message = "Run is not pending approval" });

            var rejected = await store.RejectRunAsync(runId, ct);
            return rejected is null ? Results.NotFound(new { message = "Run not found or already processed" }) : Results.Ok(rejected);
        });

        // --- Artifacts ---

        readApi.MapGet("/runs/{runId}/artifacts", async (string runId, OrchestratorStore store, CancellationToken ct) =>
        {
            var artifacts = await store.ListArtifactsAsync(runId, ct);
            return Results.Ok(artifacts);
        });

        readApi.MapGet("/runs/{runId}/artifacts/{fileName}", async (string runId, string fileName, OrchestratorStore store, CancellationToken ct) =>
        {
            var fileStream = await store.GetArtifactAsync(runId, fileName, ct);
            if (fileStream is null)
                return Results.NotFound(new { message = "Artifact not found" });

            return Results.File(fileStream, "application/octet-stream", fileName);
        });

        // --- Findings ---

        readApi.MapGet("/findings", async (OrchestratorStore store, CancellationToken ct) =>
            Results.Ok(await store.ListAllFindingsAsync(ct)));

        readApi.MapGet("/findings/{findingId}", async (string findingId, OrchestratorStore store, CancellationToken ct) =>
        {
            var finding = await store.GetFindingAsync(findingId, ct);
            return finding is null ? Results.NotFound(new { message = "Finding not found" }) : Results.Ok(finding);
        });

        readApi.MapGet("/repositories/{repositoryId}/findings", async (string repositoryId, OrchestratorStore store, CancellationToken ct) =>
            Results.Ok(await store.ListFindingsAsync(repositoryId, ct)));

        writeApi.MapPatch("/findings/{findingId}", async (string findingId, UpdateFindingStateRequest request, OrchestratorStore store, CancellationToken ct) =>
        {
            var finding = await store.UpdateFindingStateAsync(findingId, request.State, ct);
            return finding is null ? Results.NotFound(new { message = "Finding not found" }) : Results.Ok(finding);
        });

        writeApi.MapPost("/findings/{findingId}/retry", async (string findingId, OrchestratorStore store, RunDispatcher dispatcher, CancellationToken ct) =>
        {
            var finding = await store.GetFindingAsync(findingId, ct);
            if (finding is null)
                return Results.NotFound(new { message = "Finding not found" });

            var run = await store.GetRunAsync(finding.RunId, ct);
            if (run is null)
                return Results.NotFound(new { message = "Original run not found" });

            var task = await store.GetTaskAsync(run.TaskId, ct);
            if (task is null)
                return Results.NotFound(new { message = "Task not found" });

            var repository = await store.GetRepositoryAsync(task.RepositoryId, ct);
            if (repository is null)
                return Results.NotFound(new { message = "Repository not found" });

            var project = await store.GetProjectAsync(repository.ProjectId, ct);
            if (project is null)
                return Results.NotFound(new { message = "Project not found" });

            var retryRun = await store.CreateRunAsync(task, project.Id, ct);
            await dispatcher.DispatchAsync(project, repository, task, retryRun, ct);
            await store.UpdateFindingStateAsync(findingId, FindingState.InProgress, ct);

            return Results.Ok(retryRun);
        });

        writeApi.MapDelete("/findings/{findingId}", async (string findingId, OrchestratorStore store, CancellationToken ct) =>
        {
            var deleted = await store.DeleteFindingAsync(findingId, ct);
            return deleted ? Results.Ok(new { message = "Finding deleted" }) : Results.NotFound(new { message = "Finding not found" });
        });

        // --- Workers ---

        readApi.MapGet("/workers", async (OrchestratorStore store, CancellationToken ct) =>
            Results.Ok(await store.ListWorkersAsync(ct)));

        readApi.MapGet("/workers/registered", (IWorkerRegistryService workerRegistry) =>
        {
            var workers = workerRegistry.GetRegisteredWorkers();
            var result = workers.Select(w => new
            {
                w.WorkerId,
                w.HostName,
                w.ActiveSlots,
                w.MaxSlots,
                w.LastHeartbeat
            });
            return Results.Ok(result);
        });

        readApi.MapGet("/workers/count", (IWorkerRegistryService workerRegistry) =>
        {
            var count = workerRegistry.GetRegisteredWorkerCount();
            return Results.Ok(new { count });
        });

        writeApi.MapPost("/workers/status-request", async (IWorkerRegistryService workerRegistry) =>
        {
            var request = new StatusRequestMessage
            {
                RequestId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            await workerRegistry.BroadcastStatusRequestAsync(request);

            var workerCount = workerRegistry.GetRegisteredWorkerCount();
            return Results.Ok(new
            {
                requestId = request.RequestId,
                timestamp = request.Timestamp,
                workerCount
            });
        });

        app.MapPost("/api/workers/heartbeat", async (
            WorkerHeartbeatRequest request,
            OrchestratorStore store,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.WorkerId))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["workerId"] = ["WorkerId is required"] });

            await store.UpsertWorkerHeartbeatAsync(
                request.WorkerId,
                request.Endpoint ?? string.Empty,
                request.ActiveSlots,
                request.MaxSlots,
                ct);

            return Results.Ok(new { acknowledged = true });
        }).AllowAnonymous();

        // --- Schedules ---

        readApi.MapGet("/schedules", async (OrchestratorStore store, CancellationToken ct) =>
            Results.Ok(await store.ListScheduledTasksAsync(ct)));

        // --- Secrets ---

        readApi.MapGet("/repositories/{repositoryId}/secrets", async (string repositoryId, OrchestratorStore store, CancellationToken ct) =>
        {
            var secrets = await store.ListProviderSecretsAsync(repositoryId, ct);
            var summary = secrets.Select(x => new { x.Provider, x.UpdatedAtUtc });
            return Results.Ok(summary);
        });

        writeApi.MapPut("/repositories/{repositoryId}/secrets/{provider}", async (
            string repositoryId,
            string provider,
            SetProviderSecretRequest request,
            OrchestratorStore store,
            SecretCryptoService crypto,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.SecretValue))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["secretValue"] = ["Secret value is required"] });

            var encrypted = crypto.Encrypt(request.SecretValue.Trim());
            await store.UpsertProviderSecretAsync(repositoryId, provider.Trim().ToLowerInvariant(), encrypted, ct);

            return Results.Ok(new { message = "Secret saved" });
        });

        writeApi.MapPost("/repositories/{repositoryId}/secrets/{provider}/test", async (
            string repositoryId,
            string provider,
            OrchestratorStore store,
            SecretCryptoService crypto,
            CredentialValidationService validator,
            CancellationToken ct) =>
        {
            var secret = await store.GetProviderSecretAsync(repositoryId, provider.Trim().ToLowerInvariant(), ct);
            if (secret is null)
                return Results.NotFound(new { message = "Secret not found" });

            var decrypted = crypto.Decrypt(secret.EncryptedValue);
            var (success, message) = await validator.ValidateAsync(provider, decrypted, ct);
            return Results.Ok(new { success, message });
        });

        writeApi.MapDelete("/repositories/{repositoryId}/secrets/{provider}", async (
            string repositoryId,
            string provider,
            OrchestratorStore store,
            CancellationToken ct) =>
        {
            var deleted = await store.DeleteProviderSecretAsync(repositoryId, provider.Trim().ToLowerInvariant(), ct);
            return deleted ? Results.Ok(new { message = "Secret deleted" }) : Results.NotFound(new { message = "Secret not found" });
        });

        // --- Harness Provider Settings ---

        readApi.MapGet("/repositories/{repositoryId}/providers/{harness}", async (
            string repositoryId,
            string harness,
            OrchestratorStore store,
            CancellationToken ct) =>
        {
            var settings = await store.GetHarnessProviderSettingsAsync(repositoryId, harness.Trim().ToLowerInvariant(), ct);
            return settings is null ? Results.Ok(new { message = "No settings found" }) : Results.Ok(settings);
        });

        writeApi.MapPut("/repositories/{repositoryId}/providers/{harness}", async (
            string repositoryId,
            string harness,
            UpdateHarnessProviderSettingsRequest request,
            OrchestratorStore store,
            CancellationToken ct) =>
        {
            var repo = await store.GetRepositoryAsync(repositoryId, ct);
            if (repo is null)
                return Results.NotFound(new { message = "Repository not found" });

            if (request.Temperature < 0 || request.Temperature > 2)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["temperature"] = ["Temperature must be between 0 and 2"] });

            if (request.MaxTokens < 1)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["maxTokens"] = ["MaxTokens must be at least 1"] });

            var settings = await store.UpsertHarnessProviderSettingsAsync(
                repositoryId,
                harness.Trim().ToLowerInvariant(),
                request.Model,
                request.Temperature,
                request.MaxTokens,
                request.AdditionalSettings,
                ct);

            return Results.Ok(settings);
        });

        // --- Webhooks ---

        readApi.MapGet("/webhooks/{webhookId}", async (string webhookId, OrchestratorStore store, CancellationToken ct) =>
        {
            var webhook = await store.GetWebhookAsync(webhookId, ct);
            return webhook is null ? Results.NotFound(new { message = "Webhook not found" }) : Results.Ok(webhook);
        });

        readApi.MapGet("/repositories/{repositoryId}/webhooks", async (
            string repositoryId,
            OrchestratorStore store,
            CancellationToken ct) =>
        {
            var webhooks = await store.ListWebhooksAsync(repositoryId, ct);
            return Results.Ok(webhooks);
        });

        writeApi.MapPost("/webhooks", async (CreateWebhookRequest request, OrchestratorStore store, CancellationToken ct) =>
        {
            var repo = await store.GetRepositoryAsync(request.RepositoryId, ct);
            if (repo is null)
                return Results.NotFound(new { message = "Repository not found" });

            return Results.Ok(await store.CreateWebhookAsync(request, ct));
        });

        writeApi.MapPut("/webhooks/{webhookId}", async (string webhookId, UpdateWebhookRequest request, OrchestratorStore store, CancellationToken ct) =>
        {
            var webhook = await store.UpdateWebhookAsync(webhookId, request, ct);
            return webhook is null ? Results.NotFound(new { message = "Webhook not found" }) : Results.Ok(webhook);
        });

        writeApi.MapDelete("/webhooks/{webhookId}", async (string webhookId, OrchestratorStore store, CancellationToken ct) =>
        {
            var deleted = await store.DeleteWebhookAsync(webhookId, ct);
            return deleted ? Results.Ok(new { message = "Webhook deleted" }) : Results.NotFound(new { message = "Webhook not found" });
        });

        app.MapPost("/api/webhooks/{repositoryId}/{eventType?}", async (
            string repositoryId,
            string? eventType,
            HttpContext httpContext,
            IOrchestratorStore store,
            RunDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var repository = await store.GetRepositoryAsync(repositoryId, ct);
            if (repository is null)
                return Results.NotFound(new { message = "Repository not found" });

            var project = await store.GetProjectAsync(repository.ProjectId, ct);
            if (project is null)
                return Results.NotFound(new { message = "Project not found" });

            var webhooks = await store.ListWebhooksAsync(repositoryId, ct);
            var matchedTaskIds = webhooks
                .Where(w => w.Enabled && (w.EventFilter == "*" || w.EventFilter == eventType || string.IsNullOrEmpty(eventType)))
                .Select(w => w.TaskId)
                .ToHashSet();

            var tasks = await store.ListEventDrivenTasksAsync(repositoryId, ct);
            var filteredTasks = tasks.Where(t => matchedTaskIds.Contains(t.Id)).ToList();

            if (filteredTasks.Count == 0)
                return Results.Ok(new { message = "No matching event-driven tasks for event type", eventType, dispatched = 0 });

            var dispatched = 0;
            foreach (var task in filteredTasks)
            {
                var run = await store.CreateRunAsync(task, project.Id, ct);
                if (await dispatcher.DispatchAsync(project, repository, task, run, ct))
                    dispatched++;
            }

            return Results.Ok(new { dispatched, eventType });
        }).AllowAnonymous().DisableAntiforgery().RequireRateLimiting("WebhookPolicy");

        // --- Finding Assignment & Task Creation ---

        writeApi.MapPut("/findings/{findingId}/assign", async (
            string findingId,
            AssignFindingRequest request,
            OrchestratorStore store,
            CancellationToken ct) =>
        {
            var finding = await store.AssignFindingAsync(findingId, request.AssignedTo, ct);
            return finding is null ? Results.NotFound(new { message = "Finding not found" }) : Results.Ok(finding);
        });

        writeApi.MapPost("/findings/{findingId}/create-task", async (
            string findingId,
            CreateTaskFromFindingRequest request,
            OrchestratorStore store,
            CancellationToken ct) =>
        {
            var finding = await store.GetFindingAsync(findingId, ct);
            if (finding is null)
                return Results.NotFound(new { message = "Finding not found" });

            var run = await store.GetRunAsync(finding.RunId, ct);
            if (run is null)
                return Results.NotFound(new { message = "Run not found" });

            var task = await store.GetTaskAsync(run.TaskId, ct);
            if (task is null)
                return Results.NotFound(new { message = "Task not found" });

            var linkedFailureRuns = request.LinkedFailureRuns ?? [finding.RunId];

            var createTaskRequest = new CreateTaskRequest(
                RepositoryId: task.RepositoryId,
                Name: request.Name,
                Harness: request.Harness,
                Command: request.Command,
                Prompt: request.Prompt,
                Kind: TaskKind.OneShot,
                Enabled: true,
                CronExpression: string.Empty,
                AutoCreatePullRequest: false,
                LinkedFailureRuns: linkedFailureRuns
            );

            return Results.Ok(await store.CreateTaskAsync(createTaskRequest, ct));
        });

        // --- System Settings ---

        readApi.MapGet("/settings", async (OrchestratorStore store, CancellationToken ct) =>
            Results.Ok(await store.GetSettingsAsync(ct)));

        writeApi.MapPut("/settings", async (
            UpdateSystemSettingsRequest request,
            OrchestratorStore store,
            CancellationToken ct) =>
        {
            var settings = await store.GetSettingsAsync(ct);

            var updated = new SystemSettingsDocument
            {
                Id = settings.Id,
                DockerAllowedImages = request.DockerAllowedImages ?? settings.DockerAllowedImages,
                RetentionDaysLogs = request.RetentionDaysLogs ?? settings.RetentionDaysLogs,
                RetentionDaysRuns = request.RetentionDaysRuns ?? settings.RetentionDaysRuns,
                VictoriaMetricsEndpoint = request.VictoriaMetricsEndpoint ?? settings.VictoriaMetricsEndpoint,
                VmUiEndpoint = request.VmUiEndpoint ?? settings.VmUiEndpoint,
                UpdatedAtUtc = DateTime.UtcNow
            };

            await store.UpdateSettingsAsync(updated, ct);
            return Results.Ok(updated);
        });

        // --- Workflows ---

        readApi.MapGet("/workflows", async (OrchestratorStore store, CancellationToken ct) =>
            Results.Ok(await store.ListAllWorkflowsAsync(ct)));

        readApi.MapGet("/repositories/{repositoryId}/workflows", async (string repositoryId, OrchestratorStore store, CancellationToken ct) =>
            Results.Ok(await store.ListWorkflowsByRepositoryAsync(repositoryId, ct)));

        readApi.MapGet("/workflows/{workflowId}", async (string workflowId, OrchestratorStore store, CancellationToken ct) =>
        {
            var workflow = await store.GetWorkflowAsync(workflowId, ct);
            return workflow is null ? Results.NotFound(new { message = "Workflow not found" }) : Results.Ok(workflow);
        });

        writeApi.MapPost("/workflows", async (CreateWorkflowRequest request, OrchestratorStore store, CancellationToken ct) =>
        {
            var repository = await store.GetRepositoryAsync(request.RepositoryId, ct);
            if (repository is null)
                return Results.NotFound(new { message = "Repository not found" });

            var workflow = new WorkflowDocument
            {
                Id = Guid.NewGuid().ToString("N"),
                RepositoryId = request.RepositoryId,
                Name = request.Name,
                Description = request.Description,
                Stages = request.Stages.Select(s => new WorkflowStageConfig
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = s.Name,
                    Type = s.Type,
                    TaskId = s.TaskId,
                    DelaySeconds = s.DelaySeconds,
                    ParallelStageIds = s.ParallelStageIds,
                    TimeoutMinutes = s.TimeoutMinutes,
                    Order = s.Order
                }).ToList(),
                Enabled = true,
                CreatedAtUtc = DateTime.UtcNow
            };

            return Results.Ok(await store.CreateWorkflowAsync(workflow, ct));
        });

        writeApi.MapPut("/workflows/{workflowId}", async (
            string workflowId,
            UpdateWorkflowRequest request,
            OrchestratorStore store,
            CancellationToken ct) =>
        {
            var workflow = await store.GetWorkflowAsync(workflowId, ct);
            if (workflow is null)
                return Results.NotFound(new { message = "Workflow not found" });

            workflow.Name = request.Name;
            workflow.Description = request.Description;
            workflow.Stages = request.Stages.Select(s => new WorkflowStageConfig
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = s.Name,
                Type = s.Type,
                TaskId = s.TaskId,
                DelaySeconds = s.DelaySeconds,
                ParallelStageIds = s.ParallelStageIds,
                TimeoutMinutes = s.TimeoutMinutes,
                Order = s.Order
            }).ToList();
            workflow.Enabled = request.Enabled;

            await store.UpdateWorkflowAsync(workflow.Id, workflow, ct);
            return Results.Ok(workflow);
        });

        writeApi.MapDelete("/workflows/{workflowId}", async (string workflowId, OrchestratorStore store, CancellationToken ct) =>
        {
            await store.DeleteWorkflowAsync(workflowId, ct);
            return Results.Ok(new { message = "Workflow deleted" });
        });

        // --- Workflow Executions ---

        writeApi.MapPost("/workflows/{workflowId}/execute", async (
            string workflowId,
            OrchestratorStore store,
            WorkflowExecutor executor,
            CancellationToken ct) =>
        {
            var workflow = await store.GetWorkflowAsync(workflowId, ct);
            if (workflow is null)
                return Results.NotFound(new { message = "Workflow not found" });

            if (!workflow.Enabled)
                return Results.BadRequest(new { message = "Workflow is disabled" });

            var repository = await store.GetRepositoryAsync(workflow.RepositoryId, ct);
            if (repository is null)
                return Results.NotFound(new { message = "Repository not found" });

            var project = await store.GetProjectAsync(repository.ProjectId, ct);
            if (project is null)
                return Results.NotFound(new { message = "Project not found" });

            var execution = await executor.ExecuteWorkflowAsync(workflow, project.Id, ct);
            return Results.Ok(execution);
        });

        readApi.MapGet("/workflows/{workflowId}/executions", async (
            string workflowId,
            OrchestratorStore store,
            CancellationToken ct) =>
        {
            var executions = await store.ListWorkflowExecutionsAsync(workflowId, ct);
            return Results.Ok(executions);
        });

        readApi.MapGet("/workflows/{workflowId}/executions/{executionId}", async (
            string workflowId,
            string executionId,
            OrchestratorStore store,
            CancellationToken ct) =>
        {
            var execution = await store.GetWorkflowExecutionAsync(executionId, ct);
            if (execution is null)
                return Results.NotFound(new { message = "Execution not found" });

            if (execution.WorkflowId != workflowId)
                return Results.BadRequest(new { message = "Execution does not belong to this workflow" });

            return Results.Ok(execution);
        });

        writeApi.MapPost("/workflows/{workflowId}/executions/{executionId}/approve", async (
            string workflowId,
            string executionId,
            ApproveWorkflowStageRequest request,
            OrchestratorStore store,
            WorkflowExecutor executor,
            CancellationToken ct) =>
        {
            var execution = await store.GetWorkflowExecutionAsync(executionId, ct);
            if (execution is null)
                return Results.NotFound(new { message = "Execution not found" });

            if (execution.WorkflowId != workflowId)
                return Results.BadRequest(new { message = "Execution does not belong to this workflow" });

            if (execution.State != WorkflowExecutionState.PendingApproval)
                return Results.BadRequest(new { message = "Execution is not pending approval" });

            var approved = await executor.ApproveWorkflowStageAsync(executionId, request.ApprovedBy, ct);
            if (approved is null)
                return Results.NotFound(new { message = "Failed to approve execution" });

            return Results.Ok(approved);
        });

        // --- Images ---

        readApi.MapGet("/images", async (ImageBuilderService imageBuilder, string? filter, CancellationToken ct) =>
        {
            var images = await imageBuilder.ListImagesAsync(filter, ct);
            return Results.Ok(images);
        });

        writeApi.MapPost("/images/build", async (
            BuildImageRequest request,
            ImageBuilderService imageBuilder,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.DockerfileContent))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["dockerfileContent"] = ["Dockerfile content is required"] });

            if (string.IsNullOrWhiteSpace(request.Tag))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["tag"] = ["Image tag is required"] });

            var result = await imageBuilder.BuildImageAsync(
                request.DockerfileContent,
                request.Tag,
                onLogLine: null,
                ct);

            return result.Success
                ? Results.Ok(result)
                : Results.BadRequest(result);
        });

        writeApi.MapDelete("/images/{tag}", async (string tag, ImageBuilderService imageBuilder, CancellationToken ct) =>
        {
            var deleted = await imageBuilder.DeleteImageAsync(tag, ct);
            return deleted
                ? Results.Ok(new { message = "Image deleted" })
                : Results.BadRequest(new { message = "Failed to delete image" });
        });

        // --- Alert Rules ---

        readApi.MapGet("/alerts/rules", async (OrchestratorStore store, CancellationToken ct) =>
            Results.Ok(await store.ListAlertRulesAsync(ct)));

        readApi.MapGet("/alerts/rules/{ruleId}", async (string ruleId, OrchestratorStore store, CancellationToken ct) =>
        {
            var rule = await store.GetAlertRuleAsync(ruleId, ct);
            return rule is null ? Results.NotFound(new { message = "Alert rule not found" }) : Results.Ok(rule);
        });

        writeApi.MapPost("/alerts/rules", async (CreateAlertRuleRequest request, OrchestratorStore store, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required"] });

            if (request.Threshold <= 0)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["threshold"] = ["Threshold must be greater than 0"] });

            var rule = new AlertRuleDocument
            {
                Name = request.Name,
                RuleType = request.RuleType,
                Threshold = request.Threshold,
                WindowMinutes = request.WindowMinutes > 0 ? request.WindowMinutes : 10,
                CooldownMinutes = request.CooldownMinutes > 0 ? request.CooldownMinutes : 15,
                WebhookUrl = request.WebhookUrl ?? string.Empty,
                Enabled = request.Enabled
            };

            return Results.Ok(await store.CreateAlertRuleAsync(rule, ct));
        });

        writeApi.MapPut("/alerts/rules/{ruleId}", async (
            string ruleId,
            UpdateAlertRuleRequest request,
            OrchestratorStore store,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required"] });

            if (request.Threshold <= 0)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["threshold"] = ["Threshold must be greater than 0"] });

            var rule = new AlertRuleDocument
            {
                Id = ruleId,
                Name = request.Name,
                RuleType = request.RuleType,
                Threshold = request.Threshold,
                WindowMinutes = request.WindowMinutes > 0 ? request.WindowMinutes : 10,
                CooldownMinutes = request.CooldownMinutes > 0 ? request.CooldownMinutes : 15,
                WebhookUrl = request.WebhookUrl ?? string.Empty,
                Enabled = request.Enabled,
                CreatedAtUtc = DateTime.UtcNow
            };

            var updated = await store.UpdateAlertRuleAsync(ruleId, rule, ct);
            return updated is null ? Results.NotFound(new { message = "Alert rule not found" }) : Results.Ok(updated);
        });

        writeApi.MapDelete("/alerts/rules/{ruleId}", async (string ruleId, OrchestratorStore store, CancellationToken ct) =>
        {
            var deleted = await store.DeleteAlertRuleAsync(ruleId, ct);
            return deleted ? Results.Ok(new { message = "Alert rule deleted" }) : Results.NotFound(new { message = "Alert rule not found" });
        });

        // --- Alert Events ---

        readApi.MapGet("/alerts/events", async (OrchestratorStore store, CancellationToken ct) =>
            Results.Ok(await store.ListRecentAlertEventsAsync(100, ct)));

        readApi.MapGet("/alerts/events/{eventId}", async (string eventId, OrchestratorStore store, CancellationToken ct) =>
        {
            var alertEvent = await store.GetAlertEventAsync(eventId, ct);
            return alertEvent is null ? Results.NotFound(new { message = "Alert event not found" }) : Results.Ok(alertEvent);
        });

        writeApi.MapPost("/alerts/events/{eventId}/resolve", async (
            string eventId,
            OrchestratorStore store,
            CancellationToken ct) =>
        {
            var resolved = await store.ResolveAlertEventAsync(eventId, ct);
            return resolved is null
                ? Results.NotFound(new { message = "Alert event not found" })
                : Results.Ok(resolved);
        });

        writeApi.MapPost("/alerts/events/bulk-resolve", async (
            BulkResolveAlertsRequest request,
            OrchestratorStore store,
            CancellationToken ct) =>
        {
            if (request.EventIds.Count == 0)
                return Results.BadRequest(new { message = "No event IDs provided" });

            var count = await store.ResolveAlertEventsAsync(request.EventIds, ct);
            return Results.Ok(new BulkOperationResult(count, []));
        });

        // --- Bulk Operations ---

        writeApi.MapPost("/runs/bulk-cancel", async (
            BulkCancelRunsRequest request,
            OrchestratorStore store,
            RunDispatcher dispatcher,
            CancellationToken ct) =>
        {
            if (request.RunIds.Count == 0)
                return Results.BadRequest(new { message = "No run IDs provided" });

            var errors = new List<string>();
            foreach (var runId in request.RunIds)
            {
                try
                {
                    await dispatcher.CancelAsync(runId, ct);
                }
                catch (Exception ex)
                {
                    errors.Add($"Run {runId}: {ex.Message}");
                }
            }

            var count = await store.BulkCancelRunsAsync(request.RunIds, ct);
            return Results.Ok(new BulkOperationResult(count, errors));
        });

        // --- Credential Validation ---

        writeApi.MapPost("/providers/validate", async (
            ValidateProviderRequest request,
            CredentialValidationService validator,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Provider))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["provider"] = ["Provider is required"] });

            if (string.IsNullOrWhiteSpace(request.SecretValue))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["secretValue"] = ["Secret value is required"] });

            var (success, message) = await validator.ValidateAsync(request.Provider, request.SecretValue.Trim(), ct);
            return Results.Ok(new { success, message });
        });

        // --- Harness Health ---

        readApi.MapGet("/health/harnesses", ([FromServices] HarnessHealthService harnessHealth) =>
            Results.Ok(harnessHealth.GetAllHealth()));

        // --- Proxy Audits ---

        readApi.MapGet("/proxy-audits", async (
            OrchestratorStore store,
            string? projectId,
            string? repositoryId,
            string? taskId,
            string? runId,
            int? limit,
            int? skip,
            string? from,
            string? to,
            int? minLatencyMs,
            int? maxLatencyMs,
            CancellationToken ct) =>
        {
            var effectiveLimit = limit is > 0 ? Math.Min(limit.Value, 500) : 100;
            var audits = await store.ListProxyAuditsAsync(projectId, repositoryId, taskId, runId, effectiveLimit, ct);
            return Results.Ok(audits);
        });

        // --- Task Templates ---

        readApi.MapGet("/templates", async (TaskTemplateService templateService, CancellationToken ct) =>
            Results.Ok(await templateService.ListTemplatesAsync(ct)));

        readApi.MapGet("/templates/{templateId}", async (string templateId, TaskTemplateService templateService, CancellationToken ct) =>
        {
            var template = await templateService.GetTemplateByTemplateIdAsync(templateId, ct);
            return template is null ? Results.NotFound(new { message = "Template not found" }) : Results.Ok(template);
        });

        writeApi.MapPost("/templates", async (TaskTemplateDocument template, TaskTemplateService templateService, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(template.Name))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required"] });

            var created = await templateService.CreateCustomTemplateAsync(template, ct);
            return Results.Ok(created);
        });

        writeApi.MapPut("/templates/{templateId}", async (string templateId, TaskTemplateDocument template, TaskTemplateService templateService, CancellationToken ct) =>
        {
            var updated = await templateService.UpdateTemplateAsync(templateId, template, ct);
            return updated is null ? Results.NotFound(new { message = "Template not found or not editable" }) : Results.Ok(updated);
        });

        writeApi.MapDelete("/templates/{templateId}", async (string templateId, TaskTemplateService templateService, CancellationToken ct) =>
        {
            var template = await templateService.GetTemplateByTemplateIdAsync(templateId, ct);
            if (template is null)
                return Results.NotFound(new { message = "Template not found" });

            var deleted = await templateService.DeleteTemplateAsync(templateId, ct);
            return deleted ? Results.Ok(new { message = "Template deleted" }) : Results.BadRequest(new { message = "Cannot delete built-in templates" });
        });

        return app;
    }
}
