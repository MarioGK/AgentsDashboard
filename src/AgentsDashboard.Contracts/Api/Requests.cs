using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.Contracts.Api;












public sealed record CreateRepositoryRequest(string Name, string GitUrl, string LocalPath, string DefaultBranch);
