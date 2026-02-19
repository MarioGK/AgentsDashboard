using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.Contracts.Api;












public sealed record CreateRepositoryInstructionRequest(string Name, string Content, int Priority, bool Enabled = true);
