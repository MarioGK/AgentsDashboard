using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.Contracts.Api;












public sealed record BuildImageRequest(string DockerfileContent, string Tag);
