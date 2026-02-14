using System.Net;
using System.Net.Http.Json;
using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;
using FluentAssertions;

namespace AgentsDashboard.IntegrationTests.Api;

[Collection("Api")]
public class ExistingWorkflowsRegressionTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private readonly HttpClient _client = fixture.Client;

    private async Task<(ProjectDocument Project, RepositoryDocument Repository)> SetupAsync()
    {
        var projectResponse = await _client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("RegP", "d"));
        var project = (await projectResponse.Content.ReadFromJsonAsync<ProjectDocument>())!;

        var repoRequest = new CreateRepositoryRequest(project.Id, "RegR", "https://github.com/test/repo.git", "main");
        var repoResponse = await _client.PostAsJsonAsync("/api/repositories", repoRequest);
        var repo = (await repoResponse.Content.ReadFromJsonAsync<RepositoryDocument>())!;

        return (project, repo);
    }

    private static CreateWorkflowRequest MakeV1WorkflowRequest(string repoId, string name = "V1 Workflow") =>
        new(repoId, name, "Stage-based workflow",
        [
            new WorkflowStageConfigRequest("Stage 1", WorkflowStageType.Task, Order: 0),
            new WorkflowStageConfigRequest("Stage 2", WorkflowStageType.Delay, DelaySeconds: 5, Order: 1)
        ]);

    [Fact]
    public async Task ListWorkflows_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/workflows");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var workflows = await response.Content.ReadFromJsonAsync<List<WorkflowDocument>>();
        workflows.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateWorkflow_Valid_ReturnsOk()
    {
        var (_, repo) = await SetupAsync();
        var request = MakeV1WorkflowRequest(repo.Id);

        var response = await _client.PostAsJsonAsync("/api/workflows", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var workflow = await response.Content.ReadFromJsonAsync<WorkflowDocument>();
        workflow.Should().NotBeNull();
        workflow!.Name.Should().Be("V1 Workflow");
        workflow.RepositoryId.Should().Be(repo.Id);
        workflow.Stages.Should().HaveCount(2);
        workflow.Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task GetWorkflow_ReturnsOk()
    {
        var (_, repo) = await SetupAsync();
        var createResponse = await _client.PostAsJsonAsync("/api/workflows", MakeV1WorkflowRequest(repo.Id));
        var created = await createResponse.Content.ReadFromJsonAsync<WorkflowDocument>();

        var response = await _client.GetAsync($"/api/workflows/{created!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var workflow = await response.Content.ReadFromJsonAsync<WorkflowDocument>();
        workflow!.Id.Should().Be(created.Id);
        workflow.Name.Should().Be("V1 Workflow");
    }

    [Fact]
    public async Task DeleteWorkflow_ReturnsOk()
    {
        var (_, repo) = await SetupAsync();
        var createResponse = await _client.PostAsJsonAsync("/api/workflows", MakeV1WorkflowRequest(repo.Id));
        var created = await createResponse.Content.ReadFromJsonAsync<WorkflowDocument>();

        var response = await _client.DeleteAsync($"/api/workflows/{created!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task BothWorkflowTypesCoexist()
    {
        var (_, repo) = await SetupAsync();

        var agentRequest = new CreateAgentRequest(repo.Id, "CoexistAgent", "desc", "codex", "prompt", "cmd", false);
        var agentResponse = await _client.PostAsJsonAsync("/api/agents", agentRequest);
        var agent = (await agentResponse.Content.ReadFromJsonAsync<AgentDocument>())!;

        var v1Response = await _client.PostAsJsonAsync("/api/workflows", MakeV1WorkflowRequest(repo.Id, "Coexist V1"));
        v1Response.StatusCode.Should().Be(HttpStatusCode.OK);
        var v1Workflow = await v1Response.Content.ReadFromJsonAsync<WorkflowDocument>();

        var v2Request = new CreateWorkflowV2Request(
            repo.Id, "Coexist V2", "Graph-based workflow",
            Nodes:
            [
                new WorkflowNodeConfigRequest("start-1", "Start", WorkflowNodeType.Start, PositionX: 0, PositionY: 0),
                new WorkflowNodeConfigRequest("agent-1", "Agent", WorkflowNodeType.Agent, AgentId: agent.Id, PositionX: 200, PositionY: 0),
                new WorkflowNodeConfigRequest("end-1", "End", WorkflowNodeType.End, PositionX: 400, PositionY: 0)
            ],
            Edges:
            [
                new WorkflowEdgeConfigRequest("start-1", "agent-1", Priority: 0),
                new WorkflowEdgeConfigRequest("agent-1", "end-1", Priority: 0)
            ]);

        var v2Response = await _client.PostAsJsonAsync("/api/workflows-v2", v2Request);
        v2Response.StatusCode.Should().Be(HttpStatusCode.OK);
        var v2Workflow = await v2Response.Content.ReadFromJsonAsync<WorkflowV2Document>();

        var v1ListResponse = await _client.GetAsync("/api/workflows");
        v1ListResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var v1Workflows = await v1ListResponse.Content.ReadFromJsonAsync<List<WorkflowDocument>>();
        v1Workflows.Should().Contain(w => w.Name == "Coexist V1");

        var v2ListResponse = await _client.GetAsync("/api/workflows-v2");
        v2ListResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var v2Workflows = await v2ListResponse.Content.ReadFromJsonAsync<List<WorkflowV2Document>>();
        v2Workflows.Should().Contain(w => w.Name == "Coexist V2");

        var v1Get = await _client.GetAsync($"/api/workflows/{v1Workflow!.Id}");
        v1Get.StatusCode.Should().Be(HttpStatusCode.OK);

        var v2Get = await _client.GetAsync($"/api/workflows-v2/{v2Workflow!.Id}");
        v2Get.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
