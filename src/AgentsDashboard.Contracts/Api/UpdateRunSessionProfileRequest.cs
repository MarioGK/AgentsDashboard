using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.Contracts.Api;












public sealed record UpdateRunSessionProfileRequest(
    string Name,
    string Harness,
    HarnessExecutionMode ExecutionModeDefault,
    string ApprovalMode,
    string DiffViewDefault,
    string ToolTimelineMode,
    string McpConfigJson,
    bool Enabled = true);
