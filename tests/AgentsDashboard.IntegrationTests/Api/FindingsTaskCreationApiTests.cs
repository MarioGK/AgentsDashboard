using System.Net;
using System.Net.Http.Json;
using AgentsDashboard.Contracts.Api;

namespace AgentsDashboard.IntegrationTests.Api;

[ClassDataSource<ApiTestFixture>(Shared = SharedType.Keyed, Key = "Api")]
public class FindingsTaskCreationApiTests(ApiTestFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    [Test]
    public async Task CreateTaskFromFinding_ReturnsNotFound_WhenFindingDoesNotExist()
    {
        var request = new CreateTaskFromFindingRequest("New Task", "codex", "npm test", "Fix the issue");

        var response = await _client.PostAsJsonAsync("/api/findings/nonexistent/create-task", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
