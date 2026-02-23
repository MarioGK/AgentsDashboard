using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.Contracts.Api;












public sealed record UpdateRepositoryInstructionRequest(string Name, string Content, int Priority, bool Enabled);
