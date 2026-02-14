using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public class WebhookServiceTests
{
    [Fact]
    public async Task RegisterAsync_DelegatesToStore()
    {
        var store = new Mock<OrchestratorStore>() { CallBase = false };
        var request = new CreateWebhookRequest("repo-1", "task-1", "push", "secret123");
        var expected = new WebhookRegistration
        {
            RepositoryId = "repo-1",
            TaskId = "task-1",
            EventFilter = "push",
            Secret = "secret123"
        };

        store.Setup(s => s.CreateWebhookAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var service = new WebhookService(store.Object, NullLogger<WebhookService>.Instance);

        var result = await service.RegisterAsync(request, CancellationToken.None);

        result.Should().Be(expected);
        store.Verify(s => s.CreateWebhookAsync(request, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListAsync_DelegatesToStore()
    {
        var store = new Mock<OrchestratorStore>() { CallBase = false };
        var webhooks = new List<WebhookRegistration>
        {
            new() { RepositoryId = "repo-1", TaskId = "task-1" },
            new() { RepositoryId = "repo-1", TaskId = "task-2" }
        };

        store.Setup(s => s.ListWebhooksAsync("repo-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(webhooks);

        var service = new WebhookService(store.Object, NullLogger<WebhookService>.Instance);

        var result = await service.ListAsync("repo-1", CancellationToken.None);

        result.Should().HaveCount(2);
        result.Should().BeEquivalentTo(webhooks);
    }

    [Fact]
    public async Task ListAsync_EmptyRepository_ReturnsEmpty()
    {
        var store = new Mock<OrchestratorStore>() { CallBase = false };
        store.Setup(s => s.ListWebhooksAsync("empty-repo", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WebhookRegistration>());

        var service = new WebhookService(store.Object, NullLogger<WebhookService>.Instance);

        var result = await service.ListAsync("empty-repo", CancellationToken.None);

        result.Should().BeEmpty();
    }
}
