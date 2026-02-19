using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.Contracts.Api;












public sealed record BulkOperationResult(int AffectedCount, List<string> Errors);
