using System.Net;
using System.Net.Http.Json;
using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.IntegrationTests.Api;

[ClassDataSource<ApiTestFixture>(Shared = SharedType.Keyed, Key = "Api")]
public class RunsApprovalApiTests(ApiTestFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    private async Task<(ProjectDocument Project, RepositoryDocument Repository, TaskDocument Task, RunDocument Run)> SetupAsync()
    {
        var projectResponse = await _client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("ApprovalP", "d"));
        var project = (await projectResponse.Content.ReadFromJsonAsync<ProjectDocument>())!;

        var repoRequest = new CreateRepositoryRequest(project.Id, "ApprovalR", "https://github.com/test/repo.git", "main");
        var repoResponse = await _client.PostAsJsonAsync("/api/repositories", repoRequest);
        var repo = (await repoResponse.Content.ReadFromJsonAsync<RepositoryDocument>())!;

        var taskRequest = new CreateTaskRequest(repo.Id, "ApprovalT", TaskKind.OneShot, "codex", "prompt", "cmd", false, "", true);
        var taskResponse = await _client.PostAsJsonAsync("/api/tasks", taskRequest);
        var task = (await taskResponse.Content.ReadFromJsonAsync<TaskDocument>())!;

        var runRequest = new CreateRunRequest(task.Id);
        var runResponse = await _client.PostAsJsonAsync("/api/runs", runRequest);
        var run = (await runResponse.Content.ReadFromJsonAsync<RunDocument>())!;

        return (project, repo, task, run);
    }

    [Test]
    public async Task ApproveRun_ReturnsNotFound_WhenRunDoesNotExist()
    {
        var response = await _client.PostAsync("/api/runs/nonexistent/approve", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task ApproveRun_ReturnsBadRequest_WhenRunNotPendingApproval()
    {
        var (_, _, _, run) = await SetupAsync();

        var response = await _client.PostAsync($"/api/runs/{run.Id}/approve", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task RejectRun_ReturnsNotFound_WhenRunDoesNotExist()
    {
        var response = await _client.PostAsync("/api/runs/nonexistent/reject", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task RejectRun_ReturnsBadRequest_WhenRunNotPendingApproval()
    {
        var (_, _, _, run) = await SetupAsync();

        var response = await _client.PostAsync($"/api/runs/{run.Id}/reject", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task CancelRun_ReturnsBadRequest_WhenRunAlreadyCompleted()
    {
        var (_, _, _, run) = await SetupAsync();
        await _client.PostAsync($"/api/runs/{run.Id}/cancel", null);

        var response = await _client.PostAsync($"/api/runs/{run.Id}/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task CancelRun_ReturnsBadRequest_WhenRunInTerminalState()
    {
        var (_, _, task) = await SetupWithoutRunAsync();
        var runResponse = await _client.PostAsJsonAsync("/api/runs", new CreateRunRequest(task.Id));
        var run = await runResponse.Content.ReadFromJsonAsync<RunDocument>();

        await _client.PostAsync($"/api/runs/{run!.Id}/cancel", null);

        var response = await _client.PostAsync($"/api/runs/{run.Id}/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<(ProjectDocument Project, RepositoryDocument Repository, TaskDocument Task)> SetupWithoutRunAsync()
    {
        var projectResponse = await _client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("ApprovalP", "d"));
        var project = (await projectResponse.Content.ReadFromJsonAsync<ProjectDocument>())!;

        var repoRequest = new CreateRepositoryRequest(project.Id, "ApprovalR", "https://github.com/test/repo.git", "main");
        var repoResponse = await _client.PostAsJsonAsync("/api/repositories", repoRequest);
        var repo = (await repoResponse.Content.ReadFromJsonAsync<RepositoryDocument>())!;

        var taskRequest = new CreateTaskRequest(repo.Id, "ApprovalT", TaskKind.OneShot, "codex", "prompt", "cmd", false, "", true);
        var taskResponse = await _client.PostAsJsonAsync("/api/tasks", taskRequest);
        var task = (await taskResponse.Content.ReadFromJsonAsync<TaskDocument>())!;

        return (project, repo, task);
    }
}
