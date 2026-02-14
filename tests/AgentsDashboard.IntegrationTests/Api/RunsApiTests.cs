using System.Net;
using System.Net.Http.Json;
using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;
using FluentAssertions;

namespace AgentsDashboard.IntegrationTests.Api;

[Collection("Api")]
public class RunsApiTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private readonly HttpClient _client = fixture.Client;

    private async Task<(ProjectDocument Project, RepositoryDocument Repository, TaskDocument Task)> SetupAsync()
    {
        var projectResponse = await _client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("P", "d"));
        var project = (await projectResponse.Content.ReadFromJsonAsync<ProjectDocument>())!;

        var repoRequest = new CreateRepositoryRequest(project.Id, "R", "https://github.com/test/repo.git", "main");
        var repoResponse = await _client.PostAsJsonAsync("/api/repositories", repoRequest);
        var repo = (await repoResponse.Content.ReadFromJsonAsync<RepositoryDocument>())!;

        var taskRequest = new CreateTaskRequest(repo.Id, "T", TaskKind.OneShot, "codex", "prompt", "cmd", false, "", true);
        var taskResponse = await _client.PostAsJsonAsync("/api/tasks", taskRequest);
        var task = (await taskResponse.Content.ReadFromJsonAsync<TaskDocument>())!;

        return (project, repo, task);
    }

    [Fact]
    public async Task CreateRun_ReturnsCreatedRun()
    {
        var (_, _, task) = await SetupAsync();
        var request = new CreateRunRequest(task.Id);

        var response = await _client.PostAsJsonAsync("/api/runs", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var run = await response.Content.ReadFromJsonAsync<RunDocument>();
        run.Should().NotBeNull();
        run!.TaskId.Should().Be(task.Id);
        run.State.Should().Be(RunState.Queued);
        run.Attempt.Should().Be(1);
    }

    [Fact]
    public async Task CreateRun_ReturnsNotFound_WhenTaskDoesNotExist()
    {
        var request = new CreateRunRequest("nonexistent");
        var response = await _client.PostAsJsonAsync("/api/runs", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetRun_ReturnsRun_WhenRunExists()
    {
        var (_, _, task) = await SetupAsync();
        var createResponse = await _client.PostAsJsonAsync("/api/runs", new CreateRunRequest(task.Id));
        var created = await createResponse.Content.ReadFromJsonAsync<RunDocument>();

        var response = await _client.GetAsync($"/api/runs/{created!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var run = await response.Content.ReadFromJsonAsync<RunDocument>();
        run!.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task GetRun_ReturnsNotFound_WhenRunDoesNotExist()
    {
        var response = await _client.GetAsync("/api/runs/nonexistent");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListRuns_ReturnsAllRuns()
    {
        var (_, _, task) = await SetupAsync();
        var run1Response = await _client.PostAsJsonAsync("/api/runs", new CreateRunRequest(task.Id));
        var run1 = await run1Response.Content.ReadFromJsonAsync<RunDocument>();
        var run2Response = await _client.PostAsJsonAsync("/api/runs", new CreateRunRequest(task.Id));
        var run2 = await run2Response.Content.ReadFromJsonAsync<RunDocument>();

        var response = await _client.GetAsync("/api/runs");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var runs = await response.Content.ReadFromJsonAsync<List<RunDocument>>();
        runs.Should().Contain(r => r.Id == run1!.Id);
        runs.Should().Contain(r => r.Id == run2!.Id);
    }

    [Fact]
    public async Task ListRepositoryRuns_ReturnsRunsForRepository()
    {
        var (_, repo, task) = await SetupAsync();
        var runResponse = await _client.PostAsJsonAsync("/api/runs", new CreateRunRequest(task.Id));
        var run = await runResponse.Content.ReadFromJsonAsync<RunDocument>();

        var response = await _client.GetAsync($"/api/repositories/{repo.Id}/runs");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var runs = await response.Content.ReadFromJsonAsync<List<RunDocument>>();
        runs.Should().Contain(r => r.Id == run!.Id);
    }

    [Fact]
    public async Task CancelRun_ReturnsCancelledRun()
    {
        var (_, _, task) = await SetupAsync();
        var createResponse = await _client.PostAsJsonAsync("/api/runs", new CreateRunRequest(task.Id));
        var created = await createResponse.Content.ReadFromJsonAsync<RunDocument>();

        var response = await _client.PostAsync($"/api/runs/{created!.Id}/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var run = await response.Content.ReadFromJsonAsync<RunDocument>();
        run!.State.Should().Be(RunState.Cancelled);
    }

    [Fact]
    public async Task CancelRun_ReturnsNotFound_WhenRunDoesNotExist()
    {
        var response = await _client.PostAsync("/api/runs/nonexistent/cancel", null);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RetryRun_ReturnsNewRunWithIncrementedAttempt()
    {
        var (_, _, task) = await SetupAsync();
        var createResponse = await _client.PostAsJsonAsync("/api/runs", new CreateRunRequest(task.Id));
        var original = await createResponse.Content.ReadFromJsonAsync<RunDocument>();

        var response = await _client.PostAsync($"/api/runs/{original!.Id}/retry", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var retryRun = await response.Content.ReadFromJsonAsync<RunDocument>();
        retryRun!.Attempt.Should().Be(2);
        retryRun.TaskId.Should().Be(task.Id);
    }

    [Fact]
    public async Task RetryRun_ReturnsNotFound_WhenRunDoesNotExist()
    {
        var response = await _client.PostAsync("/api/runs/nonexistent/retry", null);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetRunLogs_ReturnsEmptyList_WhenNoLogs()
    {
        var (_, _, task) = await SetupAsync();
        var createResponse = await _client.PostAsJsonAsync("/api/runs", new CreateRunRequest(task.Id));
        var run = await createResponse.Content.ReadFromJsonAsync<RunDocument>();

        var response = await _client.GetAsync($"/api/runs/{run!.Id}/logs");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
