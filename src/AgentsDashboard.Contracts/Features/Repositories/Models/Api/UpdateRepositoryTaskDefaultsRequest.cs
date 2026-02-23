using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.Contracts.Api;

public sealed record UpdateRepositoryTaskDefaultsRequest(
    string Harness,
    HarnessExecutionMode ExecutionModeDefault,
    string Command,
    bool AutoCreatePullRequest,
    bool Enabled,
    string? SessionProfileId = null);
