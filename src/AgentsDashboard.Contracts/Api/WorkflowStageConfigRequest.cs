using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.Contracts.Api;












public sealed record WorkflowStageConfigRequest(
    string Name,
    WorkflowStageType Type,
    string? TaskId = null,
    string? PromptOverride = null,
    string? CommandOverride = null,
    int? DelaySeconds = null,
    List<string>? ParallelStageIds = null,
    List<WorkflowAgentTeamMemberConfigRequest>? AgentTeamMembers = null,
    WorkflowSynthesisStageConfigRequest? Synthesis = null,
    int? TimeoutMinutes = null,
    int Order = 0);
