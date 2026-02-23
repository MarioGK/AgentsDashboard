using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.Contracts.Api;












public sealed record BulkResolveAlertsRequest(List<string> EventIds);
