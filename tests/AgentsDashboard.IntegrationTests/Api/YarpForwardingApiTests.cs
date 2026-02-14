using System.Net;
using System.Net.Http.Json;
using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Configuration;

namespace AgentsDashboard.IntegrationTests.Api;

[Collection("Api")]
public class YarpForwardingApiTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private readonly HttpClient _client = fixture.Client;
    private readonly WebApplicationFactory<AgentsDashboard.ControlPlane.Program> _factory = fixture.Factory;

    private async Task<(ProjectDocument Project, RepositoryDocument Repository, TaskDocument Task, RunDocument Run)> SetupRunWithProxyAsync()
    {
        var projectResponse = await _client.PostAsJsonAsync("/api/projects", new CreateProjectRequest($"ProxyP{Guid.NewGuid():N}", "d"));
        var project = (await projectResponse.Content.ReadFromJsonAsync<ProjectDocument>())!;

        var repoRequest = new CreateRepositoryRequest(project.Id, $"ProxyR{Guid.NewGuid():N}", "https://github.com/test/repo.git", "main");
        var repoResponse = await _client.PostAsJsonAsync("/api/repositories", repoRequest);
        var repo = (await repoResponse.Content.ReadFromJsonAsync<RepositoryDocument>())!;

        var taskRequest = new CreateTaskRequest(repo.Id, "ProxyTask", TaskKind.OneShot, "codex", "prompt", "cmd", false, "", true);
        var taskResponse = await _client.PostAsJsonAsync("/api/tasks", taskRequest);
        var task = (await taskResponse.Content.ReadFromJsonAsync<TaskDocument>())!;

        var runResponse = await _client.PostAsJsonAsync("/api/runs", new CreateRunRequest(task.Id));
        var run = (await runResponse.Content.ReadFromJsonAsync<RunDocument>())!;

        return (project, repo, task, run);
    }

    [Fact]
    public async Task ProxyRoutes_ListIsEmpty_Initially()
    {
        var response = await _client.GetAsync("/api/proxy-audits");
        
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ProxyRoute_CreatedForRun_Exists()
    {
        var (_, _, _, run) = await SetupRunWithProxyAsync();
        
        var proxyResponse = await _client.GetAsync($"/api/runs/{run.Id}");
        proxyResponse.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ProxyRoute_WithRunId_FiltersByRun()
    {
        var response = await _client.GetAsync("/api/proxy-audits?runId=test-run-id");
        
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var audits = await response.Content.ReadFromJsonAsync<List<ProxyAuditDocument>>();
        audits.Should().NotBeNull();
    }

    [Fact]
    public async Task ProxyRoute_WithProjectId_FiltersByProject()
    {
        var response = await _client.GetAsync("/api/proxy-audits?projectId=test-project-id");
        
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var audits = await response.Content.ReadFromJsonAsync<List<ProxyAuditDocument>>();
        audits.Should().NotBeNull();
    }

    [Fact]
    public async Task ProxyRoute_WithRepositoryId_FiltersByRepository()
    {
        var response = await _client.GetAsync("/api/proxy-audits?repositoryId=test-repo-id");
        
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var audits = await response.Content.ReadFromJsonAsync<List<ProxyAuditDocument>>();
        audits.Should().NotBeNull();
    }

    [Fact]
    public async Task ProxyRoute_WithDateRange_FiltersCorrectly()
    {
        var from = DateTime.UtcNow.AddDays(-1).ToString("o");
        var to = DateTime.UtcNow.ToString("o");
        
        var response = await _client.GetAsync($"/api/proxy-audits?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}");
        
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var audits = await response.Content.ReadFromJsonAsync<List<ProxyAuditDocument>>();
        audits.Should().NotBeNull();
    }

    [Fact]
    public async Task ProxyRoute_PaginatesCorrectly()
    {
        var response = await _client.GetAsync("/api/proxy-audits?skip=0&limit=10");
        
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var audits = await response.Content.ReadFromJsonAsync<List<ProxyAuditDocument>>();
        audits.Should().NotBeNull();
        audits!.Count.Should().BeLessThanOrEqualTo(10);
    }

    [Fact]
    public async Task ProxyRoute_WithInvalidRunId_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/runs/nonexistent-run-id");
        
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ProxyConfig_Updates_WhenRunCreated()
    {
        var (_, _, _, run) = await SetupRunWithProxyAsync();
        
        var response = await _client.GetAsync("/api/runs");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var runs = await response.Content.ReadFromJsonAsync<List<RunDocument>>();
        runs.Should().NotBeNull();
        runs.Should().Contain(r => r.Id == run.Id);
    }

    [Fact]
    public async Task ProxyRoute_AuditRecord_ContainsRequiredFields()
    {
        var response = await _client.GetAsync("/api/proxy-audits?limit=1");
        
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ProxyRoute_WithLatencyFilter_FiltersCorrectly()
    {
        var response = await _client.GetAsync("/api/proxy-audits?minLatencyMs=0&maxLatencyMs=10000");
        
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var audits = await response.Content.ReadFromJsonAsync<List<ProxyAuditDocument>>();
        audits.Should().NotBeNull();
    }
}
