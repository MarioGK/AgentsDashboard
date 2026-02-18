using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;
using CliWrap;
using CliWrap.Buffered;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class DevelopmentSelfRepositoryBootstrapService(
    IHostEnvironment hostEnvironment,
    IBackgroundWorkCoordinator backgroundWorkCoordinator,
    IOrchestratorStore store,
    IGitWorkspaceService gitWorkspace,
    ILogger<DevelopmentSelfRepositoryBootstrapService> logger) : IHostedService
{
    private const string StartupOperationKey = "startup:development-repository-bootstrap";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!hostEnvironment.IsDevelopment())
        {
            return Task.CompletedTask;
        }

        var workId = backgroundWorkCoordinator.Enqueue(
            BackgroundWorkKind.RepositoryGitRefresh,
            StartupOperationKey,
            RunBootstrapAsync,
            dedupeByOperationKey: true,
            isCritical: false);

        logger.ZLogInformation("Queued development self-repository bootstrap background work {WorkId}", workId);
        return Task.CompletedTask;
    }

    private async Task RunBootstrapAsync(
        CancellationToken cancellationToken,
        IProgress<BackgroundWorkSnapshot> progress)
    {
        progress.Report(new BackgroundWorkSnapshot(
            WorkId: string.Empty,
            OperationKey: string.Empty,
            Kind: BackgroundWorkKind.RepositoryGitRefresh,
            State: BackgroundWorkState.Running,
            PercentComplete: 10,
            Message: "Resolving development repository seed.",
            StartedAt: null,
            UpdatedAt: DateTimeOffset.UtcNow,
            ErrorCode: null,
            ErrorMessage: null));

        var repositorySeed = await TryResolveRepositorySeedAsync(cancellationToken);
        if (repositorySeed is null)
        {
            logger.ZLogDebug("Skipped development repository bootstrap because current directory is not a git workspace with origin.");
            progress.Report(new BackgroundWorkSnapshot(
                WorkId: string.Empty,
                OperationKey: string.Empty,
                Kind: BackgroundWorkKind.RepositoryGitRefresh,
                State: BackgroundWorkState.Succeeded,
                PercentComplete: 100,
                Message: "Development repository bootstrap skipped (no git workspace seed found).",
                StartedAt: null,
                UpdatedAt: DateTimeOffset.UtcNow,
                ErrorCode: null,
                ErrorMessage: null));
            return;
        }

        progress.Report(new BackgroundWorkSnapshot(
            WorkId: string.Empty,
            OperationKey: string.Empty,
            Kind: BackgroundWorkKind.RepositoryGitRefresh,
            State: BackgroundWorkState.Running,
            PercentComplete: 45,
            Message: $"Upserting development repository {repositorySeed.Name}.",
            StartedAt: null,
            UpdatedAt: DateTimeOffset.UtcNow,
            ErrorCode: null,
            ErrorMessage: null));

        RepositoryDocument repository;
        var repositories = await store.ListRepositoriesAsync(cancellationToken);
        var existing = repositories.FirstOrDefault(x => PathsEqual(x.LocalPath, repositorySeed.LocalPath));

        if (existing is null)
        {
            repository = await store.CreateRepositoryAsync(
                new CreateRepositoryRequest(
                    repositorySeed.Name,
                    repositorySeed.GitUrl,
                    repositorySeed.LocalPath,
                    repositorySeed.DefaultBranch),
                cancellationToken);

            logger.ZLogInformation(
                "Development repository bootstrap created repository {RepositoryId} at {LocalPath}",
                repository.Id,
                repository.LocalPath);
        }
        else
        {
            repository = existing;
            if (ShouldUpdate(existing, repositorySeed))
            {
                var updated = await store.UpdateRepositoryAsync(
                    existing.Id,
                    new UpdateRepositoryRequest(
                        repositorySeed.Name,
                        repositorySeed.GitUrl,
                        repositorySeed.LocalPath,
                        repositorySeed.DefaultBranch),
                    cancellationToken);

                if (updated is not null)
                {
                    repository = updated;
                }

                logger.ZLogInformation(
                    "Development repository bootstrap updated repository {RepositoryId} at {LocalPath}",
                    existing.Id,
                    repositorySeed.LocalPath);
            }
        }

        try
        {
            progress.Report(new BackgroundWorkSnapshot(
                WorkId: string.Empty,
                OperationKey: string.Empty,
                Kind: BackgroundWorkKind.RepositoryGitRefresh,
                State: BackgroundWorkState.Running,
                PercentComplete: 80,
                Message: "Refreshing local git status for development repository.",
                StartedAt: null,
                UpdatedAt: DateTimeOffset.UtcNow,
                ErrorCode: null,
                ErrorMessage: null));

            var status = await gitWorkspace.RefreshStatusAsync(repository, githubToken: null, fetchRemote: false, cancellationToken);
            await store.UpdateRepositoryGitStateAsync(repository.Id, status, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.ZLogWarning(
                ex,
                "Development repository bootstrap could not refresh git status for {RepositoryId}",
                repository.Id);
        }

        progress.Report(new BackgroundWorkSnapshot(
            WorkId: string.Empty,
            OperationKey: string.Empty,
            Kind: BackgroundWorkKind.RepositoryGitRefresh,
            State: BackgroundWorkState.Succeeded,
            PercentComplete: 100,
            Message: "Development repository bootstrap completed.",
            StartedAt: null,
            UpdatedAt: DateTimeOffset.UtcNow,
            ErrorCode: null,
            ErrorMessage: null));
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static bool ShouldUpdate(RepositoryDocument existing, RepositorySeed seed)
    {
        return !string.Equals(existing.Name, seed.Name, StringComparison.Ordinal)
            || !string.Equals(existing.GitUrl, seed.GitUrl, StringComparison.Ordinal)
            || !PathsEqual(existing.LocalPath, seed.LocalPath)
            || !string.Equals(existing.DefaultBranch, seed.DefaultBranch, StringComparison.Ordinal);
    }

    private static bool PathsEqual(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            var normalizedLeft = NormalizePath(left);
            var normalizedRight = NormalizePath(right);

            return OperatingSystem.IsWindows()
                ? string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase)
                : string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal);
        }
        catch
        {
            return OperatingSystem.IsWindows()
                ? string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase)
                : string.Equals(left.Trim(), right.Trim(), StringComparison.Ordinal);
        }
    }

    private static string NormalizePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private async Task<RepositorySeed?> TryResolveRepositorySeedAsync(CancellationToken cancellationToken)
    {
        var localPath = await TryRunGitAsync(["rev-parse", "--show-toplevel"], Directory.GetCurrentDirectory(), cancellationToken);
        if (string.IsNullOrWhiteSpace(localPath))
        {
            return null;
        }

        localPath = Path.GetFullPath(localPath);

        var gitUrl = await TryRunGitAsync(["-C", localPath, "remote", "get-url", "origin"], Directory.GetCurrentDirectory(), cancellationToken);
        if (string.IsNullOrWhiteSpace(gitUrl))
        {
            return null;
        }

        var defaultBranch = await TryResolveDefaultBranchAsync(localPath, cancellationToken) ?? "main";
        var repositoryName = new DirectoryInfo(localPath).Name;

        return new RepositorySeed(repositoryName, gitUrl, localPath, defaultBranch);
    }

    private async Task<string?> TryResolveDefaultBranchAsync(string localPath, CancellationToken cancellationToken)
    {
        var remoteHead = await TryRunGitAsync(
            ["-C", localPath, "symbolic-ref", "--quiet", "--short", "refs/remotes/origin/HEAD"],
            Directory.GetCurrentDirectory(),
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(remoteHead))
        {
            var separatorIndex = remoteHead.LastIndexOf('/');
            return separatorIndex >= 0
                ? remoteHead[(separatorIndex + 1)..]
                : remoteHead;
        }

        var currentBranch = await TryRunGitAsync(
            ["-C", localPath, "rev-parse", "--abbrev-ref", "HEAD"],
            Directory.GetCurrentDirectory(),
            cancellationToken);

        return string.Equals(currentBranch, "HEAD", StringComparison.OrdinalIgnoreCase)
            ? null
            : currentBranch;
    }

    private static async Task<string?> TryRunGitAsync(IReadOnlyList<string> args, string workingDirectory, CancellationToken cancellationToken)
    {
        var result = await Cli.Wrap("git")
            .WithArguments(args)
            .WithWorkingDirectory(workingDirectory)
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(cancellationToken);

        if (result.ExitCode != 0)
        {
            return null;
        }

        var output = result.StandardOutput.Trim();
        return string.IsNullOrWhiteSpace(output) ? null : output;
    }

    private sealed record RepositorySeed(string Name, string GitUrl, string LocalPath, string DefaultBranch);
}
