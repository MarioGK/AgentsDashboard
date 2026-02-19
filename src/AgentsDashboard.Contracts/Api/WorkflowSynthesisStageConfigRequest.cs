using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.Contracts.Api;












public sealed record WorkflowSynthesisStageConfigRequest(
    bool Enabled,
    string Prompt,
    string? Harness = null,
    HarnessExecutionMode? Mode = null,
    string? ModelOverride = null,
    int? TimeoutSeconds = null);
