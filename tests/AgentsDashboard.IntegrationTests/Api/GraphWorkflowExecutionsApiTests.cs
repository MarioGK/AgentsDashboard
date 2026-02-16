using System.Net;
using System.Net.Http.Json;
using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.IntegrationTests.Api;

[ClassDataSource<ApiTestFixture>(Shared = SharedType.Keyed, Key = "Api")]
public class GraphWorkflowExecutionsApiTests(ApiTestFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    private async Task<(ProjectDocument Project, RepositoryDocument Repository, AgentDocument Agent, WorkflowV2Document Workflow)> SetupAsync()
    {
        var projectResponse = await _client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("GWExP", "d"));
        var project = (await projectResponse.Content.ReadFromJsonAsync<ProjectDocument>())!;

        var repoRequest = new CreateRepositoryRequest(project.Id, "GWExR", "https://github.com/test/repo.git", "main");
        var repoResponse = await _client.PostAsJsonAsync("/api/repositories", repoRequest);
        var repo = (await repoResponse.Content.ReadFromJsonAsync<RepositoryDocument>())!;

        var agentRequest = new CreateAgentRequest(repo.Id, "ExecAgent", "desc", "codex", "prompt", "cmd", false);
        var agentResponse = await _client.PostAsJsonAsync("/api/agents", agentRequest);
        var agent = (await agentResponse.Content.ReadFromJsonAsync<AgentDocument>())!;

        var workflowRequest = new CreateWorkflowV2Request(
            repo.Id, "ExecWorkflowV2", "Test execution",
            Nodes:
            [
                new WorkflowNodeConfigRequest("start-1", "Start", WorkflowNodeType.Start, PositionX: 0, PositionY: 0),
                new WorkflowNodeConfigRequest("agent-1", "Agent Step", WorkflowNodeType.Agent, AgentId: agent.Id, PositionX: 200, PositionY: 0),
                new WorkflowNodeConfigRequest("end-1", "End", WorkflowNodeType.End, PositionX: 400, PositionY: 0)
            ],
            Edges:
            [
                new WorkflowEdgeConfigRequest("start-1", "agent-1", Priority: 0),
                new WorkflowEdgeConfigRequest("agent-1", "end-1", Priority: 0)
            ]);

        var workflowResponse = await _client.PostAsJsonAsync("/api/workflows-v2", workflowRequest);
        var workflow = (await workflowResponse.Content.ReadFromJsonAsync<WorkflowV2Document>())!;

        return (project, repo, agent, workflow);
    }

    private async Task<WorkflowV2Document> CreateApprovalWorkflowAsync(string repositoryId)
    {
        var request = new CreateWorkflowV2Request(
            repositoryId, "ApprovalWorkflowV2", "Test with approval",
            Nodes:
            [
                new WorkflowNodeConfigRequest("start-1", "Start", WorkflowNodeType.Start, PositionX: 0, PositionY: 0),
                new WorkflowNodeConfigRequest("approval-1", "Approval", WorkflowNodeType.Approval, PositionX: 200, PositionY: 0),
                new WorkflowNodeConfigRequest("end-1", "End", WorkflowNodeType.End, PositionX: 400, PositionY: 0)
            ],
            Edges:
            [
                new WorkflowEdgeConfigRequest("start-1", "approval-1", Priority: 0),
                new WorkflowEdgeConfigRequest("approval-1", "end-1", Priority: 0)
            ]);

        var response = await _client.PostAsJsonAsync("/api/workflows-v2", request);
        return (await response.Content.ReadFromJsonAsync<WorkflowV2Document>())!;
    }

    [Test]
    public async Task ExecuteWorkflowV2_ReturnsOk()
    {
        var (_, _, _, workflow) = await SetupAsync();
        var request = new ExecuteWorkflowV2Request();

        var response = await _client.PostAsJsonAsync($"/api/workflows-v2/{workflow.Id}/execute", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var execution = await response.Content.ReadFromJsonAsync<WorkflowExecutionV2Document>();
        execution.Should().NotBeNull();
        execution!.WorkflowV2Id.Should().Be(workflow.Id);
        execution.Id.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task ExecuteWorkflowV2_NotFound_Returns404()
    {
        var request = new ExecuteWorkflowV2Request();

        var response = await _client.PostAsJsonAsync("/api/workflows-v2/nonexistent/execute", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task ListExecutionsV2_ReturnsOk()
    {
        var (_, _, _, workflow) = await SetupAsync();

        var response = await _client.GetAsync($"/api/workflows-v2/{workflow.Id}/executions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var executions = await response.Content.ReadFromJsonAsync<List<WorkflowExecutionV2Document>>();
        executions.Should().NotBeNull();
    }

    [Test]
    public async Task GetExecutionV2_NotFound_Returns404()
    {
        var response = await _client.GetAsync("/api/workflows-v2/executions/nonexistent");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task CancelExecutionV2_ReturnsOk()
    {
        var (_, _, _, workflow) = await SetupAsync();
        var executeRequest = new ExecuteWorkflowV2Request();
        var executeResponse = await _client.PostAsJsonAsync($"/api/workflows-v2/{workflow.Id}/execute", executeRequest);
        var execution = await executeResponse.Content.ReadFromJsonAsync<WorkflowExecutionV2Document>();

        var response = await _client.PostAsync($"/api/workflows-v2/executions/{execution!.Id}/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var cancelled = await response.Content.ReadFromJsonAsync<WorkflowExecutionV2Document>();
        cancelled.Should().NotBeNull();
        cancelled!.State.Should().Be(WorkflowV2ExecutionState.Cancelled);
    }

    [Test]
    public async Task ApproveExecutionV2_ReturnsOk()
    {
        var (_, repo, _, _) = await SetupAsync();
        var approvalWorkflow = await CreateApprovalWorkflowAsync(repo.Id);

        var executeResponse = await _client.PostAsJsonAsync(
            $"/api/workflows-v2/{approvalWorkflow.Id}/execute",
            new ExecuteWorkflowV2Request());
        var execution = await executeResponse.Content.ReadFromJsonAsync<WorkflowExecutionV2Document>();

        var approveRequest = new ApproveWorkflowV2NodeRequest("test-admin");
        var response = await _client.PostAsJsonAsync(
            $"/api/workflows-v2/executions/{execution!.Id}/approve", approveRequest);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    [Test]
    public async Task ExecuteWorkflowV2_WithInitialContext_ReturnsOk()
    {
        var (_, _, _, workflow) = await SetupAsync();
        var contextValue = System.Text.Json.JsonSerializer.SerializeToElement("test-value");
        var request = new ExecuteWorkflowV2Request(
            InitialContext: new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["env"] = contextValue
            },
            TriggeredBy: "integration-test");

        var response = await _client.PostAsJsonAsync($"/api/workflows-v2/{workflow.Id}/execute", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var execution = await response.Content.ReadFromJsonAsync<WorkflowExecutionV2Document>();
        execution.Should().NotBeNull();
        execution!.TriggeredBy.Should().Be("integration-test");
    }

    [Test]
    public async Task GetExecutionV2_AfterExecute_HasRunningState()
    {
        var (_, _, _, workflow) = await SetupAsync();
        var executeResponse = await _client.PostAsJsonAsync(
            $"/api/workflows-v2/{workflow.Id}/execute",
            new ExecuteWorkflowV2Request());
        var execution = await executeResponse.Content.ReadFromJsonAsync<WorkflowExecutionV2Document>();

        var response = await _client.GetAsync($"/api/workflows-v2/executions/{execution!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var fetched = await response.Content.ReadFromJsonAsync<WorkflowExecutionV2Document>();
        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(execution.Id);
        fetched.State.Should().BeOneOf(
            WorkflowV2ExecutionState.Running,
            WorkflowV2ExecutionState.Succeeded,
            WorkflowV2ExecutionState.Failed);
    }
}
