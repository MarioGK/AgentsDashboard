using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.Contracts.Api;












public sealed record CreateTaskFromFindingRequest(string Name, string Harness, string Command, string Prompt, List<string>? LinkedFailureRuns = null);
