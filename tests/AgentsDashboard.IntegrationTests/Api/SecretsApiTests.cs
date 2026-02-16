using System.Net;
using System.Net.Http.Json;
using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.IntegrationTests.Api;

[ClassDataSource<ApiTestFixture>(Shared = SharedType.Keyed, Key = "Api")]
public class SecretsApiTests(ApiTestFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    private async Task<(ProjectDocument Project, RepositoryDocument Repository)> SetupAsync()
    {
        var projectResponse = await _client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("SecretP", "d"));
        var project = (await projectResponse.Content.ReadFromJsonAsync<ProjectDocument>())!;

        var repoRequest = new CreateRepositoryRequest(project.Id, "SecretR", "https://github.com/test/repo.git", "main");
        var repoResponse = await _client.PostAsJsonAsync("/api/repositories", repoRequest);
        var repo = (await repoResponse.Content.ReadFromJsonAsync<RepositoryDocument>())!;

        return (project, repo);
    }

    [Test]
    public async Task ListSecrets_ReturnsEmptyList_WhenNoSecrets()
    {
        var (_, repo) = await SetupAsync();

        var response = await _client.GetAsync($"/api/repositories/{repo.Id}/secrets");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var secrets = await response.Content.ReadFromJsonAsync<List<object>>();
        secrets.Should().NotBeNull();
    }

    [Test]
    public async Task SetSecret_ReturnsOk()
    {
        var (_, repo) = await SetupAsync();
        var request = new SetProviderSecretRequest("sk-test-key-12345");

        var response = await _client.PutAsJsonAsync($"/api/repositories/{repo.Id}/secrets/openai", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task SetSecret_ReturnsValidationProblem_WhenSecretValueIsEmpty()
    {
        var (_, repo) = await SetupAsync();
        var request = new SetProviderSecretRequest("");

        var response = await _client.PutAsJsonAsync($"/api/repositories/{repo.Id}/secrets/openai", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task SetSecret_AppearsInList()
    {
        var (_, repo) = await SetupAsync();
        var request = new SetProviderSecretRequest("sk-test-key-12345");
        await _client.PutAsJsonAsync($"/api/repositories/{repo.Id}/secrets/anthropic", request);

        var response = await _client.GetAsync($"/api/repositories/{repo.Id}/secrets");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("anthropic");
    }

    [Test]
    public async Task TestSecret_ReturnsNotFound_WhenSecretDoesNotExist()
    {
        var (_, repo) = await SetupAsync();

        var response = await _client.PostAsync($"/api/repositories/{repo.Id}/secrets/nonexistent/test", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
