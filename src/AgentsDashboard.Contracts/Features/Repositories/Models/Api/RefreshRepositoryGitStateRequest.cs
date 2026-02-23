

namespace AgentsDashboard.Contracts.Features.Repositories.Models.Api;












public sealed record RefreshRepositoryGitStateRequest(string RepositoryId, bool FetchRemote = false);
