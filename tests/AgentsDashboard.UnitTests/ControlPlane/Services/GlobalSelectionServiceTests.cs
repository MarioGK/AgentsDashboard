using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Services;
using Moq;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public class GlobalSelectionServiceTests
{
    private readonly Mock<IOrchestratorStore> _storeMock;
    private readonly Mock<ILocalStorageService> _localStorageMock;
    private readonly List<ProjectDocument> _testProjects;
    private readonly List<RepositoryDocument> _testRepositories;

    public GlobalSelectionServiceTests()
    {
        _storeMock = new Mock<IOrchestratorStore>(MockBehavior.Loose);
        _localStorageMock = new Mock<ILocalStorageService>(MockBehavior.Loose);
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

    private void SetupLocalStorageMock(string? projectId = null, string? repoId = null)
    {
        _localStorageMock.Setup(x => x.GetItemAsync("selectedProjectId"))
            .ReturnsAsync(projectId);
        _localStorageMock.Setup(x => x.GetItemAsync("selectedRepositoryId"))
            .ReturnsAsync(repoId);
        _localStorageMock.Setup(x => x.SetItemAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
    }

    private void SetupDefaultStoreMocks()
    {
        _storeMock.Setup(s => s.ListProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testProjects);
        _storeMock.Setup(s => s.ListRepositoriesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string projectId, CancellationToken _) =>
                _testRepositories.Where(r => r.ProjectId == projectId).ToList());
    }

    private GlobalSelectionService CreateService()
    {
        return new GlobalSelectionService(_storeMock.Object, _localStorageMock.Object);
    }

    [Test]
    public async Task InitializeAsync_LoadsProjectsFromStore()
    {
        SetupDefaultStoreMocks();
        SetupLocalStorageMock();

        var service = CreateService();

        await service.InitializeAsync(CancellationToken.None);

        service.Projects.Should().HaveCount(3);
        service.IsInitialized.Should().BeTrue();
    }

    [Test]
    public async Task InitializeAsync_WithNoSavedSelection_SelectsFirstProject()
    {
        SetupDefaultStoreMocks();
        SetupLocalStorageMock();

        var service = CreateService();

        await service.InitializeAsync(CancellationToken.None);

        service.SelectedProjectId.Should().Be("proj-1");
        service.SelectedRepositoryId.Should().Be("repo-1");
    }

    [Test]
    public async Task InitializeAsync_WithValidSavedProjectId_RestoresSelection()
    {
        _storeMock.Setup(s => s.ListProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testProjects);
        _storeMock.Setup(s => s.ListRepositoriesAsync("proj-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RepositoryDocument { Id = "repo-3", Name = "Repo 3", ProjectId = "proj-2" }]);
        SetupLocalStorageMock("proj-2", null);

        var service = CreateService();

        await service.InitializeAsync(CancellationToken.None);

        service.SelectedProjectId.Should().Be("proj-2");
    }

    [Test]
    public async Task InitializeAsync_WithInvalidSavedProjectId_SelectsFirstProject()
    {
        SetupDefaultStoreMocks();
        SetupLocalStorageMock("invalid-id", null);

        var service = CreateService();

        await service.InitializeAsync(CancellationToken.None);

        service.SelectedProjectId.Should().Be("proj-1");
    }

    [Test]
    public async Task InitializeAsync_WithValidSavedRepoId_RestoresRepoSelection()
    {
        SetupDefaultStoreMocks();
        SetupLocalStorageMock("proj-1", "repo-2");

        var service = CreateService();

        await service.InitializeAsync(CancellationToken.None);

        service.SelectedProjectId.Should().Be("proj-1");
        service.SelectedRepositoryId.Should().Be("repo-2");
    }

    [Test]
    public async Task SelectProjectAsync_UpdatesSelection()
    {
        _storeMock.Setup(s => s.ListProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testProjects);
        _storeMock.Setup(s => s.ListRepositoriesAsync("proj-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testRepositories);
        _storeMock.Setup(s => s.ListRepositoriesAsync("proj-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RepositoryDocument { Id = "repo-3", Name = "Repo 3", ProjectId = "proj-2" }]);
        SetupLocalStorageMock();

        var service = CreateService();
        await service.InitializeAsync(CancellationToken.None);

        await service.SelectProjectAsync("proj-2", CancellationToken.None);

        service.SelectedProjectId.Should().Be("proj-2");
        service.SelectedProject?.Name.Should().Be("Project 2");
    }

    [Test]
    public async Task SelectProjectAsync_RaisesSelectionChangedEvent()
    {
        SetupDefaultStoreMocks();
        SetupLocalStorageMock();

        var service = CreateService();
        await service.InitializeAsync(CancellationToken.None);

        SelectionChangedEventArgs? eventArgs = null;
        service.SelectionChanged += (_, e) => eventArgs = e;

        await service.SelectProjectAsync("proj-2", CancellationToken.None);

        eventArgs.Should().NotBeNull();
        eventArgs!.ProjectId.Should().Be("proj-2");
    }

    [Test]
    public async Task SelectProjectAsync_WithNull_ClearsSelection()
    {
        SetupDefaultStoreMocks();
        SetupLocalStorageMock();

        var service = CreateService();
        await service.InitializeAsync(CancellationToken.None);

        await service.SelectProjectAsync(null, CancellationToken.None);

        service.SelectedProjectId.Should().BeNull();
        service.SelectedRepositoryId.Should().BeNull();
        service.Repositories.Should().BeEmpty();
    }

    [Test]
    public async Task SelectProjectAsync_SameProjectId_DoesNothing()
    {
        SetupDefaultStoreMocks();
        SetupLocalStorageMock();

        var service = CreateService();
        await service.InitializeAsync(CancellationToken.None);

        var initialProjectId = service.SelectedProjectId;

        await service.SelectProjectAsync("proj-1", CancellationToken.None);

        service.SelectedProjectId.Should().Be(initialProjectId);
    }

    [Test]
    public async Task SelectRepositoryAsync_UpdatesSelection()
    {
        SetupDefaultStoreMocks();
        SetupLocalStorageMock();

        var service = CreateService();
        await service.InitializeAsync(CancellationToken.None);

        await service.SelectRepositoryAsync("repo-2", CancellationToken.None);

        service.SelectedRepositoryId.Should().Be("repo-2");
        service.SelectedRepository?.Name.Should().Be("Repo 2");
    }

    [Test]
    public async Task SelectRepositoryAsync_RaisesSelectionChangedEvent()
    {
        SetupDefaultStoreMocks();
        SetupLocalStorageMock();

        var service = CreateService();
        await service.InitializeAsync(CancellationToken.None);

        SelectionChangedEventArgs? eventArgs = null;
        service.SelectionChanged += (_, e) => eventArgs = e;

        await service.SelectRepositoryAsync("repo-2", CancellationToken.None);

        eventArgs.Should().NotBeNull();
        eventArgs!.RepositoryId.Should().Be("repo-2");
    }

    [Test]
    public async Task SelectRepositoryAsync_WithNull_ClearsSelection()
    {
        SetupDefaultStoreMocks();
        SetupLocalStorageMock();

        var service = CreateService();
        await service.InitializeAsync(CancellationToken.None);

        await service.SelectRepositoryAsync(null, CancellationToken.None);

        service.SelectedRepositoryId.Should().BeNull();
        service.SelectedRepository.Should().BeNull();
    }

    [Test]
    public async Task SelectRepositoryAsync_SameRepoId_DoesNothing()
    {
        SetupDefaultStoreMocks();
        SetupLocalStorageMock();

        var service = CreateService();
        await service.InitializeAsync(CancellationToken.None);

        var initialRepoId = service.SelectedRepositoryId;

        await service.SelectRepositoryAsync("repo-1", CancellationToken.None);

        service.SelectedRepositoryId.Should().Be(initialRepoId);
    }

    [Test]
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
        SetupLocalStorageMock();

        var service = CreateService();
        await service.InitializeAsync(CancellationToken.None);

        _storeMock.Setup(s => s.ListProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedProjects);

        await service.RefreshAsync(CancellationToken.None);

        service.Projects.Should().HaveCount(2);
    }

    [Test]
    public async Task Subscribe_ReceivesNotifications()
    {
        SetupDefaultStoreMocks();
        SetupLocalStorageMock();

        var service = CreateService();
        SelectionChangedEventArgs? receivedArgs = null;
        var subscription = service.Subscribe(args => receivedArgs = args);

        await service.SelectProjectAsync("proj-2", CancellationToken.None);

        receivedArgs.Should().NotBeNull();
        receivedArgs!.ProjectId.Should().Be("proj-2");
        subscription.Dispose();
    }

    [Test]
    public async Task Subscribe_MultipleSubscribers_AllReceiveNotifications()
    {
        SetupDefaultStoreMocks();
        SetupLocalStorageMock();

        var service = CreateService();
        var callCount1 = 0;
        var callCount2 = 0;

        var sub1 = service.Subscribe(_ => callCount1++);
        var sub2 = service.Subscribe(_ => callCount2++);

        await service.SelectProjectAsync("proj-2", CancellationToken.None);

        callCount1.Should().Be(1);
        callCount2.Should().Be(1);

        sub1.Dispose();
        sub2.Dispose();
    }

    [Test]
    public async Task Subscribe_AfterDispose_StopsReceivingNotifications()
    {
        SetupDefaultStoreMocks();
        SetupLocalStorageMock();

        var service = CreateService();
        var callCount = 0;
        var subscription = service.Subscribe(_ => callCount++);

        await service.SelectProjectAsync("proj-2", CancellationToken.None);
        subscription.Dispose();
        await service.SelectProjectAsync("proj-3", CancellationToken.None);

        callCount.Should().Be(1);
    }

    [Test]
    public async Task InitializeAsync_WithEmptyProjects_NoSelection()
    {
        _storeMock.Setup(s => s.ListProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        SetupLocalStorageMock();

        var service = CreateService();

        await service.InitializeAsync(CancellationToken.None);

        service.SelectedProjectId.Should().BeNull();
        service.SelectedRepositoryId.Should().BeNull();
        service.IsInitialized.Should().BeTrue();
    }

    [Test]
    public async Task InitializeAsync_WithEmptyRepos_SelectsProjectButNoRepo()
    {
        _storeMock.Setup(s => s.ListProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testProjects);
        _storeMock.Setup(s => s.ListRepositoriesAsync("proj-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        SetupLocalStorageMock();

        var service = CreateService();

        await service.InitializeAsync(CancellationToken.None);

        service.SelectedProjectId.Should().Be("proj-1");
        service.SelectedRepositoryId.Should().BeNull();
    }

    [Test]
    public async Task InitializeAsync_CalledTwice_OnlyInitializesOnce()
    {
        var callCount = 0;
        _storeMock.Setup(s => s.ListProjectsAsync(It.IsAny<CancellationToken>()))
            .Callback(() => callCount++)
            .ReturnsAsync(_testProjects);
        _storeMock.Setup(s => s.ListRepositoriesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testRepositories);
        SetupLocalStorageMock();

        var service = CreateService();

        await service.InitializeAsync(CancellationToken.None);
        await service.InitializeAsync(CancellationToken.None);

        callCount.Should().Be(1);
    }

    [Test]
    public void Dispose_CleansUpResources()
    {
        SetupDefaultStoreMocks();
        SetupLocalStorageMock();

        var service = CreateService();
        service.InitializeAsync(CancellationToken.None).Wait();

        var act = () => service.Dispose();

        act.Should().NotThrow();
    }

    [Test]
    public void Dispose_CalledMultipleTimes_NoException()
    {
        var service = CreateService();

        service.Dispose();
        var act = () => service.Dispose();

        act.Should().NotThrow();
    }

    [Test]
    public async Task SelectRepositoryAsync_WithInvalidId_DoesNotThrow()
    {
        SetupDefaultStoreMocks();
        SetupLocalStorageMock();

        var service = CreateService();
        await service.InitializeAsync(CancellationToken.None);

        var act = async () => await service.SelectRepositoryAsync("invalid-repo", CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task InitializeAsync_ConcurrentCalls_OnlyInitializesOnce()
    {
        var callCount = 0;
        var tcs = new TaskCompletionSource();

        _storeMock.Setup(s => s.ListProjectsAsync(It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                callCount++;
                tcs.Task.Wait();
            })
            .ReturnsAsync(_testProjects);
        _storeMock.Setup(s => s.ListRepositoriesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testRepositories);
        SetupLocalStorageMock();

        var service = CreateService();

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => service.InitializeAsync(CancellationToken.None))
            .ToList();

        tcs.SetResult();
        await Task.WhenAll(tasks);

        callCount.Should().Be(1);
    }
}
