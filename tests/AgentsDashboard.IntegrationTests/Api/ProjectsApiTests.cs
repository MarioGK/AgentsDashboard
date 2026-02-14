using System.Net;
using System.Net.Http.Json;
using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;
using FluentAssertions;

namespace AgentsDashboard.IntegrationTests.Api;

[Collection("Api")]
public class ProjectsApiTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private readonly HttpClient _client = fixture.Client;

    [Fact]
    public async Task ListProjects_ReturnsEmptyList_WhenNoProjects()
    {
        var response = await _client.GetAsync("/api/projects");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var projects = await response.Content.ReadFromJsonAsync<List<ProjectDocument>>();
        projects.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateProject_ReturnsCreatedProject()
    {
        var request = new CreateProjectRequest("Test Project", "Test description");
        var response = await _client.PostAsJsonAsync("/api/projects", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var project = await response.Content.ReadFromJsonAsync<ProjectDocument>();
        project.Should().NotBeNull();
        project!.Name.Should().Be("Test Project");
        project.Description.Should().Be("Test description");
        project.Id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CreateProject_ReturnsValidationProblem_WhenNameIsEmpty()
    {
        var request = new CreateProjectRequest("", "Test description");
        var response = await _client.PostAsJsonAsync("/api/projects", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateProject_ReturnsUpdatedProject()
    {
        var createRequest = new CreateProjectRequest("Original Name", "Original description");
        var createResponse = await _client.PostAsJsonAsync("/api/projects", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<ProjectDocument>();

        var updateRequest = new UpdateProjectRequest("Updated Name", "Updated description");
        var response = await _client.PutAsJsonAsync($"/api/projects/{created!.Id}", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<ProjectDocument>();
        updated!.Name.Should().Be("Updated Name");
        updated.Description.Should().Be("Updated description");
    }

    [Fact]
    public async Task UpdateProject_ReturnsNotFound_WhenProjectDoesNotExist()
    {
        var request = new UpdateProjectRequest("Name", "Description");
        var response = await _client.PutAsJsonAsync("/api/projects/nonexistent", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteProject_ReturnsOk_WhenProjectExists()
    {
        var createRequest = new CreateProjectRequest("To Delete", "Description");
        var createResponse = await _client.PostAsJsonAsync("/api/projects", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<ProjectDocument>();

        var response = await _client.DeleteAsync($"/api/projects/{created!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteProject_ReturnsNotFound_WhenProjectDoesNotExist()
    {
        var response = await _client.DeleteAsync("/api/projects/nonexistent");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListProjectRepositories_ReturnsRepositoriesForProject()
    {
        var projectResponse = await _client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("P", "d"));
        var project = await projectResponse.Content.ReadFromJsonAsync<ProjectDocument>();

        var repoRequest = new CreateRepositoryRequest(project!.Id, "Repo1", "https://github.com/test/repo.git", "main");
        await _client.PostAsJsonAsync("/api/repositories", repoRequest);

        var response = await _client.GetAsync($"/api/projects/{project.Id}/repositories");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var repos = await response.Content.ReadFromJsonAsync<List<RepositoryDocument>>();
        repos.Should().ContainSingle(r => r.Name == "Repo1");
    }
}
