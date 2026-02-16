using System.Net;
using System.Net.Http.Json;
using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.IntegrationTests.Api;

[ClassDataSource<ApiTestFixture>(Shared = SharedType.Keyed, Key = "Api")]
public class DeadLettersApiTests(ApiTestFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    [Test]
    public async Task ListDeadLetters_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/workflow-deadletters");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var deadLetters = await response.Content.ReadFromJsonAsync<List<WorkflowDeadLetterDocument>>();
        deadLetters.Should().NotBeNull();
    }

    [Test]
    public async Task GetDeadLetter_NotFound_Returns404()
    {
        var response = await _client.GetAsync("/api/workflow-deadletters/nonexistent");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task ReplayDeadLetter_NotFound_Returns404()
    {
        var request = new ReplayDeadLetterRequest("test-user");

        var response = await _client.PostAsJsonAsync("/api/workflow-deadletters/nonexistent/replay", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task ListDeadLetters_EmptyByDefault()
    {
        var response = await _client.GetAsync("/api/workflow-deadletters");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var deadLetters = await response.Content.ReadFromJsonAsync<List<WorkflowDeadLetterDocument>>();
        deadLetters.Should().NotBeNull();
        deadLetters!.Should().BeEmpty();
    }

    [Test]
    public async Task GetDeadLetter_NonExistent_Returns404()
    {
        var response = await _client.GetAsync($"/api/workflow-deadletters/{Guid.NewGuid():N}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task ReplayDeadLetter_NonExistent_Returns404()
    {
        var request = new ReplayDeadLetterRequest("integration-test");

        var response = await _client.PostAsJsonAsync(
            $"/api/workflow-deadletters/{Guid.NewGuid():N}/replay", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
