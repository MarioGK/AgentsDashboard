using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.Contracts.Api;












public sealed record RefreshRepositoryGitStateRequest(string RepositoryId, bool FetchRemote = false);
