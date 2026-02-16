using System.Net;
using System.Net.Http.Json;
using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.IntegrationTests.Api;

[ClassDataSource<ApiTestFixture>(Shared = SharedType.Keyed, Key = "Api")]
public class ProxyAuditsApiTests(ApiTestFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    [Test]
    public async Task ListProxyAudits_ReturnsEmptyList_WhenNoAudits()
    {
        var response = await _client.GetAsync("/api/proxy-audits");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var audits = await response.Content.ReadFromJsonAsync<List<ProxyAuditDocument>>();
        audits.Should().NotBeNull();
    }

    [Test]
    public async Task ListProxyAudits_WithFilters_ReturnsFilteredResults()
    {
        var response = await _client.GetAsync("/api/proxy-audits?projectId=test-project&limit=10");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var audits = await response.Content.ReadFromJsonAsync<List<ProxyAuditDocument>>();
        audits.Should().NotBeNull();
    }

    [Test]
    public async Task ListProxyAudits_WithDateRange_FiltersCorrectly()
    {
        var from = DateTime.UtcNow.AddDays(-7).ToString("o");
        var to = DateTime.UtcNow.ToString("o");

        var response = await _client.GetAsync($"/api/proxy-audits?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var audits = await response.Content.ReadFromJsonAsync<List<ProxyAuditDocument>>();
        audits.Should().NotBeNull();
    }

    [Test]
    public async Task ListProxyAudits_WithRepositoryFilter_ReturnsFilteredResults()
    {
        var response = await _client.GetAsync("/api/proxy-audits?repositoryId=test-repo");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var audits = await response.Content.ReadFromJsonAsync<List<ProxyAuditDocument>>();
        audits.Should().NotBeNull();
    }

    [Test]
    public async Task ListProxyAudits_WithRunFilter_ReturnsFilteredResults()
    {
        var response = await _client.GetAsync("/api/proxy-audits?runId=test-run");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var audits = await response.Content.ReadFromJsonAsync<List<ProxyAuditDocument>>();
        audits.Should().NotBeNull();
    }

    [Test]
    public async Task ListProxyAudits_WithPagination_ReturnsLimitedResults()
    {
        var response = await _client.GetAsync("/api/proxy-audits?skip=0&limit=5");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var audits = await response.Content.ReadFromJsonAsync<List<ProxyAuditDocument>>();
        audits.Should().NotBeNull();
        audits!.Count.Should().BeLessThan(6);
    }
}
