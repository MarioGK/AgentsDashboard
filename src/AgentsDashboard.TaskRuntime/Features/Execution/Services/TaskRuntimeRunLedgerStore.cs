using System.Text.Json;

using AgentsDashboard.TaskRuntime.Infrastructure.Data;

namespace AgentsDashboard.TaskRuntime.Features.Execution.Services;

public sealed class TaskRuntimeRunLedgerStore(
    TaskRuntimeLiteDbStore liteDbStore,
    ILogger<TaskRuntimeRunLedgerStore> logger)
{
    private const string CollectionName = "runtime_run_ledger";

    public async Task UpsertQueuedAsync(DispatchJobRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RunId))
        {
            return;
        }

        await liteDbStore.ExecuteAsync(
            database =>
            {
                var collection = database.GetCollection<TaskRuntimeRunLedgerDocument>(CollectionName);
                collection.EnsureIndex(x => x.TaskId);
                collection.EnsureIndex(x => x.State);
                collection.EnsureIndex(x => x.UpdatedAtUtc);
                var now = DateTime.UtcNow;
                var document = collection.FindById(request.RunId);
                if (document is null)
                {
                    document = new TaskRuntimeRunLedgerDocument
                    {
                        RunId = request.RunId,
                        TaskId = request.TaskId,
                        State = TaskRuntimeExecutionState.Queued,
                        Summary = "Queued",
                        PayloadJson = string.Empty,
                        RequestJson = JsonSerializer.Serialize(request),
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now
                    };
                    collection.Insert(document);
                    return;
                }

                document.TaskId = request.TaskId;
                document.State = TaskRuntimeExecutionState.Queued;
                document.Summary = "Queued";
                document.PayloadJson = string.Empty;
                document.RequestJson = JsonSerializer.Serialize(request);
                document.EndedAtUtc = null;
                document.UpdatedAtUtc = now;
                collection.Update(document);
            },
            cancellationToken);
    }

    public async Task MarkRunningAsync(string runId, string taskId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return;
        }

        await liteDbStore.ExecuteAsync(
            database =>
            {
                var collection = database.GetCollection<TaskRuntimeRunLedgerDocument>(CollectionName);
                collection.EnsureIndex(x => x.State);
                var now = DateTime.UtcNow;
                var document = collection.FindById(runId) ?? new TaskRuntimeRunLedgerDocument
                {
                    RunId = runId,
                    TaskId = taskId,
                    CreatedAtUtc = now
                };

                document.TaskId = string.IsNullOrWhiteSpace(taskId) ? document.TaskId : taskId;
                document.State = TaskRuntimeExecutionState.Running;
                document.Summary = "Running";
                document.StartedAtUtc ??= now;
                document.EndedAtUtc = null;
                document.UpdatedAtUtc = now;
                collection.Upsert(document);
            },
            cancellationToken);
    }

    public async Task MarkCompletedAsync(
        string runId,
        string taskId,
        TaskRuntimeExecutionState state,
        string summary,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return;
        }

        await liteDbStore.ExecuteAsync(
            database =>
            {
                var collection = database.GetCollection<TaskRuntimeRunLedgerDocument>(CollectionName);
                var now = DateTime.UtcNow;
                var document = collection.FindById(runId) ?? new TaskRuntimeRunLedgerDocument
                {
                    RunId = runId,
                    TaskId = taskId,
                    CreatedAtUtc = now
                };

                document.TaskId = string.IsNullOrWhiteSpace(taskId) ? document.TaskId : taskId;
                document.State = state;
                document.Summary = summary?.Trim() ?? string.Empty;
                document.PayloadJson = payloadJson?.Trim() ?? string.Empty;
                document.EndedAtUtc = now;
                document.UpdatedAtUtc = now;
                collection.Upsert(document);
            },
            cancellationToken);
    }

    public async Task<List<string>> ListQueuedRunIdsAsync(CancellationToken cancellationToken)
    {
        return await liteDbStore.ExecuteAsync(
            database =>
            {
                var collection = database.GetCollection<TaskRuntimeRunLedgerDocument>(CollectionName);
                return collection.Query()
                    .Where(x => x.State == TaskRuntimeExecutionState.Queued)
                    .OrderBy(x => x.CreatedAtUtc)
                    .Select(x => x.RunId)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();
            },
            cancellationToken);
    }

    public async Task<List<string>> ListRunningRunIdsAsync(CancellationToken cancellationToken)
    {
        return await liteDbStore.ExecuteAsync(
            database =>
            {
                var collection = database.GetCollection<TaskRuntimeRunLedgerDocument>(CollectionName);
                return collection.Query()
                    .Where(x => x.State == TaskRuntimeExecutionState.Running)
                    .OrderBy(x => x.UpdatedAtUtc)
                    .Select(x => x.RunId)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();
            },
            cancellationToken);
    }

    public async Task<List<DispatchJobRequest>> ListQueuedRequestsAsync(CancellationToken cancellationToken)
    {
        var records = await liteDbStore.ExecuteAsync(
            database =>
            {
                var collection = database.GetCollection<TaskRuntimeRunLedgerDocument>(CollectionName);
                return collection.Query()
                    .Where(x => x.State == TaskRuntimeExecutionState.Queued && x.RequestJson != string.Empty)
                    .OrderBy(x => x.CreatedAtUtc)
                    .ToList();
            },
            cancellationToken);

        var requests = new List<DispatchJobRequest>(records.Count);
        foreach (var record in records)
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<DispatchJobRequest>(record.RequestJson);
                if (parsed is null || string.IsNullOrWhiteSpace(parsed.RunId))
                {
                    continue;
                }

                requests.Add(parsed);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Failed to deserialize queued run request {RunId}", record.RunId);
            }
        }

        return requests;
    }

    public async Task<RunExecutionSnapshotResult> GetSnapshotAsync(string runId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return new RunExecutionSnapshotResult
            {
                Success = false,
                ErrorMessage = "run_id is required",
                Found = false
            };
        }

        var record = await liteDbStore.ExecuteAsync(
            database =>
            {
                var collection = database.GetCollection<TaskRuntimeRunLedgerDocument>(CollectionName);
                return collection.FindById(runId.Trim());
            },
            cancellationToken);

        if (record is null)
        {
            return new RunExecutionSnapshotResult
            {
                Success = true,
                ErrorMessage = null,
                Found = false,
                RunId = runId.Trim()
            };
        }

        return new RunExecutionSnapshotResult
        {
            Success = true,
            ErrorMessage = null,
            Found = true,
            RunId = record.RunId,
            TaskId = record.TaskId,
            State = record.State,
            Summary = record.Summary,
            PayloadJson = record.PayloadJson,
            StartedAt = record.StartedAtUtc.HasValue ? new DateTimeOffset(record.StartedAtUtc.Value, TimeSpan.Zero) : null,
            EndedAt = record.EndedAtUtc.HasValue ? new DateTimeOffset(record.EndedAtUtc.Value, TimeSpan.Zero) : null,
            UpdatedAt = new DateTimeOffset(record.UpdatedAtUtc, TimeSpan.Zero)
        };
    }

    public async Task RecoverStaleRunningRunsAsync(CancellationToken cancellationToken)
    {
        await liteDbStore.ExecuteAsync(
            database =>
            {
                var collection = database.GetCollection<TaskRuntimeRunLedgerDocument>(CollectionName);
                var running = collection.Query()
                    .Where(x => x.State == TaskRuntimeExecutionState.Running)
                    .ToList();

                if (running.Count == 0)
                {
                    return;
                }

                var now = DateTime.UtcNow;
                foreach (var run in running)
                {
                    run.State = TaskRuntimeExecutionState.Failed;
                    run.Summary = "Task runtime restarted before completion";
                    run.EndedAtUtc = now;
                    run.UpdatedAtUtc = now;
                    collection.Update(run);
                }
            },
            cancellationToken);
    }
}
