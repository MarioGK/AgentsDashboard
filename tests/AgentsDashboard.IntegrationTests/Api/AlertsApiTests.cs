using System.Net;
using System.Net.Http.Json;
using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;
using Microsoft.Extensions.DependencyInjection;

namespace AgentsDashboard.IntegrationTests.Api;

[ClassDataSource<ApiTestFixture>(Shared = SharedType.Keyed, Key = "Api")]
public class AlertsApiTests(ApiTestFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    [Test]
    public async Task ListAlertRules_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/alerts/rules");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var rules = await response.Content.ReadFromJsonAsync<List<AlertRuleDocument>>();
        rules.Should().NotBeNull();
    }

    [Test]
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

    [Test]
    public async Task CreateAlertRule_ReturnsValidationProblem_WhenNameIsEmpty()
    {
        var request = new CreateAlertRuleRequest("", AlertRuleType.FailureRateSpike, 5, 10);
        var response = await _client.PostAsJsonAsync("/api/alerts/rules", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task CreateAlertRule_ReturnsValidationProblem_WhenThresholdIsZero()
    {
        var request = new CreateAlertRuleRequest("Rule", AlertRuleType.FailureRateSpike, 0, 10);
        var response = await _client.PostAsJsonAsync("/api/alerts/rules", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
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

    [Test]
    public async Task UpdateAlertRule_ReturnsNotFound_WhenRuleDoesNotExist()
    {
        var request = new UpdateAlertRuleRequest("Name", AlertRuleType.FailureRateSpike, 5, 10);
        var response = await _client.PutAsJsonAsync("/api/alerts/rules/nonexistent", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task UpdateAlertRule_ReturnsValidationProblem_WhenNameIsEmpty()
    {
        var createRequest = new CreateAlertRuleRequest("Rule", AlertRuleType.FailureRateSpike, 3, 15);
        var createResponse = await _client.PostAsJsonAsync("/api/alerts/rules", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<AlertRuleDocument>();

        var updateRequest = new UpdateAlertRuleRequest("", AlertRuleType.FailureRateSpike, 5, 10);
        var response = await _client.PutAsJsonAsync($"/api/alerts/rules/{created!.Id}", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task DeleteAlertRule_ReturnsOk_WhenRuleExists()
    {
        var createRequest = new CreateAlertRuleRequest("To Delete", AlertRuleType.FailureRateSpike, 1, 5);
        var createResponse = await _client.PostAsJsonAsync("/api/alerts/rules", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<AlertRuleDocument>();

        var response = await _client.DeleteAsync($"/api/alerts/rules/{created!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task DeleteAlertRule_ReturnsNotFound_WhenRuleDoesNotExist()
    {
        var response = await _client.DeleteAsync("/api/alerts/rules/nonexistent");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task ListAlertEvents_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/alerts/events");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task BulkResolveAlerts_ReturnsOk_WithValidEventIds()
    {
        var createRequest = new CreateAlertRuleRequest("Test Rule", AlertRuleType.FailureRateSpike, 5, 30);
        var createResponse = await _client.PostAsJsonAsync("/api/alerts/rules", createRequest);
        var rule = await createResponse.Content.ReadFromJsonAsync<AlertRuleDocument>();

        var store = fixture.Services.GetRequiredService<OrchestratorStore>();
        var event1 = await store.RecordAlertEventAsync(new AlertEventDocument { RuleId = rule!.Id, RuleName = rule.Name, Message = "Test 1" }, CancellationToken.None);
        var event2 = await store.RecordAlertEventAsync(new AlertEventDocument { RuleId = rule.Id, RuleName = rule.Name, Message = "Test 2" }, CancellationToken.None);

        var request = new BulkResolveAlertsRequest([event1.Id, event2.Id]);
        var response = await _client.PostAsJsonAsync("/api/alerts/events/bulk-resolve", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<BulkOperationResult>();
        result!.AffectedCount.Should().Be(2);
        result.Errors.Should().BeEmpty();
    }

    [Test]
    public async Task BulkResolveAlerts_ReturnsPartialSuccess_WithMixedEventIds()
    {
        var createRequest = new CreateAlertRuleRequest("Test Rule", AlertRuleType.FailureRateSpike, 5, 30);
        var createResponse = await _client.PostAsJsonAsync("/api/alerts/rules", createRequest);
        var rule = await createResponse.Content.ReadFromJsonAsync<AlertRuleDocument>();

        var store = fixture.Services.GetRequiredService<OrchestratorStore>();
        var evt = await store.RecordAlertEventAsync(new AlertEventDocument { RuleId = rule!.Id, RuleName = rule.Name, Message = "Test" }, CancellationToken.None);

        var request = new BulkResolveAlertsRequest([evt.Id, "nonexistent-event"]);
        var response = await _client.PostAsJsonAsync("/api/alerts/events/bulk-resolve", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<BulkOperationResult>();
        result!.AffectedCount.Should().Be(1);
    }

    [Test]
    public async Task BulkResolveAlerts_ReturnsBadRequest_WhenNoEventIdsProvided()
    {
        var request = new BulkResolveAlertsRequest([]);
        var response = await _client.PostAsJsonAsync("/api/alerts/events/bulk-resolve", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task BulkResolveAlerts_ReturnsOk_WithOnlyInvalidEventIds()
    {
        var request = new BulkResolveAlertsRequest(["nonexistent1", "nonexistent2"]);
        var response = await _client.PostAsJsonAsync("/api/alerts/events/bulk-resolve", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<BulkOperationResult>();
        result!.AffectedCount.Should().Be(0);
    }
}
