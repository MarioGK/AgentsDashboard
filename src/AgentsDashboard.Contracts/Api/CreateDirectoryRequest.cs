using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.Contracts.Api;












public sealed record CreateDirectoryRequest(string ParentPath, string Name);
