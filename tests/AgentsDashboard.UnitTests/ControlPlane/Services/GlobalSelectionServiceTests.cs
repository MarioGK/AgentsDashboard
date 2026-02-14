using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Services;
using Microsoft.JSInterop;
using Moq;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public class GlobalSelectionServiceTests
{
    private readonly Mock<OrchestratorStore> _storeMock;
    private readonly Mock<IJSRuntime> _jsRuntimeMock;
    private readonly List<ProjectDocument> _testProjects;
    private readonly List<RepositoryDocument> _testRepositories;

    public GlobalSelectionServiceTests()
    {
        _storeMock = new Mock<OrchestratorStore>(MockBehavior.Loose);
        _jsRuntimeMock = new Mock<IJSRuntime>(MockBehavior.Strict);
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

    private GlobalSelectionService CreateService()
    {
        return new GlobalSelectionService(_storeMock.Object, _jsRuntimeMock.Object);
    }

    [Fact]
    public async Task InitializeAsync_LoadsProjectsFromStore()
    {
        _storeMock.Setup(s => s.ListProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testProjects);
        _storeMock.Setup(s => s.ListRepositoriesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testRepositories);
        _jsRuntimeMock.Setup(j => j.InvokeAsync<string?>("localStorage.getItem", It.IsAny<object[]>()))
            .ReturnsAsync((string?)null);
        _jsRuntimeMock.Setup(j => j.InvokeVoidAsync("localStorage.setItem", It.IsAny<object[]>()))
            .Returns(ValueTask.CompletedTask);

        var service = CreateService();

        await service.InitializeAsync(CancellationToken.None);

        service.Projects.Should().HaveCount(3);
        service.IsInitialized.Should().BeTrue();
    }

    [Fact]
    public async Task InitializeAsync_WithNoSavedSelection_SelectsFirstProject()
    {
        _storeMock.Setup(s => s.ListProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testProjects);
        _storeMock.Setup(s => s.ListRepositoriesAsync("proj-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testRepositories);
        _jsRuntimeMock.Setup(j => j.InvokeAsync<string?>("localStorage.getItem", It.IsAny<object[]>()))
            .ReturnsAsync((string?)null);
        _jsRuntimeMock.Setup(j => j.InvokeVoidAsync("localStorage.setItem", It.IsAny<object[]>()))
            .Returns(ValueTask.CompletedTask);

        var service = CreateService();

        await service.InitializeAsync(CancellationToken.None);

        service.SelectedProjectId.Should().Be("proj-1");
        service.SelectedRepositoryId.Should().Be("repo-1");
    }

    [Fact]
    public async Task InitializeAsync_WithValidSavedProjectId_RestoresSelection()
    {
        _storeMock.Setup(s => s.ListProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testProjects);
        _storeMock.Setup(s => s.ListRepositoriesAsync("proj-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RepositoryDocument { Id = "repo-3", Name = "Repo 3", ProjectId = "proj-2" }]);
        _jsRuntimeMock.SetupSequence(j => j.InvokeAsync<string?>("localStorage.getItem", It.IsAny<object[]>()))
            .ReturnsAsync("proj-2")
            .ReturnsAsync((string?)null);
        _jsRuntimeMock.Setup(j => j.InvokeVoidAsync("localStorage.setItem", It.IsAny<object[]>()))
            .Returns(ValueTask.CompletedTask);

        var service = CreateService();

        await service.InitializeAsync(CancellationToken.None);

        service.SelectedProjectId.Should().Be("proj-2");
    }

    [Fact]
    public async Task InitializeAsync_WithInvalidSavedProjectId_SelectsFirstProject()
    {
        _storeMock.Setup(s => s.ListProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testProjects);
        _storeMock.Setup(s => s.ListRepositoriesAsync("proj-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testRepositories);
        _jsRuntimeMock.SetupSequence(j => j.InvokeAsync<string?>("localStorage.getItem", It.IsAny<object[]>()))
            .ReturnsAsync("invalid-id")
            .ReturnsAsync((string?)null);
        _jsRuntimeMock.Setup(j => j.InvokeVoidAsync("localStorage.setItem", It.IsAny<object[]>()))
            .Returns(ValueTask.CompletedTask);

        var service = CreateService();

        await service.InitializeAsync(CancellationToken.None);

        service.SelectedProjectId.Should().Be("proj-1");
    }

    [Fact]
    public async Task InitializeAsync_WithValidSavedRepoId_RestoresRepoSelection()
    {
        _storeMock.Setup(s => s.ListProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testProjects);
        _storeMock.Setup(s => s.ListRepositoriesAsync("proj-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testRepositories);
        _jsRuntimeMock.SetupSequence(j => j.InvokeAsync<string?>("localStorage.getItem", It.IsAny<object[]>()))
            .ReturnsAsync("proj-1")
            .ReturnsAsync("repo-2");
        _jsRuntimeMock.Setup(j => j.InvokeVoidAsync("localStorage.setItem", It.IsAny<object[]>()))
            .Returns(ValueTask.CompletedTask);

        var service = CreateService();

        await service.InitializeAsync(CancellationToken.None);

        service.SelectedProjectId.Should().Be("proj-1");
        service.SelectedRepositoryId.Should().Be("repo-2");
    }

    [Fact]
    public async Task SelectProjectAsync_UpdatesSelection()
    {
        _storeMock.Setup(s => s.ListProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testProjects);
        _storeMock.Setup(s => s.ListRepositoriesAsync("proj-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RepositoryDocument { Id = "repo-3", Name = "Repo 3", ProjectId = "proj-2" }]);
        _jsRuntimeMock.Setup(j => j.InvokeAsync<string?>("localStorage.getItem", It.IsAny<object[]>()))
            .ReturnsAsync((string?)null);
        _jsRuntimeMock.Setup(j => j.InvokeVoidAsync("localStorage.setItem", It.IsAny<object[]>()))
            .Returns(ValueTask.CompletedTask);

        var service = CreateService();
        await service.InitializeAsync(CancellationToken.None);

        await service.SelectProjectAsync("proj-2", CancellationToken.None);

        service.SelectedProjectId.Should().Be("proj-2");
        service.SelectedProject?.Name.Should().Be("Project 2");
    }

    [Fact]
    public async Task SelectProjectAsync_RaisesSelectionChangedEvent()
    {
        _storeMock.Setup(s => s.ListProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testProjects);
        _storeMock.Setup(s => s.ListRepositoriesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testRepositories);
        _jsRuntimeMock.Setup(j => j.InvokeAsync<string?>("localStorage.getItem", It.IsAny<object[]>()))
            .ReturnsAsync((string?)null);
        _jsRuntimeMock.Setup(j => j.InvokeVoidAsync("localStorage.setItem", It.IsAny<object[]>()))
            .Returns(ValueTask.CompletedTask);

        var service = CreateService();
        await service.InitializeAsync(CancellationToken.None);

        SelectionChangedEventArgs? eventArgs = null;
        service.SelectionChanged += (_, e) => eventArgs = e;

        await service.SelectProjectAsync("proj-2", CancellationToken.None);

        eventArgs.Should().NotBeNull();
        eventArgs!.ProjectId.Should().Be("proj-2");
    }

    [Fact]
    public async Task SelectProjectAsync_WithNull_ClearsSelection()
    {
        _storeMock.Setup(s => s.ListProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testProjects);
        _storeMock.Setup(s => s.ListRepositoriesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testRepositories);
        _jsRuntimeMock.Setup(j => j.InvokeAsync<string?>("localStorage.getItem", It.IsAny<object[]>()))
            .ReturnsAsync((string?)null);
        _jsRuntimeMock.Setup(j => j.InvokeVoidAsync("localStorage.setItem", It.IsAny<object[]>()))
            .Returns(ValueTask.CompletedTask);

        var service = CreateService();
        await service.InitializeAsync(CancellationToken.None);

        await service.SelectProjectAsync(null, CancellationToken.None);

        service.SelectedProjectId.Should().BeNull();
        service.SelectedRepositoryId.Should().BeNull();
        service.Repositories.Should().BeEmpty();
    }

    [Fact]
    public async Task SelectProjectAsync_SameProjectId_DoesNothing()
    {
        _storeMock.Setup(s => s.ListProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testProjects);
        _storeMock.Setup(s => s.ListRepositoriesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testRepositories);
        _jsRuntimeMock.Setup(j => j.InvokeAsync<string?>("localStorage.getItem", It.IsAny<object[]>()))
            .ReturnsAsync((string?)null);
        _jsRuntimeMock.Setup(j => j.InvokeVoidAsync("localStorage.setItem", It.IsAny<object[]>()))
            .Returns(ValueTask.CompletedTask);

        var service = CreateService();
        await service.InitializeAsync(CancellationToken.None);

        var callCountBefore = _jsRuntimeMock.Invocations.Count(i => i.Method.Name == "InvokeVoidAsync");

        await service.SelectProjectAsync("proj-1", CancellationToken.None);

        var callCountAfter = _jsRuntimeMock.Invocations.Count(i => i.Method.Name == "InvokeVoidAsync");
        callCountAfter.Should().Be(callCountBefore);
    }

    [Fact]
    public async Task SelectRepositoryAsync_UpdatesSelection()
    {
        _storeMock.Setup(s => s.ListProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testProjects);
        _storeMock.Setup(s => s.ListRepositoriesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testRepositories);
        _jsRuntimeMock.Setup(j => j.InvokeAsync<string?>("localStorage.getItem", It.IsAny<object[]>()))
            .ReturnsAsync((string?)null);
        _jsRuntimeMock.Setup(j => j.InvokeVoidAsync("localStorage.setItem", It.IsAny<object[]>()))
            .Returns(ValueTask.CompletedTask);

        var service = CreateService();
        await service.InitializeAsync(CancellationToken.None);

        await service.SelectRepositoryAsync("repo-2", CancellationToken.None);

        service.SelectedRepositoryId.Should().Be("repo-2");
        service.SelectedRepository?.Name.Should().Be("Repo 2");
    }

    [Fact]
    public async Task SelectRepositoryAsync_RaisesSelectionChangedEvent()
    {
        _storeMock.Setup(s => s.ListProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testProjects);
        _storeMock.Setup(s => s.ListRepositoriesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testRepositories);
        _jsRuntimeMock.Setup(j => j.InvokeAsync<string?>("localStorage.getItem", It.IsAny<object[]>()))
            .ReturnsAsync((string?)null);
        _jsRuntimeMock.Setup(j => j.InvokeVoidAsync("localStorage.setItem", It.IsAny<object[]>()))
            .Returns(ValueTask.CompletedTask);

        var service = CreateService();
        await service.InitializeAsync(CancellationToken.None);

        SelectionChangedEventArgs? eventArgs = null;
        service.SelectionChanged += (_, e) => eventArgs = e;

        await service.SelectRepositoryAsync("repo-2", CancellationToken.None);

        eventArgs.Should().NotBeNull();
        eventArgs!.RepositoryId.Should().Be("repo-2");
    }

    [Fact]
    public async Task SelectRepositoryAsync_WithNull_ClearsSelection()
    {
        _storeMock.Setup(s => s.ListProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testProjects);
        _storeMock.Setup(s => s.ListRepositoriesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testRepositories);
        _jsRuntimeMock.Setup(j => j.InvokeAsync<string?>("localStorage.getItem", It.IsAny<object[]>()))
            .ReturnsAsync((string?)null);
        _jsRuntimeMock.Setup(j => j.InvokeVoidAsync("localStorage.setItem", It.IsAny<object[]>()))
            .Returns(ValueTask.CompletedTask);

        var service = CreateService();
        await service.InitializeAsync(CancellationToken.None);

        await service.SelectRepositoryAsync(null, CancellationToken.None);

        service.SelectedRepositoryId.Should().BeNull();
        service.SelectedRepository.Should().BeNull();
    }

    [Fact]
    public async Task SelectRepositoryAsync_SameRepoId_DoesNothing()
    {
        _storeMock.Setup(s => s.ListProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testProjects);
        _storeMock.Setup(s => s.ListRepositoriesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testRepositories);
        _jsRuntimeMock.Setup(j => j.InvokeAsync<string?>("localStorage.getItem", It.IsAny<object[]>()))
            .ReturnsAsync((string?)null);
        _jsRuntimeMock.Setup(j => j.InvokeVoidAsync("localStorage.setItem", It.IsAny<object[]>()))
            .Returns(ValueTask.CompletedTask);

        var service = CreateService();
        await service.InitializeAsync(CancellationToken.None);

        var callCountBefore = _jsRuntimeMock.Invocations.Count(i => i.Method.Name == "InvokeVoidAsync");

        await service.SelectRepositoryAsync("repo-1", CancellationToken.None);

        var callCountAfter = _jsRuntimeMock.Invocations.Count(i => i.Method.Name == "InvokeVoidAsync");
        callCountAfter.Should().Be(callCountBefore);
    }

    [Fact]
    public async Task RefreshAsync_ReloadsProjectsAndRepositories()
    {
        var updatedProjects = new List<ProjectDocument>
        {
            new() { Id = "proj-1", Name = "Project 1 Updated" },
            new() { Id = "proj-new", Name = "New Project" }
        };
        var updatedRepos = new List<RepositoryDocument>
        {
            new() { Id = "repo-1", Name = "Repo 1 Updated", ProjectId = "proj-1" }
        };

        _storeMock.Setup(s => s.ListProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testProjects);
        _storeMock.SetupSequence(s => s.ListRepositoriesAsync("proj-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testRepositories)
            .ReturnsAsync(updatedRepos);
        _jsRuntimeMock.Setup(j => j.InvokeAsync<string?>("localStorage.getItem", It.IsAny<object[]>()))
            .ReturnsAsync((string?)null);
        _jsRuntimeMock.Setup(j => j.InvokeVoidAsync("localStorage.setItem", It.IsAny<object[]>()))
            .Returns(ValueTask.CompletedTask);

        var service = CreateService();
        await service.InitializeAsync(CancellationToken.None);

        _storeMock.Setup(s => s.ListProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedProjects);

        await service.RefreshAsync(CancellationToken.None);

        service.Projects.Should().HaveCount(2);
    }

    [Fact]
    public void Subscribe_ReceivesNotifications()
    {
        _storeMock.Setup(s => s.ListProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testProjects);
        _storeMock.Setup(s => s.ListRepositoriesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testRepositories);
        _jsRuntimeMock.Setup(j => j.InvokeAsync<string?>("localStorage.getItem", It.IsAny<object[]>()))
            .ReturnsAsync((string?)null);
        _jsRuntimeMock.Setup(j => j.InvokeVoidAsync("localStorage.setItem", It.IsAny<object[]>()))
            .Returns(ValueTask.CompletedTask);

        var service = CreateService();
        SelectionChangedEventArgs? receivedArgs = null;
        var subscription = service.Subscribe(args => receivedArgs = args);

        service.SelectProjectAsync("proj-2", CancellationToken.None).Wait();

        receivedArgs.Should().NotBeNull();
        receivedArgs!.ProjectId.Should().Be("proj-2");
        subscription.Dispose();
    }

    [Fact]
    public void Subscribe_MultipleSubscribers_AllReceiveNotifications()
    {
        _storeMock.Setup(s => s.ListProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testProjects);
        _storeMock.Setup(s => s.ListRepositoriesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testRepositories);
        _jsRuntimeMock.Setup(j => j.InvokeAsync<string?>("localStorage.getItem", It.IsAny<object[]>()))
            .ReturnsAsync((string?)null);
        _jsRuntimeMock.Setup(j => j.InvokeVoidAsync("localStorage.setItem", It.IsAny<object[]>()))
            .Returns(ValueTask.CompletedTask);

        var service = CreateService();
        var callCount1 = 0;
        var callCount2 = 0;

        var sub1 = service.Subscribe(_ => callCount1++);
        var sub2 = service.Subscribe(_ => callCount2++);

        service.SelectProjectAsync("proj-2", CancellationToken.None).Wait();

        callCount1.Should().Be(1);
        callCount2.Should().Be(1);

        sub1.Dispose();
        sub2.Dispose();
    }

    [Fact]
    public void Subscribe_AfterDispose_StopsReceivingNotifications()
    {
        _storeMock.Setup(s => s.ListProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testProjects);
        _storeMock.Setup(s => s.ListRepositoriesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testRepositories);
        _jsRuntimeMock.Setup(j => j.InvokeAsync<string?>("localStorage.getItem", It.IsAny<object[]>()))
            .ReturnsAsync((string?)null);
        _jsRuntimeMock.Setup(j => j.InvokeVoidAsync("localStorage.setItem", It.IsAny<object[]>()))
            .Returns(ValueTask.CompletedTask);

        var service = CreateService();
        var callCount = 0;
        var subscription = service.Subscribe(_ => callCount++);

        service.SelectProjectAsync("proj-2", CancellationToken.None).Wait();
        subscription.Dispose();
        service.SelectProjectAsync("proj-3", CancellationToken.None).Wait();

        callCount.Should().Be(1);
    }

    [Fact]
    public async Task InitializeAsync_WithEmptyProjects_NoSelection()
    {
        _storeMock.Setup(s => s.ListProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _jsRuntimeMock.Setup(j => j.InvokeAsync<string?>("localStorage.getItem", It.IsAny<object[]>()))
            .ReturnsAsync((string?)null);

        var service = CreateService();

        await service.InitializeAsync(CancellationToken.None);

        service.SelectedProjectId.Should().BeNull();
        service.SelectedRepositoryId.Should().BeNull();
        service.IsInitialized.Should().BeTrue();
    }

    [Fact]
    public async Task InitializeAsync_WithEmptyRepos_SelectsProjectButNoRepo()
    {
        _storeMock.Setup(s => s.ListProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testProjects);
        _storeMock.Setup(s => s.ListRepositoriesAsync("proj-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _jsRuntimeMock.Setup(j => j.InvokeAsync<string?>("localStorage.getItem", It.IsAny<object[]>()))
            .ReturnsAsync((string?)null);
        _jsRuntimeMock.Setup(j => j.InvokeVoidAsync("localStorage.setItem", It.IsAny<object[]>()))
            .Returns(ValueTask.CompletedTask);

        var service = CreateService();

        await service.InitializeAsync(CancellationToken.None);

        service.SelectedProjectId.Should().Be("proj-1");
        service.SelectedRepositoryId.Should().BeNull();
    }

    [Fact]
    public async Task InitializeAsync_CalledTwice_OnlyInitializesOnce()
    {
        var callCount = 0;
        _storeMock.Setup(s => s.ListProjectsAsync(It.IsAny<CancellationToken>()))
            .Callback(() => callCount++)
            .ReturnsAsync(_testProjects);
        _storeMock.Setup(s => s.ListRepositoriesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testRepositories);
        _jsRuntimeMock.Setup(j => j.InvokeAsync<string?>("localStorage.getItem", It.IsAny<object[]>()))
            .ReturnsAsync((string?)null);
        _jsRuntimeMock.Setup(j => j.InvokeVoidAsync("localStorage.setItem", It.IsAny<object[]>()))
            .Returns(ValueTask.CompletedTask);

        var service = CreateService();

        await service.InitializeAsync(CancellationToken.None);
        await service.InitializeAsync(CancellationToken.None);

        callCount.Should().Be(1);
    }

    [Fact]
    public void Dispose_CleansUpResources()
    {
        _storeMock.Setup(s => s.ListProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testProjects);
        _storeMock.Setup(s => s.ListRepositoriesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testRepositories);
        _jsRuntimeMock.Setup(j => j.InvokeAsync<string?>("localStorage.getItem", It.IsAny<object[]>()))
            .ReturnsAsync((string?)null);
        _jsRuntimeMock.Setup(j => j.InvokeVoidAsync("localStorage.setItem", It.IsAny<object[]>()))
            .Returns(ValueTask.CompletedTask);

        var service = CreateService();
        service.InitializeAsync(CancellationToken.None).Wait();

        var act = () => service.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_NoException()
    {
        var service = CreateService();

        service.Dispose();
        var act = () => service.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public async Task SelectRepositoryAsync_WithInvalidId_DoesNotThrow()
    {
        _storeMock.Setup(s => s.ListProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testProjects);
        _storeMock.Setup(s => s.ListRepositoriesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testRepositories);
        _jsRuntimeMock.Setup(j => j.InvokeAsync<string?>("localStorage.getItem", It.IsAny<object[]>()))
            .ReturnsAsync((string?)null);
        _jsRuntimeMock.Setup(j => j.InvokeVoidAsync("localStorage.setItem", It.IsAny<object[]>()))
            .Returns(ValueTask.CompletedTask);

        var service = CreateService();
        await service.InitializeAsync(CancellationToken.None);

        var act = async () => await service.SelectRepositoryAsync("invalid-repo", CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task InitializeAsync_ConcurrentCalls_OnlyInitializesOnce()
    {
        var callCount = 0;
        _storeMock.Setup(s => s.ListProjectsAsync(It.IsAny<CancellationToken>()))
            .Callback(async () =>
            {
                callCount++;
                await Task.Delay(100);
            })
            .ReturnsAsync(_testProjects);
        _storeMock.Setup(s => s.ListRepositoriesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testRepositories);
        _jsRuntimeMock.Setup(j => j.InvokeAsync<string?>("localStorage.getItem", It.IsAny<object[]>()))
            .ReturnsAsync((string?)null);
        _jsRuntimeMock.Setup(j => j.InvokeVoidAsync("localStorage.setItem", It.IsAny<object[]>()))
            .Returns(ValueTask.CompletedTask);

        var service = CreateService();

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => service.InitializeAsync(CancellationToken.None))
            .ToArray();

        await Task.WhenAll(tasks);

        callCount.Should().Be(1);
    }
}
