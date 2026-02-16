using System.Net;
using System.Net.Http.Json;
using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.IntegrationTests.Api;

[ClassDataSource<ApiTestFixture>(Shared = SharedType.Keyed, Key = "Api")]
public class SettingsApiTests(ApiTestFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    [Test]
    public async Task GetSettings_ReturnsDefaultSettings()
    {
        var response = await _client.GetAsync("/api/settings");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var settings = await response.Content.ReadFromJsonAsync<SystemSettingsDocument>();
        settings.Should().NotBeNull();
    }

    [Test]
    public async Task UpdateSettings_ReturnsUpdatedSettings()
    {
        var request = new UpdateSystemSettingsRequest(
            DockerAllowedImages: ["ubuntu:22.04", "node:20"],
            RetentionDaysLogs: 15,
            RetentionDaysRuns: 60,
            VictoriaMetricsEndpoint: "http://vm:8428",
            VmUiEndpoint: "http://vmui:8081");

        var response = await _client.PutAsJsonAsync("/api/settings", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<SystemSettingsDocument>();
        updated!.DockerAllowedImages.Should().BeEquivalentTo(["ubuntu:22.04", "node:20"]);
        updated.RetentionDaysLogs.Should().Be(15);
        updated.RetentionDaysRuns.Should().Be(60);
        updated.VictoriaMetricsEndpoint.Should().Be("http://vm:8428");
        updated.VmUiEndpoint.Should().Be("http://vmui:8081");
    }

    [Test]
    public async Task UpdateSettings_PartialUpdate_PreservesExistingValues()
    {
        var fullRequest = new UpdateSystemSettingsRequest(
            DockerAllowedImages: ["alpine:latest"],
            RetentionDaysLogs: 20,
            RetentionDaysRuns: 45);
        await _client.PutAsJsonAsync("/api/settings", fullRequest);

        var partialRequest = new UpdateSystemSettingsRequest(RetentionDaysLogs: 10);
        var response = await _client.PutAsJsonAsync("/api/settings", partialRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<SystemSettingsDocument>();
        updated!.RetentionDaysLogs.Should().Be(10);
        updated.RetentionDaysRuns.Should().Be(45);
    }
}
