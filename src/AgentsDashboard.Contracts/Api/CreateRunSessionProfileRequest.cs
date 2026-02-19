using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.Contracts.Api;












public sealed record CreateRunSessionProfileRequest(
    string RepositoryId,
    string Name,
    string Harness,
    HarnessExecutionMode ExecutionModeDefault,
    string ApprovalMode,
    string DiffViewDefault,
    string ToolTimelineMode,
    string McpConfigJson,
    RunSessionProfileScope Scope = RunSessionProfileScope.Repository);
