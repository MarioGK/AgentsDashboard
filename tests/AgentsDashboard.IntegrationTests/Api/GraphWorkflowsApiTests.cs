using System.Net;
using System.Net.Http.Json;
using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;
using FluentAssertions;

namespace AgentsDashboard.IntegrationTests.Api;

[Collection("Api")]
public class GraphWorkflowsApiTests(ApiTestFixture fixture) : IClassFixture<ApiTestFixture>
{
    private readonly HttpClient _client = fixture.Client;

    private async Task<(ProjectDocument Project, RepositoryDocument Repository, AgentDocument Agent)> SetupAsync()
    {
        var projectResponse = await _client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("GWP", "d"));
        var project = (await projectResponse.Content.ReadFromJsonAsync<ProjectDocument>())!;

        var repoRequest = new CreateRepositoryRequest(project.Id, "GWR", "https://github.com/test/repo.git", "main");
        var repoResponse = await _client.PostAsJsonAsync("/api/repositories", repoRequest);
        var repo = (await repoResponse.Content.ReadFromJsonAsync<RepositoryDocument>())!;

        var agentRequest = new CreateAgentRequest(repo.Id, "GraphAgent", "desc", "codex", "prompt", "cmd", false);
        var agentResponse = await _client.PostAsJsonAsync("/api/agents", agentRequest);
        var agent = (await agentResponse.Content.ReadFromJsonAsync<AgentDocument>())!;

        return (project, repo, agent);
    }

    private static CreateWorkflowV2Request MakeWorkflowV2Request(string repositoryId, string agentId, string name = "Test Graph Workflow") =>
        new(repositoryId, name, "A test graph workflow",
            Nodes:
            [
                new WorkflowNodeConfigRequest("start-1", "Start", WorkflowNodeType.Start, PositionX: 0, PositionY: 0),
                new WorkflowNodeConfigRequest("agent-1", "Agent Step", WorkflowNodeType.Agent, AgentId: agentId, PositionX: 200, PositionY: 0),
                new WorkflowNodeConfigRequest("end-1", "End", WorkflowNodeType.End, PositionX: 400, PositionY: 0)
            ],
            Edges:
            [
                new WorkflowEdgeConfigRequest("start-1", "agent-1", Priority: 0),
                new WorkflowEdgeConfigRequest("agent-1", "end-1", Priority: 0)
            ]);

    [Fact]
    public async Task ListWorkflowsV2_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/workflows-v2");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var workflows = await response.Content.ReadFromJsonAsync<List<WorkflowV2Document>>();
        workflows.Should().NotBeNull();
    }

    [Fact]
    public async Task GetWorkflowV2_NotFound_Returns404()
    {
        var response = await _client.GetAsync("/api/workflows-v2/nonexistent");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateWorkflowV2_Valid_ReturnsOk()
    {
        var (_, repo, agent) = await SetupAsync();
        var request = MakeWorkflowV2Request(repo.Id, agent.Id);

        var response = await _client.PostAsJsonAsync("/api/workflows-v2", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var workflow = await response.Content.ReadFromJsonAsync<WorkflowV2Document>();
        workflow.Should().NotBeNull();
        workflow!.Name.Should().Be("Test Graph Workflow");
        workflow.RepositoryId.Should().Be(repo.Id);
        workflow.Nodes.Should().HaveCount(3);
        workflow.Edges.Should().HaveCount(2);
        workflow.Enabled.Should().BeTrue();
        workflow.MaxConcurrentNodes.Should().Be(4);
        workflow.Id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CreateWorkflowV2_MissingName_ReturnsValidationError()
    {
        var (_, repo, agent) = await SetupAsync();
        var request = MakeWorkflowV2Request(repo.Id, agent.Id, name: "");

        var response = await _client.PostAsJsonAsync("/api/workflows-v2", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateWorkflowV2_InvalidGraph_ReturnsValidationError()
    {
        var (_, repo, _) = await SetupAsync();

        var request = new CreateWorkflowV2Request(
            repo.Id, "Invalid Graph", "No start node",
            Nodes:
            [
                new WorkflowNodeConfigRequest("end-1", "End", WorkflowNodeType.End, PositionX: 0, PositionY: 0)
            ],
            Edges: []);

        var response = await _client.PostAsJsonAsync("/api/workflows-v2", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateWorkflowV2_Valid_ReturnsOk()
    {
        var (_, repo, agent) = await SetupAsync();
        var createResponse = await _client.PostAsJsonAsync("/api/workflows-v2", MakeWorkflowV2Request(repo.Id, agent.Id));
        var created = await createResponse.Content.ReadFromJsonAsync<WorkflowV2Document>();

        var updateRequest = new UpdateWorkflowV2Request(
            "Updated Graph Workflow", "Updated desc",
            Nodes:
            [
                new WorkflowNodeConfigRequest("start-1", "Start", WorkflowNodeType.Start, PositionX: 0, PositionY: 0),
                new WorkflowNodeConfigRequest("agent-1", "Updated Agent", WorkflowNodeType.Agent, AgentId: agent.Id, PositionX: 200, PositionY: 0),
                new WorkflowNodeConfigRequest("end-1", "End", WorkflowNodeType.End, PositionX: 400, PositionY: 0)
            ],
            Edges:
            [
                new WorkflowEdgeConfigRequest("start-1", "agent-1", Priority: 0),
                new WorkflowEdgeConfigRequest("agent-1", "end-1", Priority: 0)
            ],
            Enabled: false,
            MaxConcurrentNodes: 2);

        var response = await _client.PutAsJsonAsync($"/api/workflows-v2/{created!.Id}", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<WorkflowV2Document>();
        updated!.Name.Should().Be("Updated Graph Workflow");
        updated.Description.Should().Be("Updated desc");
        updated.Enabled.Should().BeFalse();
        updated.MaxConcurrentNodes.Should().Be(2);
        updated.Nodes.Should().HaveCount(3);
    }

    [Fact]
    public async Task UpdateWorkflowV2_NotFound_Returns404()
    {
        var (_, repo, agent) = await SetupAsync();

        var request = new UpdateWorkflowV2Request(
            "Name", "Desc",
            Nodes:
            [
                new WorkflowNodeConfigRequest("start-1", "Start", WorkflowNodeType.Start),
                new WorkflowNodeConfigRequest("end-1", "End", WorkflowNodeType.End)
            ],
            Edges:
            [
                new WorkflowEdgeConfigRequest("start-1", "end-1")
            ]);

        var response = await _client.PutAsJsonAsync("/api/workflows-v2/nonexistent", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteWorkflowV2_Valid_ReturnsOk()
    {
        var (_, repo, agent) = await SetupAsync();
        var createResponse = await _client.PostAsJsonAsync("/api/workflows-v2", MakeWorkflowV2Request(repo.Id, agent.Id));
        var created = await createResponse.Content.ReadFromJsonAsync<WorkflowV2Document>();

        var response = await _client.DeleteAsync($"/api/workflows-v2/{created!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteWorkflowV2_NotFound_Returns404()
    {
        var response = await _client.DeleteAsync("/api/workflows-v2/nonexistent");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListWorkflowsV2ByRepository_ReturnsFiltered()
    {
        var (_, repo, agent) = await SetupAsync();
        await _client.PostAsJsonAsync("/api/workflows-v2", MakeWorkflowV2Request(repo.Id, agent.Id, "Filtered WF"));

        var response = await _client.GetAsync($"/api/repositories/{repo.Id}/workflows-v2");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var workflows = await response.Content.ReadFromJsonAsync<List<WorkflowV2Document>>();
        workflows.Should().Contain(w => w.Name == "Filtered WF");
    }
}
