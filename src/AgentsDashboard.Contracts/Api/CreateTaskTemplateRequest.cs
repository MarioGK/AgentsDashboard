using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.Contracts.Api;












public sealed record CreateTaskTemplateRequest(
    string TemplateId,
    string Name,
    string Description,
    TaskKind Kind,
    string Harness,
    string Prompt,
    List<string> Commands,
    string CronExpression,
    bool AutoCreatePullRequest,
    RetryPolicyConfig? RetryPolicy = null,
    TimeoutConfig? Timeouts = null,
    SandboxProfileConfig? SandboxProfile = null,
    ArtifactPolicyConfig? ArtifactPolicy = null,
    List<string>? ArtifactPatterns = null,
    List<string>? LinkedFailureRuns = null);
