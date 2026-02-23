

namespace AgentsDashboard.Contracts.Features.Repositories.Models.Api;












public sealed record CreateRepositoryRequest(string Name, string GitUrl, string LocalPath, string DefaultBranch);
