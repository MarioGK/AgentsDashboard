using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class RepositoryGitRefreshService(
    IOrchestratorStore store,
    IGitWorkspaceService gitWorkspaceService,
    ISecretCryptoService secretCrypto,
    ILogger<RepositoryGitRefreshService> logger) : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RecentlyViewedWindow = TimeSpan.FromMinutes(30);
    private const int RefreshBatchSize = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshRecentlyViewedAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.ZLogWarning(ex, "Background repository git refresh failed");
            }

            await Task.Delay(RefreshInterval, stoppingToken);
        }
    }

    private async Task RefreshRecentlyViewedAsync(CancellationToken cancellationToken)
    {
        var repositories = await store.ListRepositoriesAsync(cancellationToken);
        var threshold = DateTime.UtcNow - RecentlyViewedWindow;

        foreach (var repository in repositories
                     .Where(r => r.LastViewedAtUtc.HasValue && r.LastViewedAtUtc.Value >= threshold)
                     .OrderByDescending(r => r.LastViewedAtUtc)
                     .Take(RefreshBatchSize))
        {
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
}
