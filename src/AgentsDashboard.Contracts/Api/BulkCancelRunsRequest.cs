using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.Contracts.Api;












public sealed record BulkCancelRunsRequest(List<string> RunIds);
