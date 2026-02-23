using System.Net.Http;

namespace AgentsDashboard.Workspace.IntegrationTests;

public sealed class RuntimeReadyEndpointIntegrationTests
{
    [Test]
    public async Task ReadyEndpointReturnsRuntimePoolHealthAsync()
    {
        var baseUrl = Environment.GetEnvironmentVariable("BASE_URL");
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            await Assert.That(true).IsTrue();
            return;
        }

        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(baseUrl, UriKind.Absolute)
        };

        using var response = await client.GetAsync("/ready");
        var payload = await response.Content.ReadAsStringAsync();

        await Assert.That((int)response.StatusCode).IsEqualTo(200);
        await Assert.That(payload.Contains("task-runtime-pool", StringComparison.OrdinalIgnoreCase)).IsTrue();
        await Assert.That(payload.Contains("readiness_blocked", StringComparison.OrdinalIgnoreCase)).IsTrue();
    }
}
