using System.Net;
using System.Net.Http.Json;
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
        var request = new TaskTemplateDocument
        {
            Name = "Custom Test Template",
            Description = "Test template description",
            Kind = TaskKind.OneShot,
            Harness = "codex",
            Prompt = "Test prompt",
            Commands = ["echo test"],
        };

        var response = await _client.PostAsJsonAsync("/api/templates", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var template = await response.Content.ReadFromJsonAsync<TaskTemplateDocument>();
        template.Should().NotBeNull();
        template!.TemplateId.Should().StartWith("custom-");
        template.Name.Should().Be("Custom Test Template");
    }

    [Fact]
    public async Task UpdateTemplate_ReturnsUpdatedTemplate()
    {
        var createRequest = new TaskTemplateDocument
        {
            Name = "Original Name",
            Description = "Original description",
            Kind = TaskKind.OneShot,
            Harness = "codex",
            Prompt = "Original prompt",
            Commands = ["echo test"],
        };

        var createResponse = await _client.PostAsJsonAsync("/api/templates", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<TaskTemplateDocument>();

        var updateRequest = new TaskTemplateDocument
        {
            Name = "Updated Name",
            Description = "Updated description",
            Kind = TaskKind.Cron,
            Harness = "opencode",
            Prompt = "Updated prompt",
            Commands = ["echo updated"],
            CronExpression = "0 * * * *",
            AutoCreatePullRequest = true,
        };

        var response = await _client.PutAsJsonAsync($"/api/templates/{created!.TemplateId}", updateRequest);
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
        var createRequest = new TaskTemplateDocument
        {
            Name = "To Delete",
            Description = "Template for deletion test",
            Kind = TaskKind.OneShot,
            Harness = "codex",
            Prompt = "Prompt",
            Commands = ["echo test"],
        };

        var createResponse = await _client.PostAsJsonAsync("/api/templates", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<TaskTemplateDocument>();

        var response = await _client.DeleteAsync($"/api/templates/{created!.TemplateId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteTemplate_ReturnsNotFound_WhenNotExists()
    {
        var response = await _client.DeleteAsync("/api/templates/nonexistent");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
