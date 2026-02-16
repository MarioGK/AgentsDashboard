using System.Net;
using System.Net.Http.Json;
using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.IntegrationTests.Api;

[ClassDataSource<ApiTestFixture>(Shared = SharedType.Keyed, Key = "Api")]
public class SchedulesApiTests(ApiTestFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    [Test]
    public async Task ListSchedules_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/schedules");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var schedules = await response.Content.ReadFromJsonAsync<List<TaskDocument>>();
        schedules.Should().NotBeNull();
    }

    [Test]
    public async Task ListSchedules_ReturnsCronTasks()
    {
        var projectResponse = await _client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("SP", "d"));
        var project = (await projectResponse.Content.ReadFromJsonAsync<ProjectDocument>())!;

        var repoRequest = new CreateRepositoryRequest(project.Id, "SR", "https://github.com/test/repo.git", "main");
        var repoResponse = await _client.PostAsJsonAsync("/api/repositories", repoRequest);
        var repo = (await repoResponse.Content.ReadFromJsonAsync<RepositoryDocument>())!;

        var taskRequest = new CreateTaskRequest(repo.Id, "Scheduled Task", TaskKind.Cron, "codex", "prompt", "cmd", false, "*/5 * * * *", true);
        await _client.PostAsJsonAsync("/api/tasks", taskRequest);

        var response = await _client.GetAsync("/api/schedules");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var schedules = await response.Content.ReadFromJsonAsync<List<TaskDocument>>();
        schedules.Should().Contain(t => t.Name == "Scheduled Task" && t.Kind == TaskKind.Cron);
    }
}
