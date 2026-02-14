using System.Net;
using System.Net.Http.Json;
using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;
using FluentAssertions;

namespace AgentsDashboard.IntegrationTests.Api;

[Collection("Api")]
public class TemplatesApiTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private readonly HttpClient _client = fixture.Client;

    [Fact]
    public async Task ListTemplates_ReturnsBuiltInTemplates()
    {
        var response = await _client.GetAsync("/api/templates");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var templates = await response.Content.ReadFromJsonAsync<List<TaskTemplateDocument>>();
        templates.Should().NotBeNull();
        templates.Should().Contain(t => t.TemplateId == "qa-browser-sweep");
        templates.Should().Contain(t => t.TemplateId == "unit-test-guard");
        templates.Should().Contain(t => t.TemplateId == "dependency-health-check");
        templates.Should().Contain(t => t.TemplateId == "regression-replay");
    }

    [Fact]
    public async Task GetTemplate_ReturnsTemplate_WhenExists()
    {
        var response = await _client.GetAsync("/api/templates/qa-browser-sweep");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var template = await response.Content.ReadFromJsonAsync<TaskTemplateDocument>();
        template.Should().NotBeNull();
        template!.TemplateId.Should().Be("qa-browser-sweep");
        template.Name.Should().Be("QA Browser Sweep");
    }

    [Fact]
    public async Task GetTemplate_ReturnsNotFound_WhenNotExists()
    {
        var response = await _client.GetAsync("/api/templates/nonexistent-template");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateTemplate_ReturnsCreatedTemplate()
    {
        var request = new CreateTaskTemplateRequest(
            "custom-test",
            "Custom Test Template",
            TaskKind.OneShot,
            "codex",
            "Test prompt",
            ["echo test"],
            "",
            false,
            new RetryPolicyConfig(2),
            new TimeoutConfig(600, 1800),
            new SandboxProfileConfig(1.5, "2g"),
            new ArtifactPolicyConfig(50));

        var response = await _client.PostAsJsonAsync("/api/templates", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var template = await response.Content.ReadFromJsonAsync<TaskTemplateDocument>();
        template.Should().NotBeNull();
        template!.TemplateId.Should().Be("custom-test");
        template.Name.Should().Be("Custom Test Template");
    }

    [Fact]
    public async Task UpdateTemplate_ReturnsUpdatedTemplate()
    {
        var createRequest = new CreateTaskTemplateRequest(
            "update-test",
            "Original Name",
            TaskKind.OneShot,
            "codex",
            "Original prompt",
            ["echo test"],
            "",
            false,
            new RetryPolicyConfig(1),
            new TimeoutConfig(600, 1800),
            new SandboxProfileConfig(1.5, "2g"),
            new ArtifactPolicyConfig(50));

        await _client.PostAsJsonAsync("/api/templates", createRequest);

        var updateRequest = new UpdateTaskTemplateRequest(
            "Updated Name",
            TaskKind.Cron,
            "opencode",
            "Updated prompt",
            ["echo updated"],
            "0 * * * *",
            true,
            new RetryPolicyConfig(3),
            new TimeoutConfig(900, 2400),
            new SandboxProfileConfig(2.0, "4g"),
            new ArtifactPolicyConfig(100));

        var response = await _client.PutAsJsonAsync("/api/templates/update-test", updateRequest);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<TaskTemplateDocument>();
        updated!.Name.Should().Be("Updated Name");
        updated.Kind.Should().Be(TaskKind.Cron);
        updated.Harness.Should().Be("opencode");
        updated.AutoCreatePullRequest.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteTemplate_ReturnsOk_WhenExists()
    {
        var createRequest = new CreateTaskTemplateRequest(
            "delete-test",
            "To Delete",
            TaskKind.OneShot,
            "codex",
            "Prompt",
            ["echo test"],
            "",
            false,
            new RetryPolicyConfig(1),
            new TimeoutConfig(600, 1800),
            new SandboxProfileConfig(1.5, "2g"),
            new ArtifactPolicyConfig(50));

        await _client.PostAsJsonAsync("/api/templates", createRequest);

        var response = await _client.DeleteAsync("/api/templates/delete-test");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteTemplate_ReturnsNotFound_WhenNotExists()
    {
        var response = await _client.DeleteAsync("/api/templates/nonexistent");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
