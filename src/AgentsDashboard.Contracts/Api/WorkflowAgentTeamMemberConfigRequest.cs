using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.Contracts.Api;












public sealed record WorkflowAgentTeamMemberConfigRequest(
    string Name,
    string Harness,
    HarnessExecutionMode Mode,
    string RolePrompt,
    string? ModelOverride = null,
    int? TimeoutSeconds = null);
