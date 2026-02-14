using System.Net;
using System.Net.Http.Json;
using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;
using FluentAssertions;

namespace AgentsDashboard.IntegrationTests.Api;

[Collection("Api")]
public class WorkersApiTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private readonly HttpClient _client = fixture.Client;

    [Fact]
    public async Task ListWorkers_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/workers");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var workers = await response.Content.ReadFromJsonAsync<List<WorkerRegistration>>();
        workers.Should().NotBeNull();
    }

    [Fact]
    public async Task WorkerHeartbeat_ReturnsAcknowledged()
    {
        var request = new WorkerHeartbeatRequest("worker-1", "http://localhost:5001", 2, 4);
        var response = await _client.PostAsJsonAsync("/api/workers/heartbeat", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task WorkerHeartbeat_ReturnsValidationProblem_WhenWorkerIdEmpty()
    {
        var request = new WorkerHeartbeatRequest("", "http://localhost:5001", 0, 4);
        var response = await _client.PostAsJsonAsync("/api/workers/heartbeat", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WorkerHeartbeat_WorkerAppearsInList()
    {
        var request = new WorkerHeartbeatRequest("worker-list-test", "http://localhost:5002", 1, 8);
        await _client.PostAsJsonAsync("/api/workers/heartbeat", request);

        var response = await _client.GetAsync("/api/workers");
        var workers = await response.Content.ReadFromJsonAsync<List<WorkerRegistration>>();

        workers.Should().Contain(w => w.WorkerId == "worker-list-test");
    }
}
