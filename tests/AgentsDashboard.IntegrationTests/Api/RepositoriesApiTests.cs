using System.Net;
using System.Net.Http.Json;
using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.IntegrationTests.Api;

[ClassDataSource<ApiTestFixture>(Shared = SharedType.Keyed, Key = "Api")]
public class RepositoriesApiTests(ApiTestFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    private async Task<ProjectDocument> CreateProjectAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("Test Project", "Description"));
        return (await response.Content.ReadFromJsonAsync<ProjectDocument>())!;
    }

    [Test]
    public async Task CreateRepository_ReturnsCreatedRepository()
    {
        var project = await CreateProjectAsync();
        var request = new CreateRepositoryRequest(project.Id, "My Repo", "https://github.com/org/repo.git", "main");

        var response = await _client.PostAsJsonAsync("/api/repositories", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var repo = await response.Content.ReadFromJsonAsync<RepositoryDocument>();
        repo.Should().NotBeNull();
        repo!.Name.Should().Be("My Repo");
        repo.GitUrl.Should().Be("https://github.com/org/repo.git");
        repo.DefaultBranch.Should().Be("main");
        repo.ProjectId.Should().Be(project.Id);
    }

    [Test]
    public async Task CreateRepository_ReturnsNotFound_WhenProjectDoesNotExist()
    {
        var request = new CreateRepositoryRequest("nonexistent", "Repo", "https://github.com/test.git", "main");
        var response = await _client.PostAsJsonAsync("/api/repositories", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task UpdateRepository_ReturnsUpdatedRepository()
    {
        var project = await CreateProjectAsync();
        var createRequest = new CreateRepositoryRequest(project.Id, "Original", "https://github.com/test/original.git", "main");
        var createResponse = await _client.PostAsJsonAsync("/api/repositories", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<RepositoryDocument>();

        var updateRequest = new UpdateRepositoryRequest("Updated", "https://github.com/test/updated.git", "develop");
        var response = await _client.PutAsJsonAsync($"/api/repositories/{created!.Id}", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<RepositoryDocument>();
        updated!.Name.Should().Be("Updated");
        updated.GitUrl.Should().Be("https://github.com/test/updated.git");
        updated.DefaultBranch.Should().Be("develop");
    }

    [Test]
    public async Task UpdateRepository_ReturnsNotFound_WhenRepositoryDoesNotExist()
    {
        var request = new UpdateRepositoryRequest("Name", "https://github.com/test.git", "main");
        var response = await _client.PutAsJsonAsync("/api/repositories/nonexistent", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task DeleteRepository_ReturnsOk_WhenRepositoryExists()
    {
        var project = await CreateProjectAsync();
        var createRequest = new CreateRepositoryRequest(project.Id, "To Delete", "https://github.com/test.git", "main");
        var createResponse = await _client.PostAsJsonAsync("/api/repositories", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<RepositoryDocument>();

        var response = await _client.DeleteAsync($"/api/repositories/{created!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task DeleteRepository_ReturnsNotFound_WhenRepositoryDoesNotExist()
    {
        var response = await _client.DeleteAsync("/api/repositories/nonexistent");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
