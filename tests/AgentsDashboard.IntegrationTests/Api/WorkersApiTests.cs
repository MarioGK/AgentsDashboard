using System.Net;
using System.Net.Http.Json;
using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.IntegrationTests.Api;

[ClassDataSource<ApiTestFixture>(Shared = SharedType.Keyed, Key = "Api")]
public class WorkersApiTests(ApiTestFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    [Test]
    public async Task ListWorkers_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/workers");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var workers = await response.Content.ReadFromJsonAsync<List<WorkerRegistration>>();
        workers.Should().NotBeNull();
    }

    [Test]
    public async Task WorkerHeartbeat_ReturnsAcknowledged()
    {
        var request = new WorkerHeartbeatRequest("worker-1", "http://localhost:5001", 2, 4);
        var response = await _client.PostAsJsonAsync("/api/workers/heartbeat", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task WorkerHeartbeat_ReturnsValidationProblem_WhenWorkerIdEmpty()
    {
        var request = new WorkerHeartbeatRequest("", "http://localhost:5001", 0, 4);
        var response = await _client.PostAsJsonAsync("/api/workers/heartbeat", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task WorkerHeartbeat_WorkerAppearsInList()
    {
        var request = new WorkerHeartbeatRequest("worker-list-test", "http://localhost:5002", 1, 8);
        await _client.PostAsJsonAsync("/api/workers/heartbeat", request);

        var response = await _client.GetAsync("/api/workers");
        var workers = await response.Content.ReadFromJsonAsync<List<WorkerRegistration>>();

        workers.Should().Contain(w => w.WorkerId == "worker-list-test");
    }
}
