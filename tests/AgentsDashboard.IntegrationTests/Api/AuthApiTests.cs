using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AgentsDashboard.IntegrationTests.Api;

[Collection("Api")]
public class AuthApiTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private HttpClient CreateCookieClient() =>
        fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsSuccess()
    {
        using var client = CreateCookieClient();
        var request = new { username = "admin", password = "change-me" };
        var response = await client.PostAsJsonAsync("/auth/login", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_ReturnsUnauthorized()
    {
        using var client = CreateCookieClient();
        var request = new { username = "admin", password = "wrongpassword" };
        var response = await client.PostAsJsonAsync("/auth/login", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_SetsAuthenticationCookie()
    {
        using var client = CreateCookieClient();
        var request = new { username = "admin", password = "change-me" };
        var response = await client.PostAsJsonAsync("/auth/login", request);

        response.Headers.Contains("Set-Cookie").Should().BeTrue();
    }

    [Fact]
    public async Task GetCurrentUser_ReturnsUser_WhenAuthenticated()
    {
        using var client = CreateCookieClient();
        var loginRequest = new { username = "admin", password = "change-me" };
        await client.PostAsJsonAsync("/auth/login", loginRequest);

        var response = await client.GetAsync("/auth/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetCurrentUser_ReturnsRedirect_WhenNotAuthenticated()
    {
        using var client = CreateCookieClient();

        var response = await client.GetAsync("/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Logout_ClearsAuthentication()
    {
        using var client = CreateCookieClient();
        var loginRequest = new { username = "admin", password = "change-me" };
        await client.PostAsJsonAsync("/auth/login", loginRequest);

        var response = await client.PostAsJsonAsync("/auth/logout", new { });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var meResponse = await client.GetAsync("/auth/me");
        meResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task LogoutGet_RedirectsToLogin()
    {
        using var client = CreateCookieClient();
        var response = await client.GetAsync("/auth/logout");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
    }
}

public sealed record CurrentUserResponse(string Username, string Role);
