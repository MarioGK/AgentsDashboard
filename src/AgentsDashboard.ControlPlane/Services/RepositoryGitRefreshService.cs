using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class RepositoryGitRefreshService(
    IOrchestratorStore store,
    IGitWorkspaceService gitWorkspaceService,
    ISecretCryptoService secretCrypto,
    IBackgroundWorkCoordinator backgroundWorkCoordinator,
    ILogger<RepositoryGitRefreshService> logger) : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RecentlyViewedWindow = TimeSpan.FromMinutes(30);
    private const int RefreshBatchSize = 20;
    private const string RefreshOperationKey = "repository-git-refresh";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        QueueRefreshCycle();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(RefreshInterval, stoppingToken);
                QueueRefreshCycle();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.ZLogWarning(ex, "Background repository git refresh failed");
            }
        }
    }

    private void QueueRefreshCycle()
    {
        var workId = backgroundWorkCoordinator.Enqueue(
            BackgroundWorkKind.RepositoryGitRefresh,
            RefreshOperationKey,
            RefreshRecentlyViewedAsync,
            dedupeByOperationKey: true,
            isCritical: false);
        logger.ZLogDebug("Queued repository git refresh cycle as background work {WorkId}", workId);
    }

    private async Task RefreshRecentlyViewedAsync(
        CancellationToken cancellationToken,
        IProgress<BackgroundWorkSnapshot> progress)
    {
        progress.Report(CreateProgress("Scanning recently viewed repositories.", 5));
        var repositories = await store.ListRepositoriesAsync(cancellationToken);
        var threshold = DateTime.UtcNow - RecentlyViewedWindow;
        var targets = repositories
            .Where(r => r.LastViewedAtUtc.HasValue && r.LastViewedAtUtc.Value >= threshold)
            .OrderByDescending(r => r.LastViewedAtUtc)
            .Take(RefreshBatchSize)
            .ToList();

        if (targets.Count == 0)
        {
            progress.Report(new BackgroundWorkSnapshot(
                WorkId: string.Empty,
                OperationKey: string.Empty,
                Kind: BackgroundWorkKind.RepositoryGitRefresh,
                State: BackgroundWorkState.Succeeded,
                PercentComplete: 100,
                Message: "No recently viewed repositories to refresh.",
                StartedAt: null,
                UpdatedAt: DateTimeOffset.UtcNow,
                ErrorCode: null,
                ErrorMessage: null));
            return;
        }

        for (var index = 0; index < targets.Count; index++)
        {
            var repository = targets[index];
            var percent = Math.Clamp((int)Math.Round(((index + 1d) / targets.Count) * 95d), 5, 95);
            progress.Report(CreateProgress(
                $"Refreshing repository {repository.Name} ({index + 1}/{targets.Count}).",
                percent));

            try
            {
                var githubToken = await TryGetGithubTokenAsync(repository.Id, cancellationToken);
                var gitStatus = await gitWorkspaceService.RefreshStatusAsync(repository, githubToken, fetchRemote: true, cancellationToken);
                await store.UpdateRepositoryGitStateAsync(repository.Id, gitStatus, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.ZLogDebug(ex, "Skipping repository git refresh for {RepositoryId}", repository.Id);
                var status = new RepositoryGitStatus(
                    repository.CurrentBranch,
                    repository.CurrentCommit,
                    repository.AheadCount,
                    repository.BehindCount,
                    repository.ModifiedCount,
                    repository.StagedCount,
                    repository.UntrackedCount,
                    DateTime.UtcNow,
                    repository.LastFetchedAtUtc,
                    ex.Message);

                await store.UpdateRepositoryGitStateAsync(repository.Id, status, cancellationToken);
            }
        }

        progress.Report(new BackgroundWorkSnapshot(
            WorkId: string.Empty,
            OperationKey: string.Empty,
            Kind: BackgroundWorkKind.RepositoryGitRefresh,
            State: BackgroundWorkState.Succeeded,
            PercentComplete: 100,
            Message: $"Repository git refresh completed for {targets.Count} repositories.",
            StartedAt: null,
            UpdatedAt: DateTimeOffset.UtcNow,
            ErrorCode: null,
            ErrorMessage: null));
    }

    private async Task<string?> TryGetGithubTokenAsync(string repositoryId, CancellationToken cancellationToken)
    {
        var secret = await store.GetProviderSecretAsync(repositoryId, "github", cancellationToken);
        if (secret is null)
        {
            return null;
        }

        try
        {
            return secretCrypto.Decrypt(secret.EncryptedValue);
        }
        catch
        {
            return null;
        }
    }

    private static BackgroundWorkSnapshot CreateProgress(string message, int? percentComplete)
    {
        return new BackgroundWorkSnapshot(
            WorkId: string.Empty,
            OperationKey: string.Empty,
            Kind: BackgroundWorkKind.RepositoryGitRefresh,
            State: BackgroundWorkState.Running,
            PercentComplete: percentComplete,
            Message: message,
            StartedAt: null,
            UpdatedAt: DateTimeOffset.UtcNow,
            ErrorCode: null,
            ErrorMessage: null);
    }
}
