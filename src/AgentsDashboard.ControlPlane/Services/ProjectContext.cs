using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;
using Microsoft.JSInterop;

namespace AgentsDashboard.ControlPlane.Services;

public class ProjectContext
{
    private readonly IOrchestratorStore _store;
    private readonly IJSRuntime _jsRuntime;

    public string? SelectedRepositoryId { get; private set; }
    public List<RepositoryDocument> Repositories { get; private set; } = [];

    public ProjectContext(IOrchestratorStore store, IJSRuntime jsRuntime)
    {
        _store = store;
        _jsRuntime = jsRuntime;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Repositories = await _store.ListRepositoriesAsync(cancellationToken);
        var savedRepoId = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", "selectedRepositoryId");

        if (savedRepoId is not null && Repositories.Any(r => r.Id == savedRepoId))
        {
            SelectedRepositoryId = savedRepoId;
        }
        else if (Repositories.Count > 0)
        {
            SelectedRepositoryId = Repositories[0].Id;
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "selectedRepositoryId", SelectedRepositoryId);
        }
    }

    public async Task SelectRepositoryAsync(string? repositoryId, CancellationToken cancellationToken)
    {
        SelectedRepositoryId = repositoryId;
        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "selectedRepositoryId", repositoryId ?? string.Empty);
    }
}
