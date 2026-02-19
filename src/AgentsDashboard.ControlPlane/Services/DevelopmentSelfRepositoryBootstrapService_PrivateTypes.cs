using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;
using CliWrap;
using CliWrap.Buffered;

namespace AgentsDashboard.ControlPlane.Services;

public sealed partial class DevelopmentSelfRepositoryBootstrapService
    IHostEnvironment hostEnvironment,
    IBackgroundWorkCoordinator backgroundWorkCoordinator,
    IOrchestratorStore store,
    IGitWorkspaceService gitWorkspace,
    ILogger<DevelopmentSelfRepositoryBootstrapService> logger) : IHostedService
{
    private sealed record RepositorySeed(
        string Name,
        string GitUrl,
        string LocalPath,
        string DefaultBranch);
}
