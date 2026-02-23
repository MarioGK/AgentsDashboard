

namespace AgentsDashboard.Contracts.Features.Repositories.Models.Api;












public sealed record CreateTaskRequest(
    string RepositoryId,
    string Prompt,
    string? Name = null,
    List<string>? LinkedFailureRuns = null);
