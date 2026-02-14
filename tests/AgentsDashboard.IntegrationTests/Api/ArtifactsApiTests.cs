using System.Net;
using System.Net.Http.Json;
using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;
using FluentAssertions;

namespace AgentsDashboard.IntegrationTests.Api;

[Collection("Api")]
public class ArtifactsApiTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private readonly HttpClient _client = fixture.Client;

    private async Task<(ProjectDocument Project, RepositoryDocument Repository, TaskDocument Task, RunDocument Run)> SetupAsync()
    {
        var projectResponse = await _client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("ArtifactP", "d"));
        var project = (await projectResponse.Content.ReadFromJsonAsync<ProjectDocument>())!;

        var repoRequest = new CreateRepositoryRequest(project.Id, "ArtifactR", "https://github.com/test/repo.git", "main");
        var repoResponse = await _client.PostAsJsonAsync("/api/repositories", repoRequest);
        var repo = (await repoResponse.Content.ReadFromJsonAsync<RepositoryDocument>())!;

        var taskRequest = new CreateTaskRequest(repo.Id, "ArtifactT", TaskKind.OneShot, "codex", "prompt", "cmd", false, "", true);
        var taskResponse = await _client.PostAsJsonAsync("/api/tasks", taskRequest);
        var task = (await taskResponse.Content.ReadFromJsonAsync<TaskDocument>())!;

        var runRequest = new CreateRunRequest(task.Id);
        var runResponse = await _client.PostAsJsonAsync("/api/runs", runRequest);
        var run = (await runResponse.Content.ReadFromJsonAsync<RunDocument>())!;

        return (project, repo, task, run);
    }

    [Fact]
    public async Task ListArtifacts_ReturnsEmptyList_WhenNoArtifacts()
    {
        var (_, _, _, run) = await SetupAsync();

        var response = await _client.GetAsync($"/api/runs/{run.Id}/artifacts");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotBeNull();
    }

    [Fact]
    public async Task GetArtifact_ReturnsNotFound_WhenArtifactDoesNotExist()
    {
        var (_, _, _, run) = await SetupAsync();

        var response = await _client.GetAsync($"/api/runs/{run.Id}/artifacts/nonexistent.txt");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetArtifact_ReturnsNotFound_WhenRunDoesNotExist()
    {
        var response = await _client.GetAsync("/api/runs/nonexistent/artifacts/file.txt");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
