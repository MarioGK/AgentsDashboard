namespace AgentsDashboard.ControlPlane.Features.Repositories.Services;

public sealed record GitWorkspaceOperationResult(
    RepositoryGitStatus GitStatus,
    string EffectiveGitUrl,
    string WorkspacePath,
    IReadOnlyList<RepositoryGitOperationAttempt> Attempts);
