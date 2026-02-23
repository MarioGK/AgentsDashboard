using AgentsDashboard.ControlPlane.Components.Workspace;
using AgentsDashboard.ControlPlane.Components.Workspace.Models;
using AgentsDashboard.Workspace.ComponentTests.Infrastructure;
using MudBlazor;

namespace AgentsDashboard.Workspace.ComponentTests;

public sealed class WorkspaceLeftRailTests
{
    [Test]
    public async Task LeftRailActionsInvokeExpectedCallbacksAsync()
    {
        using var context = WorkspaceBunitTestContext.Create();

        var toggled = 0;
        var newTask = 0;
        var selectedRepositoryId = string.Empty;
        var selectedTaskId = string.Empty;
        var deletedTaskId = string.Empty;

        var repositoryGroups = new List<WorkspaceRepositoryGroup>
        {
            new(
                "Healthy",
                [
                    new WorkspaceRepositoryListItem(
                        Id: "repo-1",
                        Name: "Repo One",
                        BranchLabel: "main",
                        HealthLabel: "Healthy",
                        HealthColor: Color.Success,
                        Progress: 100,
                        IsSelected: false)
                ])
        };
        var threads = new List<WorkspaceThreadState>
        {
            new(
                TaskId: "task-1",
                Title: "Task One",
                Harness: "codex",
                LatestStateLabel: "Queued",
                LatestStateColor: Color.Warning,
                IsSelected: false,
                HasUnread: true,
                LastActivityUtc: DateTime.UtcNow,
                LatestRunHint: "Queued")
        };

        var component = context.RenderComponent<WorkspaceLeftRail>(parameters => parameters
            .Add(p => p.IsCollapsed, false)
            .Add(p => p.RepositoryFilter, "all")
            .Add(p => p.TaskFilter, "all")
            .Add(p => p.NewTaskDisabled, false)
            .Add(p => p.RepositoryGroups, repositoryGroups)
            .Add(p => p.Threads, threads)
            .Add(p => p.OnToggleCollapsed, () => toggled++)
            .Add(p => p.OnNewTask, () => newTask++)
            .Add(p => p.OnRepositorySelected, (string repositoryId) => selectedRepositoryId = repositoryId)
            .Add(p => p.OnThreadSelected, (string taskId) => selectedTaskId = taskId)
            .Add(p => p.OnThreadDeleteRequested, (string taskId) => deletedTaskId = taskId));

        component.Find("[data-testid='workspace-thread-rail-toggle']").Click();
        component.Find("[data-testid='workspace-new-task']").Click();
        component.Find("[data-testid='workspace-repository-card-repo-1']").Click();
        component.Find("[data-testid='workspace-task-card-task-1']").Click();
        component.Find("[data-testid='workspace-task-delete-task-1']").Click();

        await Assert.That(toggled).IsEqualTo(1);
        await Assert.That(newTask).IsEqualTo(1);
        await Assert.That(selectedRepositoryId).IsEqualTo("repo-1");
        await Assert.That(selectedTaskId).IsEqualTo("task-1");
        await Assert.That(deletedTaskId).IsEqualTo("task-1");
    }

    [Test]
    public async Task FilterCallbacksReceiveSelectedValuesAsync()
    {
        using var context = WorkspaceBunitTestContext.Create();

        var repositoryFilter = string.Empty;
        var taskFilter = string.Empty;

        var component = context.RenderComponent<WorkspaceLeftRail>(parameters => parameters
            .Add(p => p.IsCollapsed, false)
            .Add(p => p.RepositoryFilter, "all")
            .Add(p => p.TaskFilter, "all")
            .Add(p => p.NewTaskDisabled, true)
            .Add(p => p.RepositoryGroups, Array.Empty<WorkspaceRepositoryGroup>())
            .Add(p => p.Threads, Array.Empty<WorkspaceThreadState>())
            .Add(p => p.OnRepositoryFilterChanged, (string value) => repositoryFilter = value)
            .Add(p => p.OnTaskFilterChanged, (string value) => taskFilter = value));

        var selects = component.FindComponents<MudSelect<string>>();
        await Assert.That(selects.Count).IsEqualTo(2);

        await selects[0].Instance.ValueChanged.InvokeAsync("attention");
        await selects[1].Instance.ValueChanged.InvokeAsync("failed");

        await Assert.That(repositoryFilter).IsEqualTo("attention");
        await Assert.That(taskFilter).IsEqualTo("failed");

        var newTaskButton = component.Find("[data-testid='workspace-new-task']");
        await Assert.That(newTaskButton.HasAttribute("disabled")).IsTrue();
    }
}
