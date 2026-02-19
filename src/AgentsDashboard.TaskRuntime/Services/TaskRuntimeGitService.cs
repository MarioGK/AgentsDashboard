using System.Diagnostics;
using AgentsDashboard.Contracts.TaskRuntime;

namespace AgentsDashboard.TaskRuntime.Services;

public sealed class TaskRuntimeGitService(
    WorkspacePathGuard workspacePathGuard,
    ILogger<TaskRuntimeGitService> logger)
{
    public async ValueTask<GitStatusDto> StatusAsync(GitStatusRequest request, CancellationToken cancellationToken)
    {
        var repositoryPath = ResolveRepositoryPath(request.RepositoryPath);
        await EnsureRepositoryAsync(repositoryPath, cancellationToken);

        var arguments = new List<string>
        {
            "status",
            "--porcelain=1",
            "--branch",
        };

        if (!request.IncludeUntracked)
        {
            arguments.Add("--untracked-files=no");
        }

        var statusResult = await RunGitAsync(repositoryPath, arguments, null, cancellationToken);
        EnsureCommandSucceeded("status", statusResult);

        var lines = SplitLines(statusResult.StandardOutput);

        string branch = string.Empty;
        var aheadBy = 0;
        var behindBy = 0;
        var entries = new List<GitStatusEntryDto>();

        foreach (var line in lines)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                ParseBranchLine(line, ref branch, ref aheadBy, ref behindBy);
                continue;
            }

            if (line.Length < 3)
            {
                continue;
            }

            var indexStatus = line[0].ToString();
            var workTreeStatus = line[1].ToString();
            var path = line[3..].Trim();

            var renameMarker = path.IndexOf("->", StringComparison.Ordinal);
            if (renameMarker >= 0)
            {
                path = path[(renameMarker + 2)..].Trim();
            }

            entries.Add(new GitStatusEntryDto
            {
                Path = path.Replace('\\', '/'),
                IndexStatus = indexStatus,
                WorkTreeStatus = workTreeStatus,
            });
        }

        return new GitStatusDto
        {
            Branch = branch,
            IsClean = entries.Count == 0,
            AheadBy = aheadBy,
            BehindBy = behindBy,
            Entries = entries,
        };
    }

    public async ValueTask<GitDiffDto> DiffAsync(GitDiffRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var repositoryPath = ResolveRepositoryPath(request.RepositoryPath);
            await EnsureRepositoryAsync(repositoryPath, cancellationToken);

            var arguments = new List<string> { "diff" };

            if (request.Staged)
            {
                arguments.Add("--staged");
            }

            if (!string.IsNullOrWhiteSpace(request.BaseRef))
            {
                arguments.Add(request.BaseRef.Trim());
            }

            if (!string.IsNullOrWhiteSpace(request.TargetRef))
            {
                arguments.Add(request.TargetRef.Trim());
            }

            if (!string.IsNullOrWhiteSpace(request.Pathspec))
            {
                arguments.Add("--");
                arguments.Add(request.Pathspec.Trim());
            }

            var diffResult = await RunGitAsync(repositoryPath, arguments, null, cancellationToken);

            return new GitDiffDto
            {
                Success = diffResult.ExitCode == 0,
                ErrorMessage = diffResult.ExitCode == 0 ? null : BuildErrorMessage(diffResult),
                HasChanges = !string.IsNullOrWhiteSpace(diffResult.StandardOutput),
                DiffText = diffResult.StandardOutput,
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Git diff failed for repository path {RepositoryPath}", request.RepositoryPath);
            return new GitDiffDto
            {
                Success = false,
                ErrorMessage = ex.Message,
                HasChanges = false,
                DiffText = string.Empty,
            };
        }
    }

    public async ValueTask<GitCommitResult> CommitAsync(GitCommitRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var repositoryPath = ResolveRepositoryPath(request.RepositoryPath);
            await EnsureRepositoryAsync(repositoryPath, cancellationToken);

            var stageResult = await RunGitAsync(repositoryPath, ["add", "-A"], null, cancellationToken);
            EnsureCommandSucceeded("add", stageResult);

            Dictionary<string, string>? environment = null;
            if (!string.IsNullOrWhiteSpace(request.AuthorName) || !string.IsNullOrWhiteSpace(request.AuthorEmail))
            {
                environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (!string.IsNullOrWhiteSpace(request.AuthorName))
                {
                    environment["GIT_AUTHOR_NAME"] = request.AuthorName.Trim();
                    environment["GIT_COMMITTER_NAME"] = request.AuthorName.Trim();
                }

                if (!string.IsNullOrWhiteSpace(request.AuthorEmail))
                {
                    environment["GIT_AUTHOR_EMAIL"] = request.AuthorEmail.Trim();
                    environment["GIT_COMMITTER_EMAIL"] = request.AuthorEmail.Trim();
                }
            }

            var commitArguments = new List<string>
            {
                "commit",
                "-m",
                request.Message,
            };

            if (request.Amend)
            {
                commitArguments.Add("--amend");
            }

            if (request.AllowEmpty)
            {
                commitArguments.Add("--allow-empty");
            }

            var commitResult = await RunGitAsync(repositoryPath, commitArguments, environment, cancellationToken);

            if (commitResult.ExitCode != 0 && !ContainsNothingToCommit(commitResult))
            {
                return new GitCommitResult
                {
                    Success = false,
                    ErrorMessage = BuildErrorMessage(commitResult),
                    CommitSha = null,
                };
            }

            var headSha = await TryGetHeadShaAsync(repositoryPath, cancellationToken);

            return new GitCommitResult
            {
                Success = true,
                ErrorMessage = null,
                CommitSha = string.IsNullOrWhiteSpace(headSha) ? null : headSha,
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Git commit failed for repository path {RepositoryPath}", request.RepositoryPath);
            return new GitCommitResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                CommitSha = null,
            };
        }
    }

    public async ValueTask<GitPushResult> PushAsync(GitPushRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var repositoryPath = ResolveRepositoryPath(request.RepositoryPath);
            await EnsureRepositoryAsync(repositoryPath, cancellationToken);

            var branch = string.IsNullOrWhiteSpace(request.Branch)
                ? await GetCurrentReferenceAsync(repositoryPath, cancellationToken)
                : request.Branch.Trim();

            var arguments = new List<string> { "push" };
            if (request.SetUpstream)
            {
                arguments.Add("--set-upstream");
            }

            arguments.Add(request.Remote);
            arguments.Add(branch);

            var pushResult = await RunGitAsync(repositoryPath, arguments, null, cancellationToken);
            var summary = BuildSummary(pushResult);

            return new GitPushResult
            {
                Success = pushResult.ExitCode == 0,
                ErrorMessage = pushResult.ExitCode == 0 ? null : BuildErrorMessage(pushResult),
                Summary = summary,
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Git push failed for repository path {RepositoryPath}", request.RepositoryPath);
            return new GitPushResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                Summary = string.Empty,
            };
        }
    }

    public async ValueTask<GitFetchResult> FetchAsync(GitFetchRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var repositoryPath = ResolveRepositoryPath(request.RepositoryPath);
            await EnsureRepositoryAsync(repositoryPath, cancellationToken);

            var arguments = new List<string> { "fetch", request.Remote };
            if (request.Prune)
            {
                arguments.Add("--prune");
            }

            var fetchResult = await RunGitAsync(repositoryPath, arguments, null, cancellationToken);
            var summary = BuildSummary(fetchResult);

            return new GitFetchResult
            {
                Success = fetchResult.ExitCode == 0,
                ErrorMessage = fetchResult.ExitCode == 0 ? null : BuildErrorMessage(fetchResult),
                Summary = summary,
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Git fetch failed for repository path {RepositoryPath}", request.RepositoryPath);
            return new GitFetchResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                Summary = string.Empty,
            };
        }
    }

    public async ValueTask<GitCheckoutResult> CheckoutAsync(GitCheckoutRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var repositoryPath = ResolveRepositoryPath(request.RepositoryPath);
            await EnsureRepositoryAsync(repositoryPath, cancellationToken);

            var arguments = new List<string> { "checkout" };

            if (request.CreateBranch)
            {
                arguments.Add(request.Force ? "-B" : "-b");
            }
            else if (request.Force)
            {
                arguments.Add("-f");
            }

            arguments.Add(request.Reference.Trim());

            var checkoutResult = await RunGitAsync(repositoryPath, arguments, null, cancellationToken);
            if (checkoutResult.ExitCode != 0)
            {
                return new GitCheckoutResult
                {
                    Success = false,
                    ErrorMessage = BuildErrorMessage(checkoutResult),
                    CurrentReference = string.Empty,
                };
            }

            var currentReference = await GetCurrentReferenceAsync(repositoryPath, cancellationToken);
            return new GitCheckoutResult
            {
                Success = true,
                ErrorMessage = null,
                CurrentReference = currentReference,
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Git checkout failed for repository path {RepositoryPath}", request.RepositoryPath);
            return new GitCheckoutResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                CurrentReference = string.Empty,
            };
        }
    }

    private string ResolveRepositoryPath(string repositoryPath)
    {
        return workspacePathGuard.ResolvePath(repositoryPath);
    }

    private static async Task EnsureRepositoryAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(repositoryPath))
        {
            throw new DirectoryNotFoundException($"Repository path '{repositoryPath}' does not exist.");
        }

        var validationResult = await RunGitAsync(
            repositoryPath,
            ["rev-parse", "--is-inside-work-tree"],
            null,
            cancellationToken);

        if (validationResult.ExitCode != 0)
        {
            throw new InvalidOperationException(BuildErrorMessage(validationResult));
        }
    }

    private static async Task<string> GetCurrentReferenceAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        var currentReferenceResult = await RunGitAsync(
            repositoryPath,
            ["rev-parse", "--abbrev-ref", "HEAD"],
            null,
            cancellationToken);

        EnsureCommandSucceeded("rev-parse", currentReferenceResult);
        return currentReferenceResult.StandardOutput.Trim();
    }

    private static async Task<string?> TryGetHeadShaAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        var headResult = await RunGitAsync(repositoryPath, ["rev-parse", "HEAD"], null, cancellationToken);
        return headResult.ExitCode == 0
            ? headResult.StandardOutput.Trim()
            : null;
    }

    private static string[] SplitLines(string value)
    {
        return value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static void ParseBranchLine(string line, ref string branch, ref int aheadBy, ref int behindBy)
    {
        var content = line[3..].Trim();

        var bracketStart = content.IndexOf('[', StringComparison.Ordinal);
        if (bracketStart >= 0)
        {
            var bracketEnd = content.IndexOf(']', bracketStart + 1);
            var trackedState = bracketEnd > bracketStart
                ? content[(bracketStart + 1)..bracketEnd]
                : content[(bracketStart + 1)..];

            foreach (var part in trackedState.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (part.StartsWith("ahead ", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(part[6..], out var aheadValue))
                {
                    aheadBy = aheadValue;
                }

                if (part.StartsWith("behind ", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(part[7..], out var behindValue))
                {
                    behindBy = behindValue;
                }
            }

            content = content[..bracketStart].Trim();
        }

        var trackingSeparator = content.IndexOf("...", StringComparison.Ordinal);
        branch = trackingSeparator >= 0
            ? content[..trackingSeparator].Trim()
            : content;
    }

    private static void EnsureCommandSucceeded(string operation, (int ExitCode, string StandardOutput, string StandardError) result)
    {
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {operation} failed: {BuildErrorMessage(result)}");
        }
    }

    private static string BuildErrorMessage((int ExitCode, string StandardOutput, string StandardError) result)
    {
        var details = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;

        var trimmed = details.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            trimmed = "unknown git error";
        }

        return $"exit code {result.ExitCode}: {trimmed}";
    }

    private static string BuildSummary((int ExitCode, string StandardOutput, string StandardError) result)
    {
        var summary = string.IsNullOrWhiteSpace(result.StandardOutput)
            ? result.StandardError
            : result.StandardOutput;

        return summary.Trim();
    }

    private static bool ContainsNothingToCommit((int ExitCode, string StandardOutput, string StandardError) result)
    {
        var combined = string.Concat(result.StandardOutput, "\n", result.StandardError);
        return combined.Contains("nothing to commit", StringComparison.OrdinalIgnoreCase)
               || combined.Contains("no changes added to commit", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunGitAsync(
        string repositoryPath,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? additionalEnvironment,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = repositoryPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (additionalEnvironment is not null)
        {
            foreach (var pair in additionalEnvironment)
            {
                process.StartInfo.Environment[pair.Key] = pair.Value;
            }
        }

        if (!process.Start())
        {
            return (-1, string.Empty, "Failed to start git process.");
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

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
            await standardOutputTask,
            await standardErrorTask);
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
