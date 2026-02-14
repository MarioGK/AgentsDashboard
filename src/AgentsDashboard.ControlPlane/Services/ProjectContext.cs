using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;
using Microsoft.JSInterop;

namespace AgentsDashboard.ControlPlane.Services;

public class ProjectContext
{
    private readonly IOrchestratorStore _store;
    private readonly IJSRuntime _jsRuntime;
    private bool _initialized;

    public string? SelectedProjectId { get; private set; }
    public string? SelectedRepositoryId { get; private set; }
    public List<ProjectDocument> Projects { get; private set; } = [];
    public List<RepositoryDocument> Repositories { get; private set; } = [];

    public ProjectContext(IOrchestratorStore store, IJSRuntime jsRuntime)
    {
        _store = store;
        _jsRuntime = jsRuntime;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;

        Projects = await _store.ListProjectsAsync(cancellationToken);

        var savedProjectId = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", "selectedProjectId");
        var savedRepoId = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", "selectedRepositoryId");

        if (savedProjectId is not null && Projects.Any(p => p.Id == savedProjectId))
        {
            SelectedProjectId = savedProjectId;
            Repositories = await _store.ListRepositoriesAsync(savedProjectId, cancellationToken);

            if (savedRepoId is not null && Repositories.Any(r => r.Id == savedRepoId))
            {
                SelectedRepositoryId = savedRepoId;
            }
        }
        else if (Projects.Count > 0)
        {
            await SelectProjectAsync(Projects[0].Id, cancellationToken);
        }

        _initialized = true;
    }

    public async Task SelectProjectAsync(string? projectId, CancellationToken cancellationToken)
    {
        SelectedProjectId = projectId;
        SelectedRepositoryId = null;
        Repositories = [];

        if (projectId is not null)
        {
            Repositories = await _store.ListRepositoriesAsync(projectId, cancellationToken);
            if (Repositories.Count > 0)
            {
                SelectedRepositoryId = Repositories[0].Id;
                await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "selectedRepositoryId", SelectedRepositoryId);
            }
        }

        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "selectedProjectId", projectId ?? string.Empty);
    }

    public async Task SelectRepositoryAsync(string? repositoryId, CancellationToken cancellationToken)
    {
        SelectedRepositoryId = repositoryId;
        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "selectedRepositoryId", repositoryId ?? string.Empty);
    }
}
