using System.Net;
using System.Net.Http.Json;
using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AgentsDashboard.IntegrationTests.Api;

[Collection("Api")]
public class FindingsApiTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private readonly HttpClient _client = fixture.Client;
    private readonly WebApplicationFactory<AgentsDashboard.ControlPlane.Program> _factory = fixture.Factory;

    private async Task<(ProjectDocument Project, RepositoryDocument Repository, TaskDocument Task, RunDocument Run)> SetupWithRunAsync()
    {
        var projectResponse = await _client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("P", "d"));
        var project = (await projectResponse.Content.ReadFromJsonAsync<ProjectDocument>())!;

        var repoRequest = new CreateRepositoryRequest(project.Id, "R", "https://github.com/test/repo.git", "main");
        var repoResponse = await _client.PostAsJsonAsync("/api/repositories", repoRequest);
        var repo = (await repoResponse.Content.ReadFromJsonAsync<RepositoryDocument>())!;

        var taskRequest = new CreateTaskRequest(repo.Id, "T", TaskKind.OneShot, "codex", "prompt", "cmd", false, "", true);
        var taskResponse = await _client.PostAsJsonAsync("/api/tasks", taskRequest);
        var task = (await taskResponse.Content.ReadFromJsonAsync<TaskDocument>())!;

        var runRequest = new CreateRunRequest(task.Id);
        var runResponse = await _client.PostAsJsonAsync("/api/runs", runRequest);
        var run = (await runResponse.Content.ReadFromJsonAsync<RunDocument>())!;

        return (project, repo, task, run);
    }

    private async Task<FindingDocument> CreateFindingAsync(string runId)
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<OrchestratorStore>();
        await store.InitializeAsync(CancellationToken.None);

        var run = await store.GetRunAsync(runId, CancellationToken.None);
        await store.MarkRunCompletedAsync(runId, false, "Failed", "{}", CancellationToken.None, "TestFailure");

        var finding = await store.CreateFindingFromFailureAsync(run!, "Test finding", CancellationToken.None);
        return finding;
    }

    [Fact]
    public async Task ListFindings_ReturnsEmptyList_WhenNoFindings()
    {
        var response = await _client.GetAsync("/api/findings");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var findings = await response.Content.ReadFromJsonAsync<List<FindingDocument>>();
        findings.Should().NotBeNull();
    }

    [Fact]
    public async Task ListFindings_ReturnsAllFindings()
    {
        var (_, _, _, run) = await SetupWithRunAsync();
        var finding = await CreateFindingAsync(run.Id);

        var response = await _client.GetAsync("/api/findings");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var findings = await response.Content.ReadFromJsonAsync<List<FindingDocument>>();
        findings.Should().Contain(f => f.Id == finding.Id);
    }

    [Fact]
    public async Task GetFinding_ReturnsFinding_WhenExists()
    {
        var (_, _, _, run) = await SetupWithRunAsync();
        var finding = await CreateFindingAsync(run.Id);

        var response = await _client.GetAsync($"/api/findings/{finding.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<FindingDocument>();
        result!.Id.Should().Be(finding.Id);
    }

    [Fact]
    public async Task GetFinding_ReturnsNotFound_WhenDoesNotExist()
    {
        var response = await _client.GetAsync("/api/findings/nonexistent");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateFindingState_ReturnsUpdatedFinding()
    {
        var (_, _, _, run) = await SetupWithRunAsync();
        var finding = await CreateFindingAsync(run.Id);

        var request = new UpdateFindingStateRequest(FindingState.Acknowledged);
        var response = await _client.PatchAsJsonAsync($"/api/findings/{finding.Id}", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<FindingDocument>();
        updated!.State.Should().Be(FindingState.Acknowledged);
    }

    [Fact]
    public async Task UpdateFindingState_ReturnsNotFound_WhenFindingDoesNotExist()
    {
        var request = new UpdateFindingStateRequest(FindingState.Acknowledged);
        var response = await _client.PatchAsJsonAsync("/api/findings/nonexistent", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListRepositoryFindings_ReturnsFindingsForRepository()
    {
        var (_, repo, _, run) = await SetupWithRunAsync();
        await CreateFindingAsync(run.Id);

        var response = await _client.GetAsync($"/api/repositories/{repo.Id}/findings");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var findings = await response.Content.ReadFromJsonAsync<List<FindingDocument>>();
        findings.Should().ContainSingle(f => f.RepositoryId == repo.Id);
    }

    [Fact]
    public async Task AssignFinding_ReturnsUpdatedFinding()
    {
        var (_, _, _, run) = await SetupWithRunAsync();
        var finding = await CreateFindingAsync(run.Id);

        var request = new AssignFindingRequest("test-user@example.com");
        var response = await _client.PutAsJsonAsync($"/api/findings/{finding.Id}/assign", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<FindingDocument>();
        updated!.AssignedTo.Should().Be("test-user@example.com");
    }

    [Fact]
    public async Task AssignFinding_ReturnsNotFound_WhenFindingDoesNotExist()
    {
        var request = new AssignFindingRequest("test@example.com");
        var response = await _client.PutAsJsonAsync("/api/findings/nonexistent/assign", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RetryFinding_ReturnsNewRun()
    {
        var (_, _, _, run) = await SetupWithRunAsync();
        var finding = await CreateFindingAsync(run.Id);

        var response = await _client.PostAsync($"/api/findings/{finding.Id}/retry", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var retryRun = await response.Content.ReadFromJsonAsync<RunDocument>();
        retryRun.Should().NotBeNull();
        retryRun!.State.Should().Be(RunState.Queued);
    }

    [Fact]
    public async Task RetryFinding_ReturnsNotFound_WhenFindingDoesNotExist()
    {
        var response = await _client.PostAsync("/api/findings/nonexistent/retry", null);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
