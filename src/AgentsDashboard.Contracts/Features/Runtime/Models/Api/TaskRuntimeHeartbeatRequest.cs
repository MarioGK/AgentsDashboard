

namespace AgentsDashboard.Contracts.Features.Runtime.Models.Api;












public sealed record TaskRuntimeHeartbeatRequest(string TaskRuntimeId, string? Endpoint, int ActiveSlots, int MaxSlots);
