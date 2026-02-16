using System.Net;
using System.Net.Http.Json;
using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.IntegrationTests.Api;

[ClassDataSource<ApiTestFixture>(Shared = SharedType.Keyed, Key = "Api")]
public class TasksApiTests(ApiTestFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    private async Task<(ProjectDocument Project, RepositoryDocument Repository)> SetupAsync()
    {
        var projectResponse = await _client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("P", "d"));
        var project = (await projectResponse.Content.ReadFromJsonAsync<ProjectDocument>())!;

        var repoRequest = new CreateRepositoryRequest(project.Id, "R", "https://github.com/test/repo.git", "main");
        var repoResponse = await _client.PostAsJsonAsync("/api/repositories", repoRequest);
        var repo = (await repoResponse.Content.ReadFromJsonAsync<RepositoryDocument>())!;

        return (project, repo);
    }

    [Test]
    public async Task CreateTask_ReturnsCreatedTask()
    {
        var (_, repo) = await SetupAsync();
        var request = new CreateTaskRequest(
            repo.Id, "My Task", TaskKind.OneShot, "codex", "Fix bugs", "npm test", false, "", true);

        var response = await _client.PostAsJsonAsync("/api/tasks", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var task = await response.Content.ReadFromJsonAsync<TaskDocument>();
        task.Should().NotBeNull();
        task!.Name.Should().Be("My Task");
        task.Kind.Should().Be(TaskKind.OneShot);
        task.Harness.Should().Be("codex");
        task.RepositoryId.Should().Be(repo.Id);
    }

    [Test]
    public async Task CreateTask_ReturnsNotFound_WhenRepositoryDoesNotExist()
    {
        var request = new CreateTaskRequest("nonexistent", "Task", TaskKind.OneShot, "codex", "prompt", "cmd", false, "", true);
        var response = await _client.PostAsJsonAsync("/api/tasks", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task CreateCronTask_ReturnsValidationProblem_WhenCronExpressionMissing()
    {
        var (_, repo) = await SetupAsync();
        var request = new CreateTaskRequest(
            repo.Id, "Cron Task", TaskKind.Cron, "codex", "prompt", "cmd", false, "", true);

        var response = await _client.PostAsJsonAsync("/api/tasks", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task CreateCronTask_ReturnsCreatedTask_WhenCronExpressionProvided()
    {
        var (_, repo) = await SetupAsync();
        var request = new CreateTaskRequest(
            repo.Id, "Cron Task", TaskKind.Cron, "codex", "prompt", "cmd", false, "0 * * * *", true);

        var response = await _client.PostAsJsonAsync("/api/tasks", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var task = await response.Content.ReadFromJsonAsync<TaskDocument>();
        task!.CronExpression.Should().Be("0 * * * *");
    }

    [Test]
    public async Task UpdateTask_ReturnsUpdatedTask()
    {
        var (_, repo) = await SetupAsync();
        var createRequest = new CreateTaskRequest(repo.Id, "Original", TaskKind.OneShot, "codex", "p", "c", false, "", true);
        var createResponse = await _client.PostAsJsonAsync("/api/tasks", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<TaskDocument>();

        var updateRequest = new UpdateTaskRequest("Updated", TaskKind.OneShot, "opencode", "new prompt", "new cmd", true, "", true);
        var response = await _client.PutAsJsonAsync($"/api/tasks/{created!.Id}", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<TaskDocument>();
        updated!.Name.Should().Be("Updated");
        updated.Harness.Should().Be("opencode");
        updated.Prompt.Should().Be("new prompt");
    }

    [Test]
    public async Task UpdateTask_ReturnsNotFound_WhenTaskDoesNotExist()
    {
        var request = new UpdateTaskRequest("Name", TaskKind.OneShot, "codex", "prompt", "cmd", false, "", true);
        var response = await _client.PutAsJsonAsync("/api/tasks/nonexistent", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task DeleteTask_ReturnsOk_WhenTaskExists()
    {
        var (_, repo) = await SetupAsync();
        var createRequest = new CreateTaskRequest(repo.Id, "To Delete", TaskKind.OneShot, "codex", "p", "c", false, "", true);
        var createResponse = await _client.PostAsJsonAsync("/api/tasks", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<TaskDocument>();

        var response = await _client.DeleteAsync($"/api/tasks/{created!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task DeleteTask_ReturnsNotFound_WhenTaskDoesNotExist()
    {
        var response = await _client.DeleteAsync("/api/tasks/nonexistent");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task ListRepositoryTasks_ReturnsTasksForRepository()
    {
        var (_, repo) = await SetupAsync();
        var request = new CreateTaskRequest(repo.Id, "Task1", TaskKind.OneShot, "codex", "p", "c", false, "", true);
        await _client.PostAsJsonAsync("/api/tasks", request);

        var response = await _client.GetAsync($"/api/repositories/{repo.Id}/tasks");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var tasks = await response.Content.ReadFromJsonAsync<List<TaskDocument>>();
        tasks.Should().ContainSingle(t => t.Name == "Task1");
    }
}
