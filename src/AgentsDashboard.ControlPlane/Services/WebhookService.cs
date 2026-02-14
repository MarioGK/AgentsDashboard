using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class WebhookService(OrchestratorStore store, ILogger<WebhookService> logger)
{
    public async Task<WebhookRegistration> RegisterAsync(CreateWebhookRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Registering webhook for repo {RepositoryId}, task {TaskId}", request.RepositoryId, request.TaskId);
        return await store.CreateWebhookAsync(request, cancellationToken);
    }

    public async Task<List<WebhookRegistration>> ListAsync(string repositoryId, CancellationToken cancellationToken)
    {
        return await store.ListWebhooksAsync(repositoryId, cancellationToken);
    }
}
