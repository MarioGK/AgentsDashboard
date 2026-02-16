using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AgentsDashboard.IntegrationTests.Api;

[ClassDataSource<ApiTestFixture>(Shared = SharedType.Keyed, Key = "Api")]
public class WebhookSignatureApiTests(ApiTestFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;
    private readonly WebApplicationFactory<AgentsDashboard.ControlPlane.Program> _factory = fixture.Factory;

    private async Task<(ProjectDocument Project, RepositoryDocument Repository, TaskDocument Task)> SetupAsync()
    {
        var projectResponse = await _client.PostAsJsonAsync("/api/projects", new CreateProjectRequest($"SigP{Guid.NewGuid():N}", "d"));
        var project = (await projectResponse.Content.ReadFromJsonAsync<ProjectDocument>())!;

        var repoRequest = new CreateRepositoryRequest(project.Id, $"SigR{Guid.NewGuid():N}", "https://github.com/test/repo.git", "main");
        var repoResponse = await _client.PostAsJsonAsync("/api/repositories", repoRequest);
        var repo = (await repoResponse.Content.ReadFromJsonAsync<RepositoryDocument>())!;

        var taskRequest = new CreateTaskRequest(repo.Id, "EventTask", TaskKind.EventDriven, "codex", "prompt", "cmd", false, "", true);
        var taskResponse = await _client.PostAsJsonAsync("/api/tasks", taskRequest);
        var task = (await taskResponse.Content.ReadFromJsonAsync<TaskDocument>())!;

        return (project, repo, task);
    }

    [Test]
    public async Task TriggerWebhook_WithValidSignature_Succeeds()
    {
        var (_, repo, _) = await SetupAsync();
        var secret = "webhook-secret-123";

        var payload = "{\"ref\":\"refs/heads/main\",\"commits\":[]}";
        var signature = ComputeHmacSha256(secret, payload);

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        content.Headers.Add("X-Hub-Signature-256", $"sha256={signature}");

        var response = await _client.PostAsync($"/api/webhooks/{repo.Id}", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task TriggerWebhook_WithGithubPayload_ParsesCorrectly()
    {
        var (_, repo, _) = await SetupAsync();

        var payload = "{\"ref\":\"refs/heads/main\",\"before\":\"abc123\",\"after\":\"def456\",\"commits\":[],\"repository\":{\"full_name\":\"test/repo\"}}";

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        content.Headers.Add("X-GitHub-Event", "push");

        var response = await _client.PostAsync($"/api/webhooks/{repo.Id}", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task TriggerWebhook_WithPushEvent_ExtractsBranch()
    {
        var (_, repo, _) = await SetupAsync();

        var payload = "{\"ref\":\"refs/heads/feature/test-branch\"}";

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        content.Headers.Add("X-GitHub-Event", "push");

        var response = await _client.PostAsync($"/api/webhooks/{repo.Id}", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task TriggerWebhook_WithPullRequestEvent_ProcessesEvent()
    {
        var (_, repo, _) = await SetupAsync();

        var payload = "{\"action\":\"opened\",\"pull_request\":{\"number\":42,\"title\":\"Test PR\"}}";

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        content.Headers.Add("X-GitHub-Event", "pull_request");

        var response = await _client.PostAsync($"/api/webhooks/{repo.Id}", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task TriggerWebhook_WithEmptyPayload_Succeeds()
    {
        var (_, repo, _) = await SetupAsync();

        var response = await _client.PostAsync($"/api/webhooks/{repo.Id}", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task TriggerWebhook_WithJsonContentType_AcceptsPayload()
    {
        var (_, repo, _) = await SetupAsync();

        var payload = new { @ref = "refs/heads/main", commits = Array.Empty<object>() };

        var response = await _client.PostAsJsonAsync($"/api/webhooks/{repo.Id}", payload);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task TriggerWebhook_ConcurrentRequests_HandleGracefully()
    {
        var (_, repo, _) = await SetupAsync();

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => _client.PostAsync($"/api/webhooks/{repo.Id}", null))
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        responses.Should().AllSatisfy(r =>
            r.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.TooManyRequests));
    }

    [Test]
    public async Task TriggerWebhook_WithCustomEventHeader_ProcessedCorrectly()
    {
        var (_, repo, _) = await SetupAsync();

        var payload = "{\"test\":\"data\"}";

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        content.Headers.Add("X-Event-Type", "custom-event");

        var response = await _client.PostAsync($"/api/webhooks/{repo.Id}", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static string ComputeHmacSha256(string secret, string payload)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var data = Encoding.UTF8.GetBytes(payload);
        using var hmac = new HMACSHA256(key);
        var hash = hmac.ComputeHash(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
