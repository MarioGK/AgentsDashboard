using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.Contracts.Api;












public sealed record TaskRuntimeHeartbeatRequest(string TaskRuntimeId, string? Endpoint, int ActiveSlots, int MaxSlots);
