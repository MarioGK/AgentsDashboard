using System.Net;
using System.Net.Http.Json;
using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;
using FluentAssertions;

namespace AgentsDashboard.IntegrationTests.Api;

[Collection("Api")]
public class FindingsTaskCreationApiTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private readonly HttpClient _client = fixture.Client;

    [Fact]
    public async Task CreateTaskFromFinding_ReturnsNotFound_WhenFindingDoesNotExist()
    {
        var request = new CreateTaskFromFindingRequest("New Task", "codex", "npm test", "Fix the issue");

        var response = await _client.PostAsJsonAsync("/api/findings/nonexistent/create-task", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
