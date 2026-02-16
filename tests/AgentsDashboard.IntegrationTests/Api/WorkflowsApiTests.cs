using System.Net;
using System.Net.Http.Json;
using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.IntegrationTests.Api;

[ClassDataSource<ApiTestFixture>(Shared = SharedType.Keyed, Key = "Api")]
public class WorkflowsApiTests(ApiTestFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    private async Task<(ProjectDocument Project, RepositoryDocument Repository)> SetupAsync()
    {
        var projectResponse = await _client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("WP", "d"));
        var project = (await projectResponse.Content.ReadFromJsonAsync<ProjectDocument>())!;

        var repoRequest = new CreateRepositoryRequest(project.Id, "WR", "https://github.com/test/repo.git", "main");
        var repoResponse = await _client.PostAsJsonAsync("/api/repositories", repoRequest);
        var repo = (await repoResponse.Content.ReadFromJsonAsync<RepositoryDocument>())!;

        return (project, repo);
    }

    private CreateWorkflowRequest MakeWorkflowRequest(string repoId, string name = "Test Workflow") =>
        new(repoId, name, "A test workflow", [
            new WorkflowStageConfigRequest("Stage 1", WorkflowStageType.Task, Order: 0),
            new WorkflowStageConfigRequest("Stage 2", WorkflowStageType.Delay, DelaySeconds: 5, Order: 1)
        ]);

    [Test]
    public async Task ListWorkflows_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/workflows");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task CreateWorkflow_ReturnsCreatedWorkflow()
    {
        var (_, repo) = await SetupAsync();
        var request = MakeWorkflowRequest(repo.Id);

        var response = await _client.PostAsJsonAsync("/api/workflows", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var workflow = await response.Content.ReadFromJsonAsync<WorkflowDocument>();
        workflow.Should().NotBeNull();
        workflow!.Name.Should().Be("Test Workflow");
        workflow.RepositoryId.Should().Be(repo.Id);
        workflow.Stages.Should().HaveCount(2);
        workflow.Enabled.Should().BeTrue();
    }

    [Test]
    public async Task CreateWorkflow_ReturnsNotFound_WhenRepositoryDoesNotExist()
    {
        var request = MakeWorkflowRequest("nonexistent");
        var response = await _client.PostAsJsonAsync("/api/workflows", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetWorkflow_ReturnsWorkflow_WhenExists()
    {
        var (_, repo) = await SetupAsync();
        var createResponse = await _client.PostAsJsonAsync("/api/workflows", MakeWorkflowRequest(repo.Id));
        var created = await createResponse.Content.ReadFromJsonAsync<WorkflowDocument>();

        var response = await _client.GetAsync($"/api/workflows/{created!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var workflow = await response.Content.ReadFromJsonAsync<WorkflowDocument>();
        workflow!.Id.Should().Be(created.Id);
    }

    [Test]
    public async Task GetWorkflow_ReturnsNotFound_WhenDoesNotExist()
    {
        var response = await _client.GetAsync("/api/workflows/nonexistent");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task UpdateWorkflow_ReturnsUpdatedWorkflow()
    {
        var (_, repo) = await SetupAsync();
        var createResponse = await _client.PostAsJsonAsync("/api/workflows", MakeWorkflowRequest(repo.Id));
        var created = await createResponse.Content.ReadFromJsonAsync<WorkflowDocument>();

        var updateRequest = new UpdateWorkflowRequest("Updated Workflow", "Updated desc", [
            new WorkflowStageConfigRequest("New Stage", WorkflowStageType.Approval, ApproverRole: "admin", Order: 0)
        ], false);

        var response = await _client.PutAsJsonAsync($"/api/workflows/{created!.Id}", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<WorkflowDocument>();
        updated!.Name.Should().Be("Updated Workflow");
        updated.Enabled.Should().BeFalse();
        updated.Stages.Should().ContainSingle();
    }

    [Test]
    public async Task UpdateWorkflow_ReturnsNotFound_WhenDoesNotExist()
    {
        var request = new UpdateWorkflowRequest("Name", "Desc", [], true);
        var response = await _client.PutAsJsonAsync("/api/workflows/nonexistent", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task DeleteWorkflow_ReturnsOk()
    {
        var (_, repo) = await SetupAsync();
        var createResponse = await _client.PostAsJsonAsync("/api/workflows", MakeWorkflowRequest(repo.Id));
        var created = await createResponse.Content.ReadFromJsonAsync<WorkflowDocument>();

        var response = await _client.DeleteAsync($"/api/workflows/{created!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task ListRepositoryWorkflows_ReturnsWorkflowsForRepository()
    {
        var (_, repo) = await SetupAsync();
        await _client.PostAsJsonAsync("/api/workflows", MakeWorkflowRequest(repo.Id, "WF1"));

        var response = await _client.GetAsync($"/api/repositories/{repo.Id}/workflows");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var workflows = await response.Content.ReadFromJsonAsync<List<WorkflowDocument>>();
        workflows.Should().Contain(w => w.Name == "WF1");
    }
}
