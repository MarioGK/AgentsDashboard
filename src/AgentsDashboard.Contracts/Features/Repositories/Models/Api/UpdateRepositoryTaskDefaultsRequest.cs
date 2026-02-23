

namespace AgentsDashboard.Contracts.Features.Repositories.Models.Api;

public sealed record UpdateRepositoryTaskDefaultsRequest(
    string Harness,
    HarnessExecutionMode ExecutionModeDefault,
    string Command,
    bool AutoCreatePullRequest,
    bool Enabled,
    string? SessionProfileId = null);
