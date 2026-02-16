using System.Net;
using System.Net.Http.Json;
using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.IntegrationTests.Api;

[ClassDataSource<ApiTestFixture>(Shared = SharedType.Keyed, Key = "Api")]
public class HarnessHealthApiTests(ApiTestFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    [Test]
    public async Task GetHarnessHealth_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/health/harnesses");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task GetHarnessHealth_ReturnsHarnessHealthDictionary()
    {
        var response = await _client.GetAsync("/api/health/harnesses");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var health = await response.Content.ReadFromJsonAsync<Dictionary<string, HarnessHealthResponse>>();
        health.Should().NotBeNull();
    }

    [Test]
    public async Task GetHarnessHealth_ReturnsExpectedHarnessNames()
    {
        var response = await _client.GetAsync("/api/health/harnesses");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var health = await response.Content.ReadFromJsonAsync<Dictionary<string, HarnessHealthResponse>>();
        health.Should().NotBeNull();

        if (health!.Count > 0)
        {
            health.Keys.Should().Contain("codex");
            health.Keys.Should().Contain("opencode");
            health.Keys.Should().Contain("claude");
            health.Keys.Should().Contain("zai");
        }
    }

    [Test]
    public async Task GetHarnessHealth_ReturnsValidStatus()
    {
        var response = await _client.GetAsync("/api/health/harnesses");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var health = await response.Content.ReadFromJsonAsync<Dictionary<string, HarnessHealthResponse>>();
        health.Should().NotBeNull();

        foreach (var kvp in health!)
        {
            kvp.Value.Status.Should().BeOneOf("Available", "Unavailable", "Unknown");
        }
    }

    [Test]
    public async Task GetHarnessHealth_ReturnsCorrectHarnessCount()
    {
        var response = await _client.GetAsync("/api/health/harnesses");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var health = await response.Content.ReadFromJsonAsync<Dictionary<string, HarnessHealthResponse>>();
        health.Should().NotBeNull();
        health!.Count.Should().BeOneOf(0, 4);
    }

    [Test]
    public async Task GetHarnessHealth_EachHarnessHasName()
    {
        var response = await _client.GetAsync("/api/health/harnesses");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var health = await response.Content.ReadFromJsonAsync<Dictionary<string, HarnessHealthResponse>>();
        health.Should().NotBeNull();

        foreach (var kvp in health!)
        {
            kvp.Value.Name.Should().NotBeNullOrEmpty();
            kvp.Key.Should().Be(kvp.Value.Name);
        }
    }
}

public record HarnessHealthResponse(string Name, string Status, string? Version);
