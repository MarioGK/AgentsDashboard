

namespace AgentsDashboard.Contracts.Features.Runs.Models.Api;












public sealed record BulkOperationResult(int AffectedCount, List<string> Errors);
