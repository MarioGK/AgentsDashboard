using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.Contracts.Api;

public sealed record UpdateRepositoryTaskDefaultsRequest(
    TaskKind Kind,
    string Harness,
    HarnessExecutionMode ExecutionModeDefault,
    string Command,
    string CronExpression,
    bool AutoCreatePullRequest,
    bool Enabled,
    string? SessionProfileId = null);
