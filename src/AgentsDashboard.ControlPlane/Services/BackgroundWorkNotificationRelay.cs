using System.Collections.Concurrent;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class BackgroundWorkNotificationRelay(
    IBackgroundWorkCoordinator backgroundWorkCoordinator,
    INotificationSink notificationSink,
    ILogger<BackgroundWorkNotificationRelay> logger) : IHostedService
{
    private static readonly TimeSpan RunningMessagePublishInterval = TimeSpan.FromSeconds(15);

    private readonly ConcurrentDictionary<string, BackgroundWorkSnapshot> _lastSnapshots =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastRunningMessagePublishedAt =
        new(StringComparer.OrdinalIgnoreCase);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        backgroundWorkCoordinator.Updated += OnBackgroundWorkUpdated;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        backgroundWorkCoordinator.Updated -= OnBackgroundWorkUpdated;
        return Task.CompletedTask;
    }

    private void OnBackgroundWorkUpdated(BackgroundWorkSnapshot snapshot)
    {
        _ = PublishNotificationAsync(snapshot);
    }

    private async Task PublishNotificationAsync(BackgroundWorkSnapshot snapshot)
    {
        try
        {
            _lastSnapshots.TryGetValue(snapshot.WorkId, out var previous);
            _lastSnapshots[snapshot.WorkId] = snapshot;

            if (!ShouldPublish(previous, snapshot))
            {
                return;
            }

            var severity = MapSeverity(snapshot.State);
            var title = BuildTitle(snapshot);
            var message = BuildMessage(snapshot);

            await notificationSink.PublishAsync(
                title,
                message,
                severity,
                NotificationSource.BackgroundWork,
                snapshot.WorkId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to relay background work notification for {WorkId}", snapshot.WorkId);
        }
    }

    private bool ShouldPublish(BackgroundWorkSnapshot? previous, BackgroundWorkSnapshot current)
    {
        if (previous is null)
        {
            return true;
        }

        if (previous.State != current.State)
        {
            return true;
        }

        if (current.State != BackgroundWorkState.Running)
        {
            return false;
        }

        if (current.PercentComplete is int currentPercent)
        {
            var currentBucket = Math.Clamp(currentPercent / 10, 0, 10);
            var previousBucket = previous.PercentComplete.HasValue
                ? Math.Clamp(previous.PercentComplete.Value / 10, 0, 10)
                : -1;
            if (currentBucket > previousBucket)
            {
                return true;
            }
        }

        if (!string.Equals(previous.Message, current.Message, StringComparison.Ordinal))
        {
            return ShouldPublishRunningMessage(current.WorkId);
        }

        return false;
    }

    private bool ShouldPublishRunningMessage(string workId)
    {
        var now = DateTimeOffset.UtcNow;

        while (true)
        {
            if (!_lastRunningMessagePublishedAt.TryGetValue(workId, out var lastPublishedAt))
            {
                if (_lastRunningMessagePublishedAt.TryAdd(workId, now))
                {
                    return true;
                }

                continue;
            }

            if (now - lastPublishedAt < RunningMessagePublishInterval)
            {
                return false;
            }

            if (_lastRunningMessagePublishedAt.TryUpdate(workId, now, lastPublishedAt))
            {
                return true;
            }
        }
    }

    private static string BuildTitle(BackgroundWorkSnapshot snapshot)
    {
        var kind = snapshot.Kind.ToString();
        return snapshot.State switch
        {
            BackgroundWorkState.Pending => $"{kind}: queued",
            BackgroundWorkState.Running => $"{kind}: running",
            BackgroundWorkState.Succeeded => $"{kind}: succeeded",
            BackgroundWorkState.Failed => $"{kind}: failed",
            BackgroundWorkState.Cancelled => $"{kind}: cancelled",
            _ => kind,
        };
    }

    private static string BuildMessage(BackgroundWorkSnapshot snapshot)
    {
        if (snapshot.State == BackgroundWorkState.Failed)
        {
            var detail = !string.IsNullOrWhiteSpace(snapshot.ErrorMessage)
                ? snapshot.ErrorMessage
                : snapshot.Message;
            var remediation = snapshot.Kind switch
            {
                BackgroundWorkKind.TaskRuntimeImageResolution => "Retry from /settings/task-runtimes using Ensure Task Runtime Image.",
                BackgroundWorkKind.LiteDbVectorBootstrap => "Check LiteDB vector search configuration and restart the probe.",
                BackgroundWorkKind.RepositoryGitRefresh => "Verify repository paths and credentials, then retry refresh.",
                BackgroundWorkKind.Recovery => "Inspect recovery logs and run reconciliation from orchestrator settings.",
                BackgroundWorkKind.TaskTemplateInit => "Review task template definitions and retry initialization.",
                _ => "Review service logs and retry.",
            };

            return string.IsNullOrWhiteSpace(detail)
                ? remediation
                : $"{detail} {remediation}";
        }

        var progressSuffix = snapshot.PercentComplete.HasValue
            ? $" ({Math.Clamp(snapshot.PercentComplete.Value, 0, 100)}%)"
            : string.Empty;

        return string.IsNullOrWhiteSpace(snapshot.Message)
            ? $"{snapshot.OperationKey}{progressSuffix}"
            : $"{snapshot.Message}{progressSuffix}";
    }

    private static NotificationSeverity MapSeverity(BackgroundWorkState state)
    {
        return state switch
        {
            BackgroundWorkState.Pending => NotificationSeverity.Info,
            BackgroundWorkState.Running => NotificationSeverity.Info,
            BackgroundWorkState.Succeeded => NotificationSeverity.Success,
            BackgroundWorkState.Failed => NotificationSeverity.Error,
            BackgroundWorkState.Cancelled => NotificationSeverity.Warning,
            _ => NotificationSeverity.Info,
        };
    }
}
