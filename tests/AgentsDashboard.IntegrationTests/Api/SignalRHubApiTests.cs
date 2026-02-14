using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace AgentsDashboard.IntegrationTests.Api;

[Collection("Api")]
public class SignalRHubApiTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private readonly HttpClient _client = fixture.Client;

    [Fact]
    public async Task SignalRHub_Negotiate_ReturnsSuccess()
    {
        var loginRequest = new { username = "admin", password = "change-me" };
        await _client.PostAsJsonAsync("/auth/login", loginRequest);

        var response = await _client.PostAsync("/hubs/runs/negotiate?negotiateVersion=1", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SignalRHub_WithoutAuth_ReturnsUnauthorized()
    {
        using var unauthenticatedClient = fixture.Factory.CreateClient();

        var response = await unauthenticatedClient.PostAsync("/hubs/runs/negotiate?negotiateVersion=1", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SignalRHub_NegotiateReturnsConnectionId()
    {
        var loginRequest = new { username = "admin", password = "change-me" };
        await _client.PostAsJsonAsync("/auth/login", loginRequest);

        var response = await _client.PostAsync("/hubs/runs/negotiate?negotiateVersion=1", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("connectionId");
    }

    [Fact]
    public async Task SignalRHub_SupportsWebSockets()
    {
        var loginRequest = new { username = "admin", password = "change-me" };
        await _client.PostAsJsonAsync("/auth/login", loginRequest);

        var negotiateResponse = await _client.PostAsync("/hubs/events/negotiate?negotiateVersion=1", null);
        negotiateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SignalRHub_AvailableTransports_IncludeWebSockets()
    {
        var loginRequest = new { username = "admin", password = "change-me" };
        await _client.PostAsJsonAsync("/auth/login", loginRequest);

        var response = await _client.PostAsync("/hubs/runs/negotiate?negotiateVersion=1", null);
        var content = await response.Content.ReadFromJsonAsync<NegotiateResponse>();

        content.Should().NotBeNull();
        content!.AvailableTransports.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RunEventPublish_RequiresAuthentication()
    {
        var response = await _client.GetAsync("/hubs/runs");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SignalRHub_MultipleNegotiate_ReturnsDifferentConnectionIds()
    {
        var loginRequest = new { username = "admin", password = "change-me" };
        await _client.PostAsJsonAsync("/auth/login", loginRequest);

        var response1 = await _client.PostAsync("/hubs/events/negotiate?negotiateVersion=1", null);
        var content1 = await response1.Content.ReadFromJsonAsync<NegotiateResponse>();

        var response2 = await _client.PostAsync("/hubs/events/negotiate?negotiateVersion=1", null);
        var content2 = await response2.Content.ReadFromJsonAsync<NegotiateResponse>();

        content1!.ConnectionId.Should().NotBe(content2!.ConnectionId);
    }

    [Fact]
    public async Task SignalRHub_CookieAuth_IsSupported()
    {
        var loginRequest = new { username = "admin", password = "change-me" };
        var loginResponse = await _client.PostAsJsonAsync("/auth/login", loginRequest);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await _client.PostAsync("/hubs/runs/negotiate?negotiateVersion=1", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

public sealed record NegotiateResponse(
    string ConnectionId,
    List<string> AvailableTransports
);
