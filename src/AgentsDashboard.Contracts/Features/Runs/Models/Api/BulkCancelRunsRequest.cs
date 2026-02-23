

namespace AgentsDashboard.Contracts.Features.Runs.Models.Api;












public sealed record BulkCancelRunsRequest(List<string> RunIds);
