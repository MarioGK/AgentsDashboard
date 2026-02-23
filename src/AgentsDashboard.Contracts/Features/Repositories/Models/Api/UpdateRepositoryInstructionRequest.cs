

namespace AgentsDashboard.Contracts.Features.Repositories.Models.Api;












public sealed record UpdateRepositoryInstructionRequest(string Name, string Content, int Priority, bool Enabled);
