

namespace AgentsDashboard.Contracts.Features.Findings.Models.Api;












public sealed record CreateTaskFromFindingRequest(string Name, string Harness, string Command, string Prompt, List<string>? LinkedFailureRuns = null);
