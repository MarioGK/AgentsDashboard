using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace AgentsDashboard.IntegrationTests.Api;

[Collection("Api")]
public class AuthApiTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private readonly HttpClient _client = fixture.Client;

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsSuccess()
    {
        var request = new { username = "admin", password = "admin123" };
        var response = await _client.PostAsJsonAsync("/auth/login", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_ReturnsUnauthorized()
    {
        var request = new { username = "admin", password = "wrongpassword" };
        var response = await _client.PostAsJsonAsync("/auth/login", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_SetsAuthenticationCookie()
    {
        var request = new { username = "admin", password = "admin123" };
        var response = await _client.PostAsJsonAsync("/auth/login", request);

        response.Headers.Contains("Set-Cookie").Should().BeTrue();
    }

    [Fact]
    public async Task GetCurrentUser_ReturnsUser_WhenAuthenticated()
    {
        var loginRequest = new { username = "admin", password = "admin123" };
        await _client.PostAsJsonAsync("/auth/login", loginRequest);

        var response = await _client.GetAsync("/auth/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var user = await response.Content.ReadFromJsonAsync<CurrentUserResponse>();
        user.Should().NotBeNull();
        user!.Username.Should().Be("admin");
        user.Role.Should().Be("admin");
    }

    [Fact]
    public async Task GetCurrentUser_ReturnsRedirect_WhenNotAuthenticated()
    {
        using var unauthenticatedClient = fixture.Factory.CreateClient();

        var response = await unauthenticatedClient.GetAsync("/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Logout_ClearsAuthentication()
    {
        var loginRequest = new { username = "admin", password = "admin123" };
        await _client.PostAsJsonAsync("/auth/login", loginRequest);

        var response = await _client.PostAsJsonAsync("/auth/logout", new { });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var meResponse = await _client.GetAsync("/auth/me");
        meResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task LogoutGet_RedirectsToLogin()
    {
        var response = await _client.GetAsync("/auth/logout");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
    }
}

public sealed record CurrentUserResponse(string Username, string Role);
