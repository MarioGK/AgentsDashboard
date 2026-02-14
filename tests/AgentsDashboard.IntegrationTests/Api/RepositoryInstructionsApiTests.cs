using System.Net;
using System.Net.Http.Json;
using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;
using FluentAssertions;

namespace AgentsDashboard.IntegrationTests.Api;

[Collection("Api")]
public class RepositoryInstructionsApiTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private readonly HttpClient _client = fixture.Client;

    private async Task<(ProjectDocument Project, RepositoryDocument Repository)> SetupAsync()
    {
        var projectResponse = await _client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("InstrP", "d"));
        var project = (await projectResponse.Content.ReadFromJsonAsync<ProjectDocument>())!;

        var repoRequest = new CreateRepositoryRequest(project.Id, "InstrR", "https://github.com/test/repo.git", "main");
        var repoResponse = await _client.PostAsJsonAsync("/api/repositories", repoRequest);
        var repo = (await repoResponse.Content.ReadFromJsonAsync<RepositoryDocument>())!;

        return (project, repo);
    }

    [Fact]
    public async Task GetInstructions_ReturnsEmptyList_WhenNoInstructions()
    {
        var (_, repo) = await SetupAsync();

        var response = await _client.GetAsync($"/api/repositories/{repo.Id}/instructions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var instructions = await response.Content.ReadFromJsonAsync<List<InstructionFile>>();
        instructions.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateInstructions_ReturnsUpdatedInstructions()
    {
        var (_, repo) = await SetupAsync();
        var instructions = new List<InstructionFile>
        {
            new("CLAUDE.md", "# Instructions\nWrite clean code.", 0),
            new(".cursorrules", "Use TypeScript strict mode.", 1)
        };
        var request = new UpdateRepositoryInstructionsRequest(instructions);

        var response = await _client.PutAsJsonAsync($"/api/repositories/{repo.Id}/instructions", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<List<InstructionFile>>();
        result.Should().HaveCount(2);
        result!.Should().Contain(i => i.Name == "CLAUDE.md");
    }

    [Fact]
    public async Task UpdateInstructions_ReturnsNotFound_WhenRepositoryDoesNotExist()
    {
        var request = new UpdateRepositoryInstructionsRequest([]);

        var response = await _client.PutAsJsonAsync("/api/repositories/nonexistent/instructions", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetInstructions_ReturnsPreviouslySavedInstructions()
    {
        var (_, repo) = await SetupAsync();
        var instructions = new List<InstructionFile>
        {
            new("README.md", "Test content", 0)
        };
        await _client.PutAsJsonAsync($"/api/repositories/{repo.Id}/instructions", new UpdateRepositoryInstructionsRequest(instructions));

        var response = await _client.GetAsync($"/api/repositories/{repo.Id}/instructions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<List<InstructionFile>>();
        result.Should().ContainSingle(i => i.Name == "README.md");
    }
}
