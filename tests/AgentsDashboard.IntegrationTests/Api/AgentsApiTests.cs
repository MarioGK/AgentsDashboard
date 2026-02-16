using System.Net;
using System.Net.Http.Json;
using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.IntegrationTests.Api;

[ClassDataSource<ApiTestFixture>(Shared = SharedType.Keyed, Key = "Api")]
public class AgentsApiTests(ApiTestFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    private async Task<(ProjectDocument Project, RepositoryDocument Repository)> SetupAsync()
    {
        var projectResponse = await _client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("AgentProj", "d"));
        var project = (await projectResponse.Content.ReadFromJsonAsync<ProjectDocument>())!;

        var repoRequest = new CreateRepositoryRequest(project.Id, "AgentRepo", "https://github.com/test/repo.git", "main");
        var repoResponse = await _client.PostAsJsonAsync("/api/repositories", repoRequest);
        var repo = (await repoResponse.Content.ReadFromJsonAsync<RepositoryDocument>())!;

        return (project, repo);
    }

    private static CreateAgentRequest MakeAgentRequest(string repositoryId, string name = "Test Agent") =>
        new(repositoryId, name, "A test agent", "codex", "Do something", "echo hello", false);

    [Test]
    public async Task ListAgentsByRepository_ReturnsOk()
    {
        var (_, repo) = await SetupAsync();

        var response = await _client.GetAsync($"/api/repositories/{repo.Id}/agents");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var agents = await response.Content.ReadFromJsonAsync<List<AgentDocument>>();
        agents.Should().NotBeNull();
    }

    [Test]
    public async Task GetAgent_NotFound_Returns404()
    {
        var response = await _client.GetAsync("/api/agents/nonexistent");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task CreateAgent_Valid_ReturnsOk()
    {
        var (_, repo) = await SetupAsync();
        var request = MakeAgentRequest(repo.Id);

        var response = await _client.PostAsJsonAsync("/api/agents", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var agent = await response.Content.ReadFromJsonAsync<AgentDocument>();
        agent.Should().NotBeNull();
        agent!.Name.Should().Be("Test Agent");
        agent.Description.Should().Be("A test agent");
        agent.Harness.Should().Be("codex");
        agent.Prompt.Should().Be("Do something");
        agent.Command.Should().Be("echo hello");
        agent.RepositoryId.Should().Be(repo.Id);
        agent.Enabled.Should().BeTrue();
        agent.Id.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task CreateAgent_MissingName_ReturnsValidationError()
    {
        var (_, repo) = await SetupAsync();
        var request = MakeAgentRequest(repo.Id, name: "");

        var response = await _client.PostAsJsonAsync("/api/agents", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task CreateAgent_InvalidRepository_ReturnsValidationError()
    {
        var request = MakeAgentRequest("nonexistent-repo");

        var response = await _client.PostAsJsonAsync("/api/agents", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task UpdateAgent_Valid_ReturnsOk()
    {
        var (_, repo) = await SetupAsync();
        var createResponse = await _client.PostAsJsonAsync("/api/agents", MakeAgentRequest(repo.Id));
        var created = await createResponse.Content.ReadFromJsonAsync<AgentDocument>();

        var updateRequest = new UpdateAgentRequest(
            "Updated Agent", "Updated description", "claude-code",
            "Updated prompt", "echo updated", true, true);

        var response = await _client.PutAsJsonAsync($"/api/agents/{created!.Id}", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<AgentDocument>();
        updated!.Name.Should().Be("Updated Agent");
        updated.Description.Should().Be("Updated description");
        updated.Harness.Should().Be("claude-code");
        updated.Prompt.Should().Be("Updated prompt");
        updated.AutoCreatePullRequest.Should().BeTrue();
    }

    [Test]
    public async Task UpdateAgent_NotFound_Returns404()
    {
        var request = new UpdateAgentRequest(
            "Name", "Desc", "codex", "prompt", "cmd", false, true);

        var response = await _client.PutAsJsonAsync("/api/agents/nonexistent", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task DeleteAgent_Valid_ReturnsOk()
    {
        var (_, repo) = await SetupAsync();
        var createResponse = await _client.PostAsJsonAsync("/api/agents", MakeAgentRequest(repo.Id));
        var created = await createResponse.Content.ReadFromJsonAsync<AgentDocument>();

        var response = await _client.DeleteAsync($"/api/agents/{created!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task DeleteAgent_NotFound_Returns404()
    {
        var response = await _client.DeleteAsync("/api/agents/nonexistent");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task CreateAndGetAgent_RoundTrip()
    {
        var (_, repo) = await SetupAsync();
        var createResponse = await _client.PostAsJsonAsync("/api/agents", MakeAgentRequest(repo.Id, "RoundTrip Agent"));
        var created = await createResponse.Content.ReadFromJsonAsync<AgentDocument>();

        var getResponse = await _client.GetAsync($"/api/agents/{created!.Id}");

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var fetched = await getResponse.Content.ReadFromJsonAsync<AgentDocument>();
        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(created.Id);
        fetched.Name.Should().Be("RoundTrip Agent");
        fetched.RepositoryId.Should().Be(repo.Id);
    }

    [Test]
    public async Task ListAgents_AfterCreate_ContainsNew()
    {
        var (_, repo) = await SetupAsync();
        await _client.PostAsJsonAsync("/api/agents", MakeAgentRequest(repo.Id, "Listed Agent"));

        var response = await _client.GetAsync($"/api/repositories/{repo.Id}/agents");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var agents = await response.Content.ReadFromJsonAsync<List<AgentDocument>>();
        agents.Should().Contain(a => a.Name == "Listed Agent");
    }
}
