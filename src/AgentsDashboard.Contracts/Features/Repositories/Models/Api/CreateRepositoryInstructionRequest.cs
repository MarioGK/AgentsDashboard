

namespace AgentsDashboard.Contracts.Features.Repositories.Models.Api;












public sealed record CreateRepositoryInstructionRequest(string Name, string Content, int Priority, bool Enabled = true);
