using System.Net;
using System.Net.Http.Json;
using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AgentsDashboard.IntegrationTests.Api;

[ClassDataSource<ApiTestFixture>(Shared = SharedType.Keyed, Key = "Api")]
public class WebhooksApiTests(ApiTestFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;
    private readonly WebApplicationFactory<AgentsDashboard.ControlPlane.Program> _factory = fixture.Factory;

    private async Task<(ProjectDocument Project, RepositoryDocument Repository, TaskDocument Task)> SetupAsync()
    {
        var projectResponse = await _client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("WebhookP", "d"));
        var project = (await projectResponse.Content.ReadFromJsonAsync<ProjectDocument>())!;

        var repoRequest = new CreateRepositoryRequest(project.Id, "WebhookR", "https://github.com/test/repo.git", "main");
        var repoResponse = await _client.PostAsJsonAsync("/api/repositories", repoRequest);
        var repo = (await repoResponse.Content.ReadFromJsonAsync<RepositoryDocument>())!;

        var taskRequest = new CreateTaskRequest(repo.Id, "EventTask", TaskKind.EventDriven, "codex", "prompt", "cmd", false, "", true);
        var taskResponse = await _client.PostAsJsonAsync("/api/tasks", taskRequest);
        var task = (await taskResponse.Content.ReadFromJsonAsync<TaskDocument>())!;

        return (project, repo, task);
    }

    [Test]
    public async Task CreateWebhook_ReturnsCreatedWebhook()
    {
        var (_, repo, task) = await SetupAsync();
        var request = new CreateWebhookRequest(repo.Id, task.Id, "push", "secret123");

        var response = await _client.PostAsJsonAsync("/api/webhooks", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task CreateWebhook_ReturnsNotFound_WhenRepositoryDoesNotExist()
    {
        var request = new CreateWebhookRequest("nonexistent", "task", "push", "secret");
        var response = await _client.PostAsJsonAsync("/api/webhooks", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task TriggerWebhook_ReturnsNotFound_WhenRepositoryDoesNotExist()
    {
        var response = await _client.PostAsync("/api/webhooks/nonexistent", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task TriggerWebhook_ReturnsOk_WhenRepositoryExists()
    {
        var (_, repo, _) = await SetupAsync();

        var response = await _client.PostAsync($"/api/webhooks/{repo.Id}", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task TriggerWebhook_DispatchesEventDrivenTask()
    {
        var (_, repo, task) = await SetupAsync();
        await _client.PostAsJsonAsync("/api/webhooks", new CreateWebhookRequest(repo.Id, task.Id, "push", "secret"));

        var response = await _client.PostAsync($"/api/webhooks/{repo.Id}", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<WebhookTriggerResult>();
        result!.Dispatched.Should().Be(1);
    }

    [Test]
    public async Task TriggerWebhook_ReturnsZero_WhenNoEventDrivenTasks()
    {
        var projectResponse = await _client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("WP2", "d"));
        var project = (await projectResponse.Content.ReadFromJsonAsync<ProjectDocument>())!;

        var repoRequest = new CreateRepositoryRequest(project.Id, "WR2", "https://github.com/test/repo.git", "main");
        var repoResponse = await _client.PostAsJsonAsync("/api/repositories", repoRequest);
        var repo = (await repoResponse.Content.ReadFromJsonAsync<RepositoryDocument>())!;

        var response = await _client.PostAsync($"/api/webhooks/{repo.Id}", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<WebhookTriggerResult>();
        result!.Dispatched.Should().Be(0);
    }

    private sealed record WebhookTriggerResult(int Dispatched);
}
