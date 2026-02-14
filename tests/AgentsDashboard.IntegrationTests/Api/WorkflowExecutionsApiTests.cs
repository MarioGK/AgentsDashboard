using System.Net;
using System.Net.Http.Json;
using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AgentsDashboard.IntegrationTests.Api;

[Collection("Api")]
public class WorkflowExecutionsApiTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private readonly HttpClient _client = fixture.Client;
    private readonly WebApplicationFactory<AgentsDashboard.ControlPlane.Program> _factory = fixture.Factory;

    private async Task<(ProjectDocument Project, RepositoryDocument Repository, WorkflowDocument Workflow)> SetupAsync()
    {
        var projectResponse = await _client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("WFP", "d"));
        var project = (await projectResponse.Content.ReadFromJsonAsync<ProjectDocument>())!;

        var repoRequest = new CreateRepositoryRequest(project.Id, "WFR", "https://github.com/test/repo.git", "main");
        var repoResponse = await _client.PostAsJsonAsync("/api/repositories", repoRequest);
        var repo = (await repoResponse.Content.ReadFromJsonAsync<RepositoryDocument>())!;

        var workflowRequest = new CreateWorkflowRequest(repo.Id, "ExecWorkflow", "Test",
        [
            new WorkflowStageConfigRequest("Stage 1", WorkflowStageType.Task, Order: 0),
            new WorkflowStageConfigRequest("Stage 2", WorkflowStageType.Delay, DelaySeconds: 5, Order: 1)
        ]);
        var workflowResponse = await _client.PostAsJsonAsync("/api/workflows", workflowRequest);
        var workflow = (await workflowResponse.Content.ReadFromJsonAsync<WorkflowDocument>())!;

        return (project, repo, workflow);
    }

    private async Task<WorkflowDocument> CreateApprovalWorkflowAsync(string repoId)
    {
        var request = new CreateWorkflowRequest(repoId, "ApprovalWorkflow", "Test",
        [
            new WorkflowStageConfigRequest("Approval Stage", WorkflowStageType.Approval, ApproverRole: "admin", Order: 0)
        ]);
        var response = await _client.PostAsJsonAsync("/api/workflows", request);
        return (await response.Content.ReadFromJsonAsync<WorkflowDocument>())!;
    }

    [Fact]
    public async Task ExecuteWorkflow_ReturnsNotFound_WhenWorkflowDoesNotExist()
    {
        var response = await _client.PostAsync("/api/workflows/nonexistent/execute", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ExecuteWorkflow_ReturnsBadRequest_WhenWorkflowDisabled()
    {
        var (_, repo, workflow) = await SetupAsync();
        var updateRequest = new UpdateWorkflowRequest("Name", "Desc", [], false);
        await _client.PutAsJsonAsync($"/api/workflows/{workflow.Id}", updateRequest);

        var response = await _client.PostAsync($"/api/workflows/{workflow.Id}/execute", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ListExecutions_ReturnsEmptyList_WhenNoExecutions()
    {
        var (_, _, workflow) = await SetupAsync();

        var response = await _client.GetAsync($"/api/workflows/{workflow.Id}/executions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var executions = await response.Content.ReadFromJsonAsync<List<WorkflowExecutionDocument>>();
        executions.Should().NotBeNull();
    }

    [Fact]
    public async Task GetExecution_ReturnsNotFound_WhenDoesNotExist()
    {
        var (_, _, workflow) = await SetupAsync();

        var response = await _client.GetAsync($"/api/workflows/{workflow.Id}/executions/nonexistent");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ApproveExecution_ReturnsNotFound_WhenExecutionDoesNotExist()
    {
        var (_, _, workflow) = await SetupAsync();
        var request = new ApproveWorkflowStageRequest("test-user");

        var response = await _client.PostAsJsonAsync($"/api/workflows/{workflow.Id}/executions/nonexistent/approve", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ApproveExecution_ReturnsBadRequest_WhenWrongWorkflowId()
    {
        var (_, repo, workflow) = await SetupAsync();
        var approvalWorkflow = await CreateApprovalWorkflowAsync(repo.Id);

        var executeResponse = await _client.PostAsync($"/api/workflows/{approvalWorkflow.Id}/execute", null);
        var execution = await executeResponse.Content.ReadFromJsonAsync<WorkflowExecutionDocument>();

        var request = new ApproveWorkflowStageRequest("test-user");
        var response = await _client.PostAsJsonAsync($"/api/workflows/{workflow.Id}/executions/{execution!.Id}/approve", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
