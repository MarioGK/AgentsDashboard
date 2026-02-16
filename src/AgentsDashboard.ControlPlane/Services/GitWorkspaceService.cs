using System.Text;
using AgentsDashboard.Contracts.Domain;
using CliWrap;
using CliWrap.Buffered;
using System.Text.RegularExpressions;

namespace AgentsDashboard.ControlPlane.Services;

public interface IGitWorkspaceService
{
    Task<RepositoryGitStatus> EnsureWorkspaceAsync(string gitUrl, string localPath, string defaultBranch, string? githubToken, bool fetchRemote, CancellationToken cancellationToken);
    Task<RepositoryGitStatus> RefreshStatusAsync(RepositoryDocument repository, string? githubToken, bool fetchRemote, CancellationToken cancellationToken);
}

public sealed class GitWorkspaceService(ILogger<GitWorkspaceService> logger) : IGitWorkspaceService
{
    private static readonly Regex s_urlCredentialPattern = new(@"(?<=https?://)[^/\s@]+(?=@)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public async Task<RepositoryGitStatus> EnsureWorkspaceAsync(string gitUrl, string localPath, string defaultBranch, string? githubToken, bool fetchRemote, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(gitUrl))
        {
            throw new InvalidOperationException("Git URL is required.");
        }

        if (string.IsNullOrWhiteSpace(localPath) || !Path.IsPathRooted(localPath))
        {
            throw new InvalidOperationException("Local path must be an absolute path.");
        }

        localPath = Path.GetFullPath(localPath);

        if (!Directory.Exists(localPath))
        {
            var parentPath = Directory.GetParent(localPath)?.FullName;
            if (string.IsNullOrWhiteSpace(parentPath) || !Directory.Exists(parentPath))
            {
                throw new InvalidOperationException($"Parent folder does not exist for '{localPath}'.");
            }

            await CloneAsync(gitUrl, localPath, defaultBranch, githubToken, cancellationToken);
            var clonedStatus = await InspectAsync(localPath, githubToken, fetchRemote: false, cancellationToken);
            return clonedStatus with { FetchedAtUtc = DateTime.UtcNow };
        }

        var isGitRepo = await IsGitRepositoryAsync(localPath, cancellationToken);
        if (!isGitRepo)
        {
            var hasContent = Directory.EnumerateFileSystemEntries(localPath).Any();
            if (hasContent)
            {
                throw new InvalidOperationException($"Destination '{localPath}' is not empty and is not a git repository.");
            }

            await CloneAsync(gitUrl, localPath, defaultBranch, githubToken, cancellationToken);
            var clonedStatus = await InspectAsync(localPath, githubToken, fetchRemote: false, cancellationToken);
            return clonedStatus with { FetchedAtUtc = DateTime.UtcNow };
        }

        await EnsureRemoteMatchesAsync(localPath, gitUrl, cancellationToken);

        return await InspectAsync(localPath, githubToken, fetchRemote, cancellationToken);
    }

    public async Task<RepositoryGitStatus> RefreshStatusAsync(RepositoryDocument repository, string? githubToken, bool fetchRemote, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(repository.LocalPath))
        {
            throw new InvalidOperationException("Repository local path is not configured.");
        }

        var localPath = Path.GetFullPath(repository.LocalPath);
        if (!Directory.Exists(localPath))
        {
            throw new DirectoryNotFoundException($"Repository path not found: {localPath}");
        }

        var isGitRepo = await IsGitRepositoryAsync(localPath, cancellationToken);
        if (!isGitRepo)
        {
            throw new InvalidOperationException($"Path '{localPath}' is not a git repository.");
        }

        if (!string.IsNullOrWhiteSpace(repository.GitUrl))
        {
            await EnsureRemoteMatchesAsync(localPath, repository.GitUrl, cancellationToken);
        }

        return await InspectAsync(localPath, githubToken, fetchRemote, cancellationToken);
    }

    private async Task CloneAsync(string gitUrl, string localPath, string defaultBranch, string? githubToken, CancellationToken cancellationToken)
    {
        var args = new List<string> { "clone" };

        if (!string.IsNullOrWhiteSpace(defaultBranch))
        {
            args.Add("--branch");
            args.Add(defaultBranch);
        }

        args.Add(gitUrl);
        args.Add(localPath);

        await RunGitAsync(args, githubToken, cancellationToken, Directory.GetCurrentDirectory());
    }

    private async Task<bool> IsGitRepositoryAsync(string localPath, CancellationToken cancellationToken)
    {
        try
        {
            var output = await RunGitAsync(["-C", localPath, "rev-parse", "--is-inside-work-tree"], null, cancellationToken, Directory.GetCurrentDirectory());
            return string.Equals(output.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private async Task EnsureRemoteMatchesAsync(string localPath, string expectedGitUrl, CancellationToken cancellationToken)
    {
        var currentRemote = await RunGitAsync(["-C", localPath, "remote", "get-url", "origin"], null, cancellationToken, Directory.GetCurrentDirectory());
        var normalizedCurrent = NormalizeRemote(currentRemote);
        var normalizedExpected = NormalizeRemote(expectedGitUrl);

        if (!string.Equals(normalizedCurrent, normalizedExpected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Existing repository remote '{currentRemote}' does not match requested '{expectedGitUrl}'.");
        }
    }

    private async Task<RepositoryGitStatus> InspectAsync(string localPath, string? githubToken, bool fetchRemote, CancellationToken cancellationToken)
    {
        DateTime? fetchedAt = null;
        if (fetchRemote)
        {
            await RunGitAsync(["-C", localPath, "fetch", "--all", "--prune"], githubToken, cancellationToken, Directory.GetCurrentDirectory());
            fetchedAt = DateTime.UtcNow;
        }

        var branch = await RunGitAsync(["-C", localPath, "rev-parse", "--abbrev-ref", "HEAD"], githubToken, cancellationToken, Directory.GetCurrentDirectory());
        var commit = await RunGitAsync(["-C", localPath, "rev-parse", "HEAD"], githubToken, cancellationToken, Directory.GetCurrentDirectory());

        var (aheadCount, behindCount) = await ReadAheadBehindAsync(localPath, githubToken, cancellationToken);
        var (stagedCount, modifiedCount, untrackedCount) = await ReadWorktreeCountsAsync(localPath, githubToken, cancellationToken);

        return new RepositoryGitStatus(
            branch,
            commit,
            aheadCount,
            behindCount,
            modifiedCount,
            stagedCount,
            untrackedCount,
            DateTime.UtcNow,
            fetchedAt,
            string.Empty);
    }

    private async Task<(int AheadCount, int BehindCount)> ReadAheadBehindAsync(string localPath, string? githubToken, CancellationToken cancellationToken)
    {
        try
        {
            var output = await RunGitAsync(["-C", localPath, "rev-list", "--left-right", "--count", "@{upstream}...HEAD"], githubToken, cancellationToken, Directory.GetCurrentDirectory());
            var parts = output.Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
            {
                return (0, 0);
            }

            _ = int.TryParse(parts[0], out var behind);
            _ = int.TryParse(parts[1], out var ahead);
            return (ahead, behind);
        }
        catch
        {
            return (0, 0);
        }
    }

    private async Task<(int StagedCount, int ModifiedCount, int UntrackedCount)> ReadWorktreeCountsAsync(string localPath, string? githubToken, CancellationToken cancellationToken)
    {
        var status = await RunGitAsync(["-C", localPath, "status", "--porcelain"], githubToken, cancellationToken, Directory.GetCurrentDirectory());

        var staged = 0;
        var modified = 0;
        var untracked = 0;

        foreach (var rawLine in status.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length < 2)
            {
                continue;
            }

            if (line.StartsWith("??", StringComparison.Ordinal))
            {
                untracked++;
                continue;
            }

            var indexStatus = line[0];
            var worktreeStatus = line[1];

            if (indexStatus != ' ' && indexStatus != '?')
            {
                staged++;
            }

            if (worktreeStatus != ' ' && worktreeStatus != '?')
            {
                modified++;
            }
        }

        return (staged, modified, untracked);
    }

    private async Task<string> RunGitAsync(
        IReadOnlyList<string> args,
        string? githubToken,
        CancellationToken cancellationToken,
        string workingDirectory)
    {
        var command = Cli.Wrap("git");

        var finalArgs = new List<string>();
        if (!string.IsNullOrWhiteSpace(githubToken))
        {
            finalArgs.Add("-c");
            finalArgs.Add($"http.https://github.com/.extraheader=Authorization: Basic {ToBasicAuthToken(githubToken)}");
        }

        finalArgs.AddRange(args);

        var result = await command
            .WithArguments(finalArgs)
            .WithWorkingDirectory(workingDirectory)
            .WithEnvironmentVariables(new Dictionary<string, string?>
            {
                ["GIT_TERMINAL_PROMPT"] = "0"
            })
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(cancellationToken);

        if (result.ExitCode != 0)
        {
            var error = string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardOutput
                : result.StandardError;
            var safeArgs = finalArgs.Select(SanitizeForLog).ToArray();
            var safeError = SanitizeForLog(error);
            logger.LogWarning("Git command failed: git {Args}; exit={ExitCode}; error={Error}", string.Join(' ', safeArgs), result.ExitCode, safeError);
            throw new InvalidOperationException(safeError.Trim());
        }

        return result.StandardOutput.Trim();
    }

    private static string SanitizeForLog(string arg)
    {
        if (string.IsNullOrWhiteSpace(arg))
        {
            return arg;
        }

        const string gitHubAuthPrefix = "http.https://github.com/.extraheader=Authorization: Basic ";
        if (arg.StartsWith(gitHubAuthPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return $"{gitHubAuthPrefix}[REDACTED]";
        }

        return s_urlCredentialPattern.Replace(arg, "[REDACTED]");
    }

    private static string NormalizeRemote(string remote)
    {
        var normalized = remote.Trim();
        if (normalized.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        normalized = normalized.Replace("git@github.com:", "https://github.com/", StringComparison.OrdinalIgnoreCase);
        return normalized.TrimEnd('/');
    }

    private static string ToBasicAuthToken(string token)
    {
        var bytes = Encoding.UTF8.GetBytes($"x-access-token:{token}");
        return Convert.ToBase64String(bytes);
    }
}
