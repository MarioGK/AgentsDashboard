using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Services;
using Microsoft.JSInterop;
using Moq;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public class ProjectContextTests
{
    private readonly Mock<IOrchestratorStore> _storeMock;
    private readonly Mock<IJSRuntime> _jsRuntimeMock;
    private readonly List<ProjectDocument> _testProjects;
    private readonly List<RepositoryDocument> _testRepositories;

    public ProjectContextTests()
    {
        _storeMock = new Mock<IOrchestratorStore>(MockBehavior.Strict);
        _jsRuntimeMock = new Mock<IJSRuntime>(MockBehavior.Loose);
        _testProjects =
        [
            new ProjectDocument { Id = "proj-1", Name = "Project 1" },
            new ProjectDocument { Id = "proj-2", Name = "Project 2" },
            new ProjectDocument { Id = "proj-3", Name = "Project 3" }
        ];
        _testRepositories =
        [
            new RepositoryDocument { Id = "repo-1", Name = "Repo 1", ProjectId = "proj-1" },
            new RepositoryDocument { Id = "repo-2", Name = "Repo 2", ProjectId = "proj-1" }
        ];
    }

    private ProjectContext CreateContext()
    {
        return new ProjectContext(_storeMock.Object, _jsRuntimeMock.Object);
    }

    private void SetupJsRuntimeForGetItem(params string?[] returnValues)
    {
        var sequence = _jsRuntimeMock.SetupSequence(j => j.InvokeAsync<string?>("localStorage.getItem", It.IsAny<object[]>()));
        foreach (var value in returnValues)
        {
            sequence.ReturnsAsync(value);
        }
    }

    private void SetupJsRuntimeForSetItem()
    {
        _jsRuntimeMock.Setup(j => j.InvokeAsync<ValueTuple>("localStorage.setItem", It.IsAny<object[]>()))
            .ReturnsAsync(ValueTuple.Create);
    }

    [Fact]
    public async Task InitializeAsync_WhenAlreadyInitialized_SkipsReload()
    {
        _storeMock.Setup(s => s.ListProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testProjects);
        _storeMock.Setup(s => s.ListRepositoriesAsync("proj-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testRepositories);
        SetupJsRuntimeForGetItem(null);
        SetupJsRuntimeForSetItem();

        var context = CreateContext();
        await context.InitializeAsync(CancellationToken.None);
        await context.InitializeAsync(CancellationToken.None);

        _storeMock.Verify(s => s.ListProjectsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InitializeAsync_WithNoProjects_SetsEmptyLists()
    {
        _storeMock.Setup(s => s.ListProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProjectDocument>());
        SetupJsRuntimeForGetItem(null, null);

        var context = CreateContext();

        await context.InitializeAsync(CancellationToken.None);

        context.Projects.Should().BeEmpty();
        context.Repositories.Should().BeEmpty();
        context.SelectedProjectId.Should().BeNull();
    }

    [Fact]
    public async Task InitializeAsync_WithProjectsNoSavedState_AutoSelectsFirstProject()
    {
        _storeMock.Setup(s => s.ListProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testProjects);
        _storeMock.Setup(s => s.ListRepositoriesAsync("proj-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testRepositories);
        SetupJsRuntimeForGetItem(null);
        SetupJsRuntimeForSetItem();

        var context = CreateContext();

        await context.InitializeAsync(CancellationToken.None);

        context.SelectedProjectId.Should().Be("proj-1");
        context.SelectedRepositoryId.Should().Be("repo-1");
    }

    [Fact]
    public async Task InitializeAsync_WithSavedProjectId_RestoresSelection()
    {
        var proj2Repos = new List<RepositoryDocument>
        {
            new() { Id = "repo-3", Name = "Repo 3", ProjectId = "proj-2" }
        };
        _storeMock.Setup(s => s.ListProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testProjects);
        _storeMock.Setup(s => s.ListRepositoriesAsync("proj-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(proj2Repos);
        SetupJsRuntimeForGetItem("proj-2", null);
        SetupJsRuntimeForSetItem();

        var context = CreateContext();

        await context.InitializeAsync(CancellationToken.None);

        context.SelectedProjectId.Should().Be("proj-2");
        context.SelectedRepositoryId.Should().Be("repo-3");
    }

    [Fact]
    public async Task InitializeAsync_WithInvalidSavedProjectId_FallsBackToFirstProject()
    {
        _storeMock.Setup(s => s.ListProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testProjects);
        _storeMock.Setup(s => s.ListRepositoriesAsync("proj-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testRepositories);
        SetupJsRuntimeForGetItem("non-existent-proj", null);
        SetupJsRuntimeForSetItem();

        var context = CreateContext();

        await context.InitializeAsync(CancellationToken.None);

        context.SelectedProjectId.Should().Be("proj-1");
    }

    [Fact]
    public async Task InitializeAsync_WithSavedProjectAndRepo_RestoresBothSelections()
    {
        _storeMock.Setup(s => s.ListProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testProjects);
        _storeMock.Setup(s => s.ListRepositoriesAsync("proj-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testRepositories);
        SetupJsRuntimeForGetItem("proj-1", "repo-2");

        var context = CreateContext();

        await context.InitializeAsync(CancellationToken.None);

        context.SelectedProjectId.Should().Be("proj-1");
        context.SelectedRepositoryId.Should().Be("repo-2");
    }

    [Fact]
    public async Task InitializeAsync_WithInvalidSavedRepoId_FallsBackToFirstRepo()
    {
        _storeMock.Setup(s => s.ListProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testProjects);
        _storeMock.Setup(s => s.ListRepositoriesAsync("proj-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testRepositories);
        SetupJsRuntimeForGetItem("proj-1", "non-existent-repo");

        var context = CreateContext();

        await context.InitializeAsync(CancellationToken.None);

        context.SelectedProjectId.Should().Be("proj-1");
        context.SelectedRepositoryId.Should().BeNull();
    }

    [Fact]
    public async Task SelectProjectAsync_WithValidId_UpdatesSelectionAndLoadsRepos()
    {
        var proj2Repos = new List<RepositoryDocument>
        {
            new() { Id = "repo-3", Name = "Repo 3", ProjectId = "proj-2" }
        };
        _storeMock.Setup(s => s.ListRepositoriesAsync("proj-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(proj2Repos);
        SetupJsRuntimeForSetItem();

        var context = CreateContext();

        await context.SelectProjectAsync("proj-2", CancellationToken.None);

        context.SelectedProjectId.Should().Be("proj-2");
        context.Repositories.Should().HaveCount(1);
        context.Repositories[0].Id.Should().Be("repo-3");
    }

    [Fact]
    public async Task SelectProjectAsync_WithNullId_ClearsSelection()
    {
        SetupJsRuntimeForSetItem();

        var context = CreateContext();

        await context.SelectProjectAsync(null, CancellationToken.None);

        context.SelectedProjectId.Should().BeNull();
        context.SelectedRepositoryId.Should().BeNull();
        context.Repositories.Should().BeEmpty();
    }

    [Fact]
    public async Task SelectProjectAsync_WithProjectHavingNoRepos_SetsEmptyRepoList()
    {
        _storeMock.Setup(s => s.ListRepositoriesAsync("proj-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RepositoryDocument>());
        SetupJsRuntimeForSetItem();

        var context = CreateContext();

        await context.SelectProjectAsync("proj-1", CancellationToken.None);

        context.SelectedProjectId.Should().Be("proj-1");
        context.Repositories.Should().BeEmpty();
        context.SelectedRepositoryId.Should().BeNull();
    }

    [Fact]
    public async Task SelectProjectAsync_AutoSelectsFirstRepository()
    {
        _storeMock.Setup(s => s.ListRepositoriesAsync("proj-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testRepositories);
        SetupJsRuntimeForSetItem();

        var context = CreateContext();

        await context.SelectProjectAsync("proj-1", CancellationToken.None);

        context.SelectedRepositoryId.Should().Be("repo-1");
    }

    [Fact]
    public async Task SelectRepositoryAsync_WithValidId_UpdatesSelection()
    {
        SetupJsRuntimeForSetItem();

        var context = CreateContext();

        await context.SelectRepositoryAsync("repo-2", CancellationToken.None);

        context.SelectedRepositoryId.Should().Be("repo-2");
    }

    [Fact]
    public async Task SelectRepositoryAsync_WithNullId_ClearsSelection()
    {
        SetupJsRuntimeForSetItem();

        var context = CreateContext();

        await context.SelectRepositoryAsync(null, CancellationToken.None);

        context.SelectedRepositoryId.Should().BeNull();
    }
}
