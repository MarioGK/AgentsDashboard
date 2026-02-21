using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.Contracts.Api;












public sealed record CreateTaskRequest(
    string RepositoryId,
    string Prompt,
    string? Name = null,
    List<string>? LinkedFailureRuns = null);
