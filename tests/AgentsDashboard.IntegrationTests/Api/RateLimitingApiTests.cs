using System.Net;
using System.Net.Http.Json;

namespace AgentsDashboard.IntegrationTests.Api;

[ClassDataSource<ApiTestFixture>(Shared = SharedType.Keyed, Key = "Api")]
public class RateLimitingApiTests(ApiTestFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    [Test]
    public async Task ApiEndpoint_WithinRateLimit_ReturnsSuccess()
    {
        var response = await _client.GetAsync("/api/projects");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    [Skip("Rate limit metadata not exposed by standard ASP.NET Core sliding window limiters")]
    public async Task ApiEndpoint_ReturnsRateLimitHeaders()
    {
        var response = await _client.GetAsync("/api/projects");

        response.Headers.Should().ContainKey("X-RateLimit-Limit");
        response.Headers.Should().ContainKey("X-RateLimit-Remaining");
    }

    [Test]
    public async Task WebhookEndpoint_WithinRateLimit_ReturnsExpectedStatus()
    {
        var response = await _client.PostAsJsonAsync("/api/webhooks/test-repo/test-token", new { });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
        response.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
    }

    [Test]
    public async Task BurstRequest_WithinBurstLimit_Succeeds()
    {
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => _client.GetAsync("/api/projects"))
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        responses.Should().AllSatisfy(r => r.StatusCode.Should().Be(HttpStatusCode.OK));
    }

    [Test]
    [Skip("Rate limit metadata not exposed by standard ASP.NET Core sliding window limiters")]
    public async Task GlobalRateLimit_IsApplied()
    {
        var response = await _client.GetAsync("/api/settings");

        response.Headers.Should().ContainKey("X-RateLimit-Limit");
    }

    [Test]
    [Skip("Rate limit metadata not exposed by standard ASP.NET Core sliding window limiters")]
    public async Task ApiRequest_HasRateLimitPolicy()
    {
        var response = await _client.GetAsync("/api/projects");

        var limit = response.Headers.GetValues("X-RateLimit-Limit").FirstOrDefault();
        limit.Should().NotBeNullOrEmpty();
    }

    [Test]
    [Skip("Rate limit metadata not exposed by standard ASP.NET Core sliding window limiters")]
    public async Task RateLimitHeaders_ContainResetTime()
    {
        var response = await _client.GetAsync("/api/projects");

        response.Headers.Should().ContainKey("X-RateLimit-Reset");
        var resetValue = response.Headers.GetValues("X-RateLimit-Reset").FirstOrDefault();
        resetValue.Should().NotBeNullOrEmpty();
    }

    [Test]
    [Skip("Rate limit metadata not exposed by standard ASP.NET Core sliding window limiters")]
    public async Task MultipleRequests_DecreaseRemainingCount()
    {
        var response1 = await _client.GetAsync("/api/projects");
        var remaining1 = int.Parse(response1.Headers.GetValues("X-RateLimit-Remaining").First());

        var response2 = await _client.GetAsync("/api/projects");
        var remaining2 = int.Parse(response2.Headers.GetValues("X-RateLimit-Remaining").First());

        remaining2.Should().BeLessThanOrEqualTo(remaining1);
    }

    [Test]
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
