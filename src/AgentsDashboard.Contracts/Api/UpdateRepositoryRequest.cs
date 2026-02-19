using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.Contracts.Api;












public sealed record UpdateRepositoryRequest(string Name, string GitUrl, string LocalPath, string DefaultBranch);
