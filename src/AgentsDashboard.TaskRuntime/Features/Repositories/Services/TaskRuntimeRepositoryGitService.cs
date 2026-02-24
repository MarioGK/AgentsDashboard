using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.TaskRuntime.Features.Repositories.Services;

public sealed class TaskRuntimeRepositoryGitService(
    IOptions<TaskRuntimeOptions> options,
    ILogger<TaskRuntimeRepositoryGitService> logger)
{
    private static readonly Regex s_urlCredentialPattern = new(@"(?<=https?://)[^/\s@]+(?=@)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public async Task<RepositoryWorkspaceResult> EnsureRepositoryWorkspaceAsync(
        EnsureRepositoryWorkspaceRequest request,
        CancellationToken cancellationToken)
    {
        var attempts = new List<RepositoryGitOperationAttempt>();

        try
        {
            if (!TryNormalizeCloneUrl(request.GitUrl, out var normalizedGitUrl, out var normalizeError))
            {
                throw new InvalidOperationException(normalizeError);
            }

            var workspacePath = BuildRepositoryWorkspacePath(request.RepositoryId, request.RepositoryKeyHint, normalizedGitUrl);
            var defaultBranch = string.IsNullOrWhiteSpace(request.DefaultBranch) ? "main" : request.DefaultBranch.Trim();
            var githubToken = ResolveGitHubToken(request.GitHubToken);

            var effectiveGitUrl = await EnsureWorkspaceInternalAsync(
                normalizedGitUrl,
                workspacePath,
                defaultBranch,
                githubToken,
                request.FetchRemote,
                attempts,
                cancellationToken);

            var status = await InspectAsync(workspacePath, githubToken, request.FetchRemote, cancellationToken);

            return new RepositoryWorkspaceResult
            {
                Success = true,
                ErrorMessage = null,
                EffectiveGitUrl = effectiveGitUrl,
                WorkspacePath = workspacePath,
                GitStatus = status,
                Attempts = attempts,
                CompletedAt = DateTimeOffset.UtcNow,
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ensure repository workspace failed");

            return new RepositoryWorkspaceResult
            {
                Success = false,
                ErrorMessage = SanitizeForLog(ex.Message),
                EffectiveGitUrl = string.Empty,
                WorkspacePath = string.Empty,
                GitStatus = EmptyStatus(SanitizeForLog(ex.Message)),
                Attempts = attempts,
                CompletedAt = DateTimeOffset.UtcNow,
            };
        }
    }

    public async Task<RepositoryWorkspaceResult> RefreshRepositoryWorkspaceAsync(
        RefreshRepositoryWorkspaceRequest request,
        CancellationToken cancellationToken)
    {
        var ensureRequest = new EnsureRepositoryWorkspaceRequest
        {
            RepositoryId = request.RepositoryId,
            GitUrl = request.GitUrl,
            DefaultBranch = request.DefaultBranch,
            GitHubToken = request.GitHubToken,
            FetchRemote = request.FetchRemote,
            RepositoryKeyHint = request.LocalPath,
        };

        return await EnsureRepositoryWorkspaceAsync(ensureRequest, cancellationToken);
    }

    private async Task<string> EnsureWorkspaceInternalAsync(
        string gitUrl,
        string workspacePath,
        string defaultBranch,
        string? githubToken,
        bool fetchRemote,
        List<RepositoryGitOperationAttempt> attempts,
        CancellationToken cancellationToken)
    {
        var gitDirectory = Path.Combine(workspacePath, ".git");
        if (!Directory.Exists(gitDirectory))
        {
            if (Directory.Exists(workspacePath))
            {
                Directory.Delete(workspacePath, true);
            }

            return await CloneWithFallbackAsync(gitUrl, workspacePath, defaultBranch, githubToken, attempts, cancellationToken);
        }

        Directory.CreateDirectory(workspacePath);

        var currentOrigin = await TryRunGitAsync(
            ["-C", workspacePath, "remote", "get-url", "origin"],
            null,
            cancellationToken,
            Directory.GetCurrentDirectory());

        if (!string.IsNullOrWhiteSpace(currentOrigin) &&
            string.Equals(NormalizeRemote(currentOrigin), NormalizeRemote(gitUrl), StringComparison.OrdinalIgnoreCase))
        {
            if (fetchRemote)
            {
                var fetchResult = await RunGitAsync(
                    ["-C", workspacePath, "fetch", "--all", "--prune"],
                    githubToken,
                    cancellationToken,
                    Directory.GetCurrentDirectory());

                if (fetchResult.ExitCode != 0)
                {
                    attempts.Add(new RepositoryGitOperationAttempt
                    {
                        Strategy = "fetch",
                        GitUrl = gitUrl,
                        Success = false,
                        ExitCode = fetchResult.ExitCode,
                        ErrorMessage = BuildFailureMessage(fetchResult),
                    });

                    Directory.Delete(workspacePath, true);
                    return await CloneWithFallbackAsync(gitUrl, workspacePath, defaultBranch, githubToken, attempts, cancellationToken);
                }
            }

            return currentOrigin.Trim();
        }

        Directory.Delete(workspacePath, true);
        return await CloneWithFallbackAsync(gitUrl, workspacePath, defaultBranch, githubToken, attempts, cancellationToken);
    }

    private async Task<string> CloneWithFallbackAsync(
        string gitUrl,
        string workspacePath,
        string defaultBranch,
        string? githubToken,
        List<RepositoryGitOperationAttempt> attempts,
        CancellationToken cancellationToken)
    {
        if (!IsGitHubCloneUrl(gitUrl) || !TryParseGitHubRepoSlug(gitUrl, out var repoSlug))
        {
            var directResult = await ExecuteGitCloneAsync(gitUrl, workspacePath, defaultBranch, githubToken, cancellationToken);
            attempts.Add(new RepositoryGitOperationAttempt
            {
                Strategy = "direct",
                GitUrl = gitUrl,
                Success = directResult.ExitCode == 0,
                ExitCode = directResult.ExitCode,
                ErrorMessage = directResult.ExitCode == 0 ? string.Empty : BuildFailureMessage(directResult),
            });

            if (directResult.ExitCode != 0)
            {
                throw new InvalidOperationException($"clone failed: {BuildFailureMessage(directResult)}");
            }

            return gitUrl;
        }

        var sshUrl = BuildGitHubSshCloneUrl(repoSlug);
        var httpsUrl = BuildGitHubHttpsCloneUrl(repoSlug);

        var sshResult = await ExecuteGitCloneAsync(sshUrl, workspacePath, defaultBranch, githubToken: null, cancellationToken);
        attempts.Add(new RepositoryGitOperationAttempt
        {
            Strategy = "ssh",
            GitUrl = sshUrl,
            Success = sshResult.ExitCode == 0,
            ExitCode = sshResult.ExitCode,
            ErrorMessage = sshResult.ExitCode == 0 ? string.Empty : BuildFailureMessage(sshResult),
        });

        if (sshResult.ExitCode == 0)
        {
            return sshUrl;
        }

        DeleteDirectoryIfExists(workspacePath);

        var ghResult = await ExecuteGhCloneAsync(repoSlug, workspacePath, defaultBranch, githubToken, cancellationToken);
        attempts.Add(new RepositoryGitOperationAttempt
        {
            Strategy = "gh",
            GitUrl = sshUrl,
            Success = ghResult.ExitCode == 0,
            ExitCode = ghResult.ExitCode,
            ErrorMessage = ghResult.ExitCode == 0 ? string.Empty : BuildFailureMessage(ghResult),
        });

        if (ghResult.ExitCode == 0)
        {
            var originUrl = await TryRunGitAsync(
                ["-C", workspacePath, "remote", "get-url", "origin"],
                null,
                cancellationToken,
                Directory.GetCurrentDirectory());

            return string.IsNullOrWhiteSpace(originUrl) ? sshUrl : originUrl.Trim();
        }

        DeleteDirectoryIfExists(workspacePath);

        var httpsResult = await ExecuteGitCloneAsync(httpsUrl, workspacePath, defaultBranch, githubToken, cancellationToken);
        attempts.Add(new RepositoryGitOperationAttempt
        {
            Strategy = "https",
            GitUrl = httpsUrl,
            Success = httpsResult.ExitCode == 0,
            ExitCode = httpsResult.ExitCode,
            ErrorMessage = httpsResult.ExitCode == 0 ? string.Empty : BuildFailureMessage(httpsResult),
        });

        if (httpsResult.ExitCode == 0)
        {
            return httpsUrl;
        }

        throw new InvalidOperationException(
            $"clone failed (ssh, gh, https): ssh={BuildFailureMessage(sshResult)} | gh={BuildFailureMessage(ghResult)} | https={BuildFailureMessage(httpsResult)}");
    }

    private async Task<RepositoryGitStatusSnapshot> InspectAsync(
        string localPath,
        string? githubToken,
        bool fetchRemote,
        CancellationToken cancellationToken)
    {
        DateTime? fetchedAt = null;
        if (fetchRemote)
        {
            var fetchResult = await RunGitAsync(
                ["-C", localPath, "fetch", "--all", "--prune"],
                githubToken,
                cancellationToken,
                Directory.GetCurrentDirectory());

            if (fetchResult.ExitCode != 0)
            {
                throw new InvalidOperationException(BuildFailureMessage(fetchResult));
            }

            fetchedAt = DateTime.UtcNow;
        }

        var branchResult = await RunGitAsync(
            ["-C", localPath, "rev-parse", "--abbrev-ref", "HEAD"],
            githubToken,
            cancellationToken,
            Directory.GetCurrentDirectory());
        if (branchResult.ExitCode != 0)
        {
            throw new InvalidOperationException(BuildFailureMessage(branchResult));
        }

        var commitResult = await RunGitAsync(
            ["-C", localPath, "rev-parse", "HEAD"],
            githubToken,
            cancellationToken,
            Directory.GetCurrentDirectory());
        if (commitResult.ExitCode != 0)
        {
            throw new InvalidOperationException(BuildFailureMessage(commitResult));
        }

        var (aheadCount, behindCount) = await ReadAheadBehindAsync(localPath, githubToken, cancellationToken);
        var (stagedCount, modifiedCount, untrackedCount) = await ReadWorkingTreeCountsAsync(localPath, githubToken, cancellationToken);

        return new RepositoryGitStatusSnapshot
        {
            CurrentBranch = branchResult.StandardOutput.Trim(),
            CurrentCommit = commitResult.StandardOutput.Trim(),
            AheadCount = aheadCount,
            BehindCount = behindCount,
            ModifiedCount = modifiedCount,
            StagedCount = stagedCount,
            UntrackedCount = untrackedCount,
            ScannedAtUtc = DateTime.UtcNow,
            FetchedAtUtc = fetchedAt,
            LastSyncError = string.Empty,
        };
    }

    private async Task<(int AheadCount, int BehindCount)> ReadAheadBehindAsync(
        string localPath,
        string? githubToken,
        CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(
            ["-C", localPath, "rev-list", "--left-right", "--count", "@{upstream}...HEAD"],
            githubToken,
            cancellationToken,
            Directory.GetCurrentDirectory());

        if (result.ExitCode != 0)
        {
            return (0, 0);
        }

        var parts = result.StandardOutput.Split(
            ['\t', ' '],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length < 2)
        {
            return (0, 0);
        }

        _ = int.TryParse(parts[0], out var behind);
        _ = int.TryParse(parts[1], out var ahead);
        return (ahead, behind);
    }

    private async Task<(int StagedCount, int ModifiedCount, int UntrackedCount)> ReadWorkingTreeCountsAsync(
        string localPath,
        string? githubToken,
        CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(
            ["-C", localPath, "status", "--porcelain"],
            githubToken,
            cancellationToken,
            Directory.GetCurrentDirectory());

        if (result.ExitCode != 0)
        {
            return (0, 0, 0);
        }

        var staged = 0;
        var modified = 0;
        var untracked = 0;

        foreach (var rawLine in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
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
            var workingTreeStatus = line[1];

            if (indexStatus != ' ' && indexStatus != '?')
            {
                staged++;
            }

            if (workingTreeStatus != ' ' && workingTreeStatus != '?')
            {
                modified++;
            }
        }

        return (staged, modified, untracked);
    }

    private async Task<(int ExitCode, string StandardOutput, string StandardError)> ExecuteGitCloneAsync(
        string cloneUrl,
        string workspacePath,
        string defaultBranch,
        string? githubToken,
        CancellationToken cancellationToken)
    {
        var args = new List<string> { "clone" };

        if (!string.IsNullOrWhiteSpace(defaultBranch))
        {
            args.Add("--branch");
            args.Add(defaultBranch);
        }

        args.Add(cloneUrl);
        args.Add(workspacePath);

        return await RunGitAsync(args, githubToken, cancellationToken, Directory.GetCurrentDirectory());
    }

    private async Task<(int ExitCode, string StandardOutput, string StandardError)> ExecuteGhCloneAsync(
        string repoSlug,
        string workspacePath,
        string defaultBranch,
        string? githubToken,
        CancellationToken cancellationToken)
    {
        var args = new List<string> { "repo", "clone", repoSlug, workspacePath };

        if (!string.IsNullOrWhiteSpace(defaultBranch))
        {
            args.Add("--");
            args.Add("--branch");
            args.Add(defaultBranch);
        }

        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GIT_TERMINAL_PROMPT"] = "0",
        };

        if (!string.IsNullOrWhiteSpace(githubToken))
        {
            environment["GH_TOKEN"] = githubToken;
            environment["GITHUB_TOKEN"] = githubToken;
        }

        return await ExecuteProcessAsync("gh", args, Directory.GetCurrentDirectory(), environment, cancellationToken);
    }

    private async Task<(int ExitCode, string StandardOutput, string StandardError)> RunGitAsync(
        IReadOnlyList<string> args,
        string? githubToken,
        CancellationToken cancellationToken,
        string workingDirectory)
    {
        var finalArgs = new List<string>();
        var normalizedToken = ResolveGitHubToken(githubToken);

        if (!string.IsNullOrWhiteSpace(normalizedToken) &&
            args.Any(argument => argument.Contains("github.com", StringComparison.OrdinalIgnoreCase)))
        {
            finalArgs.Add("-c");
            finalArgs.Add($"http.https://github.com/.extraheader=Authorization: Basic {ToBasicAuthToken(normalizedToken)}");
        }

        finalArgs.AddRange(args);

        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GIT_TERMINAL_PROMPT"] = "0",
        };

        var sshAuthSock = Environment.GetEnvironmentVariable("SSH_AUTH_SOCK");
        if (!string.IsNullOrWhiteSpace(sshAuthSock))
        {
            environment["SSH_AUTH_SOCK"] = sshAuthSock.Trim();
        }

        var gitSshCommand = Environment.GetEnvironmentVariable("GIT_SSH_COMMAND");
        if (!string.IsNullOrWhiteSpace(gitSshCommand))
        {
            environment["GIT_SSH_COMMAND"] = gitSshCommand.Trim();
        }

        return await ExecuteProcessAsync("git", finalArgs, workingDirectory, environment, cancellationToken);
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> ExecuteProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            }
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                process.StartInfo.Environment[key] = value;
            }
        }

        if (!process.Start())
        {
            return (-1, string.Empty, $"Failed to start '{fileName}'.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKillProcess(process);
            throw;
        }

        return (
            process.ExitCode,
            await stdoutTask,
            await stderrTask);
    }

    private static async Task<string?> TryRunGitAsync(
        IReadOnlyList<string> args,
        string? githubToken,
        CancellationToken cancellationToken,
        string workingDirectory)
    {
        var finalArgs = new List<string>();

        if (!string.IsNullOrWhiteSpace(githubToken))
        {
            finalArgs.Add("-c");
            finalArgs.Add($"http.https://github.com/.extraheader=Authorization: Basic {ToBasicAuthToken(githubToken)}");
        }

        finalArgs.AddRange(args);

        var result = await ExecuteProcessAsync(
            "git",
            finalArgs,
            workingDirectory,
            new Dictionary<string, string> { ["GIT_TERMINAL_PROMPT"] = "0" },
            cancellationToken);

        if (result.ExitCode != 0)
        {
            return null;
        }

        var output = result.StandardOutput.Trim();
        return string.IsNullOrWhiteSpace(output) ? null : output;
    }

    private static string BuildFailureMessage((int ExitCode, string StandardOutput, string StandardError) result)
    {
        var details = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;

        if (string.IsNullOrWhiteSpace(details))
        {
            details = "unknown error";
        }

        return $"exit={result.ExitCode}; error={SanitizeForLog(details).Trim()}";
    }

    private string BuildRepositoryWorkspacePath(string? repositoryId, string? repositoryKeyHint, string gitUrl)
    {
        if (!string.IsNullOrWhiteSpace(repositoryId) &&
            !string.IsNullOrWhiteSpace(repositoryKeyHint) &&
            Path.IsPathRooted(repositoryKeyHint))
        {
            return Path.GetFullPath(repositoryKeyHint.Trim());
        }

        var key = repositoryId;
        if (string.IsNullOrWhiteSpace(key))
        {
            key = repositoryKeyHint;
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            key = BuildRepositoryKeyFromUrl(gitUrl);
        }

        var root = options.Value.WorkspacesRootPath;
        var repositoryPath = Path.Combine(root, ToPathSegment(key));
        Directory.CreateDirectory(repositoryPath);
        return Path.Combine(repositoryPath, "mirror");
    }

    private static string BuildRepositoryKeyFromUrl(string gitUrl)
    {
        var normalized = gitUrl.Trim().ToLowerInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        var hash = Convert.ToHexString(bytes).ToLowerInvariant();
        return $"repo-{hash[..12]}";
    }

    private static string ToPathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        return value.Trim().Replace('/', '-').Replace('\\', '-');
    }

    private static bool TryNormalizeCloneUrl(string? cloneUrl, out string normalizedCloneUrl, out string error)
    {
        normalizedCloneUrl = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(cloneUrl))
        {
            error = "Clone URL is required.";
            return false;
        }

        var trimmed = cloneUrl.Trim();
        if (IsScpStyleCloneUrl(trimmed) || IsSupportedCloneUrl(trimmed))
        {
            normalizedCloneUrl = trimmed;
            return true;
        }

        error = "Unsupported clone URL format.";
        return false;
    }

    private static bool IsSupportedCloneUrl(string cloneUrl)
    {
        if (string.IsNullOrWhiteSpace(cloneUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(cloneUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!uri.IsWellFormedOriginalString() || string.IsNullOrWhiteSpace(uri.Host))
        {
            return false;
        }

        return string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(uri.Scheme, Uri.UriSchemeSsh, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(uri.Scheme, "git", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(uri.Scheme, "git+ssh", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsScpStyleCloneUrl(string cloneUrl)
    {
        if (string.IsNullOrWhiteSpace(cloneUrl))
        {
            return false;
        }

        if (Uri.TryCreate(cloneUrl, UriKind.Absolute, out _))
        {
            return false;
        }

        if (cloneUrl.Contains(' '))
        {
            return false;
        }

        var atIndex = cloneUrl.IndexOf('@', StringComparison.Ordinal);
        if (atIndex <= 0)
        {
            return false;
        }

        var colonIndex = cloneUrl.IndexOf(':', StringComparison.Ordinal);
        if (colonIndex <= atIndex)
        {
            return false;
        }

        var host = cloneUrl[(atIndex + 1)..colonIndex];
        return !string.IsNullOrWhiteSpace(host) && !host.Contains('/');
    }

    private static bool IsGitHubCloneUrl(string gitUrl)
    {
        if (string.IsNullOrWhiteSpace(gitUrl))
        {
            return false;
        }

        if (IsScpStyleCloneUrl(gitUrl))
        {
            var atIndex = gitUrl.IndexOf('@', StringComparison.Ordinal);
            var colonIndex = gitUrl.IndexOf(':', StringComparison.Ordinal);
            if (atIndex < 0 || colonIndex <= atIndex)
            {
                return false;
            }

            var host = gitUrl[(atIndex + 1)..colonIndex];
            return string.Equals(host, "github.com", StringComparison.OrdinalIgnoreCase);
        }

        if (!Uri.TryCreate(gitUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseGitHubRepoSlug(string gitUrl, out string repoSlug)
    {
        repoSlug = string.Empty;
        if (!IsGitHubCloneUrl(gitUrl))
        {
            return false;
        }

        var normalized = gitUrl.Trim();
        if (normalized.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["git@github.com:".Length..];
        }
        else if (normalized.StartsWith("ssh://git@github.com/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["ssh://git@github.com/".Length..];
        }
        else if (normalized.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["https://github.com/".Length..];
        }
        else if (normalized.StartsWith("http://github.com/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["http://github.com/".Length..];
        }
        else if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri) &&
                 string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            normalized = uri.AbsolutePath.Trim('/');
        }

        if (normalized.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        normalized = normalized.Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2)
        {
            return false;
        }

        repoSlug = $"{segments[0]}/{segments[1]}";
        return true;
    }

    private static string BuildGitHubSshCloneUrl(string repoSlug)
    {
        return $"git@github.com:{repoSlug}.git";
    }

    private static string BuildGitHubHttpsCloneUrl(string repoSlug)
    {
        return $"https://github.com/{repoSlug}.git";
    }

    private static string? ResolveGitHubToken(string? requestToken)
    {
        if (!string.IsNullOrWhiteSpace(requestToken))
        {
            return requestToken.Trim();
        }

        var ghToken = Environment.GetEnvironmentVariable("GH_TOKEN");
        if (!string.IsNullOrWhiteSpace(ghToken))
        {
            return ghToken.Trim();
        }

        var githubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        return string.IsNullOrWhiteSpace(githubToken)
            ? null
            : githubToken.Trim();
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

    private static RepositoryGitStatusSnapshot EmptyStatus(string lastError)
    {
        return new RepositoryGitStatusSnapshot
        {
            CurrentBranch = string.Empty,
            CurrentCommit = string.Empty,
            AheadCount = 0,
            BehindCount = 0,
            ModifiedCount = 0,
            StagedCount = 0,
            UntrackedCount = 0,
            ScannedAtUtc = DateTime.UtcNow,
            FetchedAtUtc = null,
            LastSyncError = lastError,
        };
    }

    private static string SanitizeForLog(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        const string gitHubAuthPrefix = "http.https://github.com/.extraheader=Authorization: Basic ";
        if (value.StartsWith(gitHubAuthPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return $"{gitHubAuthPrefix}[REDACTED]";
        }

        return s_urlCredentialPattern.Replace(value, "[REDACTED]");
    }

    private static string ToBasicAuthToken(string token)
    {
        var bytes = Encoding.UTF8.GetBytes($"x-access-token:{token}");
        return Convert.ToBase64String(bytes);
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }
}
