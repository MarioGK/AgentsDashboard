

namespace AgentsDashboard.Contracts.Features.Repositories.Models.Api;












public sealed record UpdateRepositoryRequest(string Name, string GitUrl, string LocalPath, string DefaultBranch);
