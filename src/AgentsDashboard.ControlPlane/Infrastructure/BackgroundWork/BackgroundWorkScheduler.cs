using System.Collections.Concurrent;
using System.Threading.Channels;

namespace AgentsDashboard.ControlPlane.Infrastructure.BackgroundWork;

public sealed class BackgroundWorkScheduler(ILogger<BackgroundWorkScheduler> logger)
    : BackgroundService, IBackgroundWorkCoordinator
{
    private const int MaxRetainedSnapshots = 256;
    private const int MaxConcurrentWorkItems = 4;

    private readonly Channel<QueuedBackgroundWork> _queue = Channel.CreateUnbounded<QueuedBackgroundWork>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    private readonly SemaphoreSlim _concurrencyGate = new(MaxConcurrentWorkItems, MaxConcurrentWorkItems);
    private readonly ConcurrentDictionary<string, BackgroundWorkSnapshot> _snapshots =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task> _runningWork =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _operationIndex =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _operationIndexLock = new();
    private readonly object _trimLock = new();

    public event Action<BackgroundWorkSnapshot>? Updated;

    public string Enqueue(
        BackgroundWorkKind kind,
        string operationKey,
        Func<CancellationToken, IProgress<BackgroundWorkSnapshot>, Task> work,
        bool dedupeByOperationKey = true,
        bool isCritical = false)
    {
        if (string.IsNullOrWhiteSpace(operationKey))
        {
            throw new ArgumentException("Operation key is required.", nameof(operationKey));
        }

        ArgumentNullException.ThrowIfNull(work);

        BackgroundWorkSnapshot snapshot;
        var workId = string.Empty;

        lock (_operationIndexLock)
        {
            if (dedupeByOperationKey &&
                _operationIndex.TryGetValue(operationKey, out var existingWorkId) &&
                _snapshots.TryGetValue(existingWorkId, out var existingSnapshot) &&
                IsActive(existingSnapshot.State))
            {
                return existingWorkId;
            }

            workId = Guid.NewGuid().ToString("N");
            snapshot = new BackgroundWorkSnapshot(
                WorkId: workId,
                OperationKey: operationKey,
                Kind: kind,
                State: BackgroundWorkState.Pending,
                PercentComplete: null,
                Message: "Queued",
                StartedAt: null,
                UpdatedAt: DateTimeOffset.UtcNow,
                ErrorCode: null,
                ErrorMessage: null);

            _snapshots[workId] = snapshot;
            if (dedupeByOperationKey)
            {
                _operationIndex[operationKey] = workId;
            }

            if (!_queue.Writer.TryWrite(new QueuedBackgroundWork(
                    workId,
                    operationKey,
                    kind,
                    work,
                    dedupeByOperationKey,
                    isCritical)))
            {
                _snapshots.TryRemove(workId, out _);
                if (dedupeByOperationKey &&
                    _operationIndex.TryGetValue(operationKey, out var mappedWorkId) &&
                    string.Equals(mappedWorkId, workId, StringComparison.OrdinalIgnoreCase))
                {
                    _operationIndex.Remove(operationKey);
                }

                throw new InvalidOperationException("Unable to enqueue background work because the queue is unavailable.");
            }
        }

        PublishUpdate(snapshot);
        return workId;
    }

    public IReadOnlyCollection<BackgroundWorkSnapshot> Snapshot()
    {
        return _snapshots.Values
            .OrderByDescending(static snapshot => snapshot.UpdatedAt ?? snapshot.StartedAt ?? DateTimeOffset.MinValue)
            .ThenBy(static snapshot => snapshot.WorkId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public bool TryGet(string workId, out BackgroundWorkSnapshot snapshot)
    {
        return _snapshots.TryGetValue(workId, out snapshot!);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await _queue.Reader.WaitToReadAsync(stoppingToken))
            {
                while (_queue.Reader.TryRead(out var workItem))
                {
                    await _concurrencyGate.WaitAsync(stoppingToken);

                    var executionTask = ExecuteWorkItemAsync(workItem, stoppingToken);
                    _runningWork[workItem.WorkId] = executionTask;

                    _ = executionTask.ContinueWith(
                        _ =>
                        {
                            _runningWork.TryRemove(workItem.WorkId, out Task? _);
                            _concurrencyGate.Release();
                        },
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            var remainingTasks = _runningWork.Values.ToArray();
            if (remainingTasks.Length > 0)
            {
                try
                {
                    await Task.WhenAll(remainingTasks);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Background work scheduler observed exception while draining outstanding tasks.");
                }
            }
        }
    }

    private async Task ExecuteWorkItemAsync(QueuedBackgroundWork workItem, CancellationToken stoppingToken)
    {
        UpdateSnapshot(
            workItem.WorkId,
            static snapshot => snapshot with
            {
                State = BackgroundWorkState.Running,
                Message = "Running",
                StartedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                ErrorCode = null,
                ErrorMessage = null,
            });

        var progress = new Progress<BackgroundWorkSnapshot>(snapshot => ApplyProgressUpdate(workItem.WorkId, snapshot));

        try
        {
            await workItem.Work(stoppingToken, progress);

            UpdateSnapshot(
                workItem.WorkId,
                static snapshot => snapshot with
                {
                    State = snapshot.State is BackgroundWorkState.Failed or BackgroundWorkState.Cancelled
                        ? snapshot.State
                        : BackgroundWorkState.Succeeded,
                    PercentComplete = snapshot.PercentComplete ?? 100,
                    Message = snapshot.Message is "Running" or "Queued" ? "Completed" : snapshot.Message,
                    UpdatedAt = DateTimeOffset.UtcNow,
                });
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            UpdateSnapshot(
                workItem.WorkId,
                static snapshot => snapshot with
                {
                    State = BackgroundWorkState.Cancelled,
                    Message = "Cancelled",
                    UpdatedAt = DateTimeOffset.UtcNow,
                    ErrorCode = "cancelled",
                    ErrorMessage = null,
                });
        }
        catch (Exception ex)
        {
            UpdateSnapshot(
                workItem.WorkId,
                snapshot => snapshot with
                {
                    State = BackgroundWorkState.Failed,
                    Message = "Failed",
                    UpdatedAt = DateTimeOffset.UtcNow,
                    ErrorCode = "exception",
                    ErrorMessage = ex.Message,
                });

            if (workItem.IsCritical)
            {
                logger.LogError(ex, "Critical background work failed for {OperationKey}", workItem.OperationKey);
            }
            else
            {
                logger.LogWarning(ex, "Background work failed for {OperationKey}", workItem.OperationKey);
            }
        }
        finally
        {
            if (workItem.DedupeByOperationKey)
            {
                lock (_operationIndexLock)
                {
                    if (_operationIndex.TryGetValue(workItem.OperationKey, out var mappedWorkId) &&
                        string.Equals(mappedWorkId, workItem.WorkId, StringComparison.OrdinalIgnoreCase))
                    {
                        _operationIndex.Remove(workItem.OperationKey);
                    }
                }
            }

            TrimSnapshotsIfNeeded();
        }
    }

    private void ApplyProgressUpdate(string workId, BackgroundWorkSnapshot update)
    {
        UpdateSnapshot(
            workId,
            snapshot =>
            {
                if (snapshot.State is BackgroundWorkState.Succeeded or BackgroundWorkState.Failed or BackgroundWorkState.Cancelled)
                {
                    return snapshot;
                }

                var state = update.State == BackgroundWorkState.Pending
                    ? BackgroundWorkState.Running
                    : update.State;

                var message = string.IsNullOrWhiteSpace(update.Message)
                    ? snapshot.Message
                    : update.Message;

                var percent = update.PercentComplete ?? snapshot.PercentComplete;
                if (percent.HasValue)
                {
                    percent = Math.Clamp(percent.Value, 0, 100);
                }

                return snapshot with
                {
                    Kind = update.Kind == BackgroundWorkKind.Other ? snapshot.Kind : update.Kind,
                    OperationKey = string.IsNullOrWhiteSpace(update.OperationKey)
                        ? snapshot.OperationKey
                        : update.OperationKey,
                    State = state,
                    PercentComplete = percent,
                    Message = message,
                    StartedAt = snapshot.StartedAt ?? update.StartedAt ?? DateTimeOffset.UtcNow,
                    UpdatedAt = update.UpdatedAt ?? DateTimeOffset.UtcNow,
                    ErrorCode = update.ErrorCode ?? snapshot.ErrorCode,
                    ErrorMessage = update.ErrorMessage ?? snapshot.ErrorMessage,
                };
            });
    }

    private void UpdateSnapshot(string workId, Func<BackgroundWorkSnapshot, BackgroundWorkSnapshot> transform)
    {
        while (_snapshots.TryGetValue(workId, out var current))
        {
            var updated = transform(current);
            if (updated.PercentComplete.HasValue)
            {
                updated = updated with { PercentComplete = Math.Clamp(updated.PercentComplete.Value, 0, 100) };
            }

            if (_snapshots.TryUpdate(workId, updated, current))
            {
                PublishUpdate(updated);
                return;
            }
        }
    }

    private void TrimSnapshotsIfNeeded()
    {
        if (_snapshots.Count <= MaxRetainedSnapshots)
        {
            return;
        }

        lock (_trimLock)
        {
            if (_snapshots.Count <= MaxRetainedSnapshots)
            {
                return;
            }

            var overflow = _snapshots.Count - MaxRetainedSnapshots;
            if (overflow <= 0)
            {
                return;
            }

            var candidates = _snapshots.Values
                .Where(snapshot => !IsActive(snapshot.State))
                .OrderBy(snapshot => snapshot.UpdatedAt ?? snapshot.StartedAt ?? DateTimeOffset.MinValue)
                .Take(overflow)
                .Select(static snapshot => snapshot.WorkId)
                .ToList();

            foreach (var workId in candidates)
            {
                _snapshots.TryRemove(workId, out _);
            }
        }
    }

    private void PublishUpdate(BackgroundWorkSnapshot snapshot)
    {
        var handlers = Updated;
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Action<BackgroundWorkSnapshot>>())
        {
            try
            {
                handler(snapshot);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to deliver background work update for {WorkId}", snapshot.WorkId);
            }
        }
    }

    private static bool IsActive(BackgroundWorkState state)
    {
        return state is BackgroundWorkState.Pending or BackgroundWorkState.Running;
    }

    private sealed record QueuedBackgroundWork(
        string WorkId,
        string OperationKey,
        BackgroundWorkKind Kind,
        Func<CancellationToken, IProgress<BackgroundWorkSnapshot>, Task> Work,
        bool DedupeByOperationKey,
        bool IsCritical);
}
