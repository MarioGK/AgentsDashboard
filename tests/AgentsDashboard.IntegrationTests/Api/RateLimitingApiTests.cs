using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace AgentsDashboard.IntegrationTests.Api;

[Collection("Api")]
public class RateLimitingApiTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private readonly HttpClient _client = fixture.Client;

    [Fact]
    public async Task ApiEndpoint_WithinRateLimit_ReturnsSuccess()
    {
        var loginRequest = new { username = "admin", password = "admin123" };
        await _client.PostAsJsonAsync("/auth/login", loginRequest);

        var response = await _client.GetAsync("/api/projects");
        
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ApiEndpoint_ReturnsRateLimitHeaders()
    {
        var loginRequest = new { username = "admin", password = "admin123" };
        await _client.PostAsJsonAsync("/auth/login", loginRequest);

        var response = await _client.GetAsync("/api/projects");
        
        response.Headers.Should().ContainKey("X-RateLimit-Limit");
        response.Headers.Should().ContainKey("X-RateLimit-Remaining");
    }

    [Fact]
    public async Task WebhookEndpoint_HasSeparateRateLimit()
    {
        var response = await _client.PostAsJsonAsync("/api/webhooks/test-repo/test-token", new { });
        
        response.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task BurstRequest_WithinBurstLimit_Succeeds()
    {
        var loginRequest = new { username = "admin", password = "admin123" };
        await _client.PostAsJsonAsync("/auth/login", loginRequest);

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => _client.GetAsync("/api/projects"))
            .ToArray();

        var responses = await Task.WhenAll(tasks);
        
        responses.Should().AllSatisfy(r => r.StatusCode.Should().Be(HttpStatusCode.OK));
    }

    [Fact]
    public async Task GlobalRateLimit_IsApplied()
    {
        var loginRequest = new { username = "admin", password = "admin123" };
        await _client.PostAsJsonAsync("/auth/login", loginRequest);

        var response = await _client.GetAsync("/api/settings");
        
        response.Headers.Should().ContainKey("X-RateLimit-Limit");
    }

    [Fact]
    public async Task AuthenticatedRequest_HasAuthPolicy()
    {
        var loginRequest = new { username = "admin", password = "admin123" };
        await _client.PostAsJsonAsync("/auth/login", loginRequest);

        var response = await _client.GetAsync("/api/projects");
        
        var limit = response.Headers.GetValues("X-RateLimit-Limit").FirstOrDefault();
        limit.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RateLimitHeaders_ContainResetTime()
    {
        var loginRequest = new { username = "admin", password = "admin123" };
        await _client.PostAsJsonAsync("/auth/login", loginRequest);

        var response = await _client.GetAsync("/api/projects");
        
        response.Headers.Should().ContainKey("X-RateLimit-Reset");
        var resetValue = response.Headers.GetValues("X-RateLimit-Reset").FirstOrDefault();
        resetValue.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task MultipleRequests_DecreaseRemainingCount()
    {
        var loginRequest = new { username = "admin", password = "admin123" };
        await _client.PostAsJsonAsync("/auth/login", loginRequest);

        var response1 = await _client.GetAsync("/api/projects");
        var remaining1 = int.Parse(response1.Headers.GetValues("X-RateLimit-Remaining").First());

        var response2 = await _client.GetAsync("/api/projects");
        var remaining2 = int.Parse(response2.Headers.GetValues("X-RateLimit-Remaining").First());
        
        remaining2.Should().BeLessThanOrEqualTo(remaining1);
    }

    [Fact]
    public async Task HealthEndpoint_IsNotRateLimited()
    {
        var responses = new List<HttpResponseMessage>();
        
        for (int i = 0; i < 5; i++)
        {
            responses.Add(await _client.GetAsync("/health"));
        }
        
        responses.Should().AllSatisfy(r => r.StatusCode.Should().Be(HttpStatusCode.OK));
    }
}
