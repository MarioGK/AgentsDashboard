using System.Net;
using System.Net.Http.Json;
using AgentsDashboard.Contracts.Domain;
using FluentAssertions;

namespace AgentsDashboard.IntegrationTests.Api;

[Collection("Api")]
public class HarnessHealthApiTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private readonly HttpClient _client = fixture.Client;

    [Fact]
    public async Task GetHarnessHealth_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/health/harnesses");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetHarnessHealth_ReturnsHarnessHealthDictionary()
    {
        var response = await _client.GetAsync("/api/health/harnesses");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var health = await response.Content.ReadFromJsonAsync<Dictionary<string, HarnessHealthResponse>>();
        health.Should().NotBeNull();
    }

    [Fact]
    public async Task GetHarnessHealth_ReturnsExpectedHarnessNames()
    {
        var response = await _client.GetAsync("/api/health/harnesses");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var health = await response.Content.ReadFromJsonAsync<Dictionary<string, HarnessHealthResponse>>();
        health.Should().NotBeNull();
        health!.Keys.Should().Contain("codex");
        health.Keys.Should().Contain("opencode");
        health.Keys.Should().Contain("claude");
        health.Keys.Should().Contain("zai");
    }

    [Fact]
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

    [Fact]
    public async Task GetHarnessHealth_ReturnsCorrectHarnessCount()
    {
        var response = await _client.GetAsync("/api/health/harnesses");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var health = await response.Content.ReadFromJsonAsync<Dictionary<string, HarnessHealthResponse>>();
        health.Should().NotBeNull();
        health!.Count.Should().Be(4);
    }

    [Fact]
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
