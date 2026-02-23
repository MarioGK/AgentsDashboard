

namespace AgentsDashboard.Contracts.Features.Alerts.Models.Api;












public sealed record BulkResolveAlertsRequest(List<string> EventIds);
