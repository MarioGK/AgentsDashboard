using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.Contracts.Api;












public sealed record UpdateTaskRequest(
    string Name,
    string Harness,
    string Prompt,
    string Command,
    bool AutoCreatePullRequest,
    bool Enabled,
    RetryPolicyConfig? RetryPolicy = null,
    TimeoutConfig? Timeouts = null,
    SandboxProfileConfig? SandboxProfile = null,
    ArtifactPolicyConfig? ArtifactPolicy = null,
    ApprovalProfileConfig? ApprovalProfile = null,
    int? ConcurrencyLimit = null,
    List<InstructionFile>? InstructionFiles = null,
    List<string>? ArtifactPatterns = null,
    List<string>? LinkedFailureRuns = null,
    HarnessExecutionMode? ExecutionModeDefault = null,
    string? SessionProfileId = null);
