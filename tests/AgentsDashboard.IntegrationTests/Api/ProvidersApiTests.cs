using System.Net;
using System.Net.Http.Json;
using AgentsDashboard.Contracts.Api;

namespace AgentsDashboard.IntegrationTests.Api;

[ClassDataSource<ApiTestFixture>(Shared = SharedType.Keyed, Key = "Api")]
public class ProvidersApiTests(ApiTestFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    [Test]
    public async Task ValidateProvider_ReturnsValidationProblem_WhenProviderEmpty()
    {
        var request = new ValidateProviderRequest("", "sk-test");
        var response = await _client.PostAsJsonAsync("/api/providers/validate", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task ValidateProvider_ReturnsValidationProblem_WhenSecretValueEmpty()
    {
        var request = new ValidateProviderRequest("openai", "");
        var response = await _client.PostAsJsonAsync("/api/providers/validate", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task ValidateProvider_ReturnsOk_WithValidationResult()
    {
        var request = new ValidateProviderRequest("github", "ghp_test_token");
        var response = await _client.PostAsJsonAsync("/api/providers/validate", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ValidationResult>();
        result.Should().NotBeNull();
    }

    [Test]
    public async Task GetHarnessHealth_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/health/harnesses");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private sealed record ValidationResult(bool Success, string Message);
}
