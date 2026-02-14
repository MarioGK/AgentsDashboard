using System.Net;
using System.Net.Http.Json;
using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;
using FluentAssertions;

namespace AgentsDashboard.IntegrationTests.Api;

[Collection("Api")]
public class AlertsApiTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private readonly HttpClient _client = fixture.Client;

    [Fact]
    public async Task ListAlertRules_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/alerts/rules");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var rules = await response.Content.ReadFromJsonAsync<List<AlertRuleDocument>>();
        rules.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateAlertRule_ReturnsCreatedRule()
    {
        var request = new CreateAlertRuleRequest("High Failure Rate", AlertRuleType.FailureRateSpike, 5, 30, "https://hooks.example.com/alert");

        var response = await _client.PostAsJsonAsync("/api/alerts/rules", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var rule = await response.Content.ReadFromJsonAsync<AlertRuleDocument>();
        rule.Should().NotBeNull();
        rule!.Name.Should().Be("High Failure Rate");
        rule.RuleType.Should().Be(AlertRuleType.FailureRateSpike);
        rule.Threshold.Should().Be(5);
        rule.WindowMinutes.Should().Be(30);
        rule.Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task CreateAlertRule_ReturnsValidationProblem_WhenNameIsEmpty()
    {
        var request = new CreateAlertRuleRequest("", AlertRuleType.FailureRateSpike, 5, 10);
        var response = await _client.PostAsJsonAsync("/api/alerts/rules", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateAlertRule_ReturnsValidationProblem_WhenThresholdIsZero()
    {
        var request = new CreateAlertRuleRequest("Rule", AlertRuleType.FailureRateSpike, 0, 10);
        var response = await _client.PostAsJsonAsync("/api/alerts/rules", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateAlertRule_ReturnsUpdatedRule()
    {
        var createRequest = new CreateAlertRuleRequest("Original", AlertRuleType.FailureRateSpike, 3, 15);
        var createResponse = await _client.PostAsJsonAsync("/api/alerts/rules", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<AlertRuleDocument>();

        var updateRequest = new UpdateAlertRuleRequest("Updated", AlertRuleType.QueueBacklog, 10, 60, "https://hooks.example.com/updated", false);
        var response = await _client.PutAsJsonAsync($"/api/alerts/rules/{created!.Id}", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<AlertRuleDocument>();
        updated!.Name.Should().Be("Updated");
        updated.RuleType.Should().Be(AlertRuleType.QueueBacklog);
        updated.Threshold.Should().Be(10);
        updated.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAlertRule_ReturnsNotFound_WhenRuleDoesNotExist()
    {
        var request = new UpdateAlertRuleRequest("Name", AlertRuleType.FailureRateSpike, 5, 10);
        var response = await _client.PutAsJsonAsync("/api/alerts/rules/nonexistent", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateAlertRule_ReturnsValidationProblem_WhenNameIsEmpty()
    {
        var createRequest = new CreateAlertRuleRequest("Rule", AlertRuleType.FailureRateSpike, 3, 15);
        var createResponse = await _client.PostAsJsonAsync("/api/alerts/rules", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<AlertRuleDocument>();

        var updateRequest = new UpdateAlertRuleRequest("", AlertRuleType.FailureRateSpike, 5, 10);
        var response = await _client.PutAsJsonAsync($"/api/alerts/rules/{created!.Id}", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeleteAlertRule_ReturnsOk_WhenRuleExists()
    {
        var createRequest = new CreateAlertRuleRequest("To Delete", AlertRuleType.FailureRateSpike, 1, 5);
        var createResponse = await _client.PostAsJsonAsync("/api/alerts/rules", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<AlertRuleDocument>();

        var response = await _client.DeleteAsync($"/api/alerts/rules/{created!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteAlertRule_ReturnsNotFound_WhenRuleDoesNotExist()
    {
        var response = await _client.DeleteAsync("/api/alerts/rules/nonexistent");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListAlertEvents_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/alerts/events");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
