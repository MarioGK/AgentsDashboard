namespace AgentsDashboard.ControlPlane.Features.Repositories.Services;

public interface IGitWorkspaceService
{
    Task<GitWorkspaceOperationResult> EnsureWorkspaceAsync(string gitUrl, string localPath, string defaultBranch, string? githubToken, bool fetchRemote, CancellationToken cancellationToken);
    Task<GitWorkspaceOperationResult> RefreshStatusAsync(RepositoryDocument repository, string? githubToken, bool fetchRemote, CancellationToken cancellationToken);
}

public sealed class GitWorkspaceService(
    TaskRuntimeRepositoryGitGateway gateway,
    ILogger<GitWorkspaceService> logger) : IGitWorkspaceService
{
    public async Task<GitWorkspaceOperationResult> EnsureWorkspaceAsync(
        string gitUrl,
        string localPath,
        string defaultBranch,
        string? githubToken,
        bool fetchRemote,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(gitUrl))
        {
            throw new InvalidOperationException("Git URL is required.");
        }

        var response = await gateway.EnsureWorkspaceAsync(
            new EnsureRepositoryWorkspaceRequest
            {
                RepositoryId = string.Empty,
                GitUrl = gitUrl.Trim(),
                DefaultBranch = string.IsNullOrWhiteSpace(defaultBranch) ? "main" : defaultBranch.Trim(),
                GitHubToken = string.IsNullOrWhiteSpace(githubToken) ? null : githubToken.Trim(),
                FetchRemote = fetchRemote,
                RepositoryKeyHint = string.IsNullOrWhiteSpace(localPath) ? string.Empty : localPath.Trim(),
            },
            cancellationToken);

        if (!response.Success)
        {
            throw new InvalidOperationException(response.ErrorMessage ?? "Runtime workspace ensure failed.");
        }

        return MapResponse(response);
    }

    public async Task<GitWorkspaceOperationResult> RefreshStatusAsync(
        RepositoryDocument repository,
        string? githubToken,
        bool fetchRemote,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(repository.Id))
        {
            throw new InvalidOperationException("Repository id is required.");
        }

        if (string.IsNullOrWhiteSpace(repository.GitUrl))
        {
            throw new InvalidOperationException("Repository git URL is required.");
        }

        var response = await gateway.RefreshWorkspaceAsync(
            new RefreshRepositoryWorkspaceRequest
            {
                RepositoryId = repository.Id,
                GitUrl = repository.GitUrl.Trim(),
                DefaultBranch = string.IsNullOrWhiteSpace(repository.DefaultBranch) ? "main" : repository.DefaultBranch.Trim(),
                GitHubToken = string.IsNullOrWhiteSpace(githubToken) ? null : githubToken.Trim(),
                FetchRemote = fetchRemote,
                LocalPath = repository.LocalPath ?? string.Empty,
            },
            cancellationToken);

        if (!response.Success)
        {
            throw new InvalidOperationException(response.ErrorMessage ?? "Runtime workspace refresh failed.");
        }

        return MapResponse(response);
    }

    private GitWorkspaceOperationResult MapResponse(RepositoryWorkspaceResult response)
    {
        if (response.Attempts.Count > 0)
        {
            logger.LogInformation(
                "Runtime git operation completed using {Strategy} for {GitUrl}",
                response.Attempts.Last().Strategy,
                response.EffectiveGitUrl);
        }

        var status = new RepositoryGitStatus(
            response.GitStatus.CurrentBranch,
            response.GitStatus.CurrentCommit,
            response.GitStatus.AheadCount,
            response.GitStatus.BehindCount,
            response.GitStatus.ModifiedCount,
            response.GitStatus.StagedCount,
            response.GitStatus.UntrackedCount,
            response.GitStatus.ScannedAtUtc,
            response.GitStatus.FetchedAtUtc,
            response.GitStatus.LastSyncError);

        return new GitWorkspaceOperationResult(
            status,
            response.EffectiveGitUrl,
            response.WorkspacePath,
            response.Attempts);
    }
}
