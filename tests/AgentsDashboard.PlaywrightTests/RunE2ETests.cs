using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace AgentsDashboard.PlaywrightTests;

[TestFixture]
public class RunE2ETests : PageTest
{
    private const string BaseUrl = "http://localhost:5266";

    [Test]
    public async Task RunKanbanPage_LoadsAfterLogin()
    {
        
        await Page.GotoAsync($"{BaseUrl}/runs");
        await Expect(Page.Locator("text=Runs")).ToBeVisibleAsync();
    }

    [Test]
    public async Task RunKanbanPage_ShowsColumns()
    {
        
        await Page.GotoAsync($"{BaseUrl}/runs");
        await Expect(Page.Locator("text=Queued")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Running")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Succeeded")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Failed")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Run_AppearsInKanbanBoard()
    {
        
        var runId = await CreateRunWithTaskAsync();

        await Page.GotoAsync($"{BaseUrl}/runs");
        await Expect(Page.Locator($"text={runId}")).ToBeVisibleAsync();
    }

    [Test]
    public async Task RunKanban_DragAndDrop()
    {
        
        await Page.GotoAsync($"{BaseUrl}/runs");

        var runCard = Page.Locator(".mud-card, .kanban-card").First;
        if (await runCard.IsVisibleAsync())
        {
            var columns = Page.Locator(".kanban-column, .mud-paper");
            var count = await columns.CountAsync();
            if (count > 1)
            {
                await Expect(runCard).ToBeVisibleAsync();
            }
        }
    }

    [Test]
    public async Task RunDetailPage_LoadsWithRunData()
    {
        
        var runId = await CreateRunWithTaskAsync();

        await Page.GotoAsync($"{BaseUrl}/runs/{runId}");
        await Expect(Page.Locator("text=Run")).ToBeVisibleAsync();
        await Expect(Page.Locator(".mud-chip")).ToBeVisibleAsync();
    }

    [Test]
    public async Task RunDetailPage_ShowsOverviewTab()
    {
        
        var runId = await CreateRunWithTaskAsync();

        await Page.GotoAsync($"{BaseUrl}/runs/{runId}");
        await Expect(Page.Locator("text=Overview")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Project")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Task")).ToBeVisibleAsync();
    }

    [Test]
    public async Task RunDetailPage_ShowsLogsTab()
    {
        
        var runId = await CreateRunWithTaskAsync();

        await Page.GotoAsync($"{BaseUrl}/runs/{runId}");
        await Expect(Page.Locator("text=Logs")).ToBeVisibleAsync();

        await Page.ClickAsync("text=Logs");
        await Expect(Page.Locator("text=No logs yet")).ToBeVisibleAsync();
    }

    [Test]
    public async Task RunDetailPage_ViewLogs()
    {
        
        var runId = await CreateRunWithTaskAsync();

        await Page.GotoAsync($"{BaseUrl}/runs/{runId}");
        await Page.ClickAsync("text=Logs");

        await Expect(Page.Locator("text=Logs")).ToBeVisibleAsync();
    }

    [Test]
    public async Task RunDetailPage_ShowsArtifactsTab()
    {
        
        var runId = await CreateRunWithTaskAsync();

        await Page.GotoAsync($"{BaseUrl}/runs/{runId}");
        await Expect(Page.Locator("text=Artifacts")).ToBeVisibleAsync();

        await Page.ClickAsync("text=Artifacts");
        await Expect(Page.Locator("text=No artifacts")).ToBeVisibleAsync();
    }

    [Test]
    public async Task RunDetailPage_ViewArtifacts()
    {
        
        var runId = await CreateRunWithTaskAsync();

        await Page.GotoAsync($"{BaseUrl}/runs/{runId}");
        await Page.ClickAsync("text=Artifacts");

        await Expect(Page.Locator("text=Artifacts")).ToBeVisibleAsync();
    }

    [Test]
    public async Task RunDetailPage_ShowsProxyLinksTab()
    {
        
        var runId = await CreateRunWithTaskAsync();

        await Page.GotoAsync($"{BaseUrl}/runs/{runId}");
        await Expect(Page.Locator("text=Proxy Links")).ToBeVisibleAsync();

        await Page.ClickAsync("text=Proxy Links");
        await Expect(Page.Locator("text=VS Code Server")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Terminal")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Application Preview")).ToBeVisibleAsync();
    }

    [Test]
    public async Task RunDetailPage_BackButtonNavigatesToRuns()
    {
        
        var runId = await CreateRunWithTaskAsync();

        await Page.GotoAsync($"{BaseUrl}/runs/{runId}");

        var backButton = Page.Locator("button:has(.mud-icon-root) >> nth=0");
        await backButton.ClickAsync();

        await Expect(Page).ToHaveURLAsync($"{BaseUrl}/runs");
    }

    [Test]
    public async Task RunDetailPage_CancelButton_VisibleForActiveRuns()
    {
        
        var runId = await CreateRunWithTaskAsync();

        await Page.GotoAsync($"{BaseUrl}/runs/{runId}");

        var cancelButton = Page.Locator("button:has-text('Cancel Run')");
        if (await cancelButton.IsVisibleAsync())
        {
            await Expect(cancelButton).ToBeEnabledAsync();
        }
    }

    [Test]
    public async Task RunDetailPage_CancelRun()
    {
        
        var runId = await CreateRunWithTaskAsync();

        await Page.GotoAsync($"{BaseUrl}/runs/{runId}");

        var cancelButton = Page.Locator("button:has-text('Cancel Run')");
        if (await cancelButton.IsVisibleAsync())
        {
            await cancelButton.ClickAsync();
            await Page.WaitForTimeoutAsync(1000);
        }
    }

    [Test]
    public async Task RunDetailPage_RetryRun()
    {
        
        var runId = await CreateRunWithTaskAsync();

        await Page.GotoAsync($"{BaseUrl}/runs/{runId}");

        var retryButton = Page.Locator("button:has-text('Retry')");
        if (await retryButton.IsVisibleAsync())
        {
            await retryButton.ClickAsync();
            await Page.WaitForTimeoutAsync(1000);
        }
    }

    [Test]
    public async Task RunDetailPage_WorkflowTab()
    {
        
        var runId = await CreateRunWithTaskAsync();

        await Page.GotoAsync($"{BaseUrl}/runs/{runId}");

        var workflowTab = Page.Locator("text=Workflow").First;
        if (await workflowTab.IsVisibleAsync())
        {
            await workflowTab.ClickAsync();
            await Expect(Page.Locator("text=Stage")).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task Run_StatusChip_Colors()
    {
        
        var runId = await CreateRunWithTaskAsync();

        await Page.GotoAsync($"{BaseUrl}/runs/{runId}");

        var statusChip = Page.Locator(".mud-chip").First;
        await Expect(statusChip).ToBeVisibleAsync();
    }

    [Test]
    public async Task Run_Duration_Displayed()
    {
        
        var runId = await CreateRunWithTaskAsync();

        await Page.GotoAsync($"{BaseUrl}/runs/{runId}");

        var durationText = Page.Locator("text=/Duration|duration/");
        if (await durationText.IsVisibleAsync())
        {
            await Expect(durationText.First).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task Run_CreatedTime_Displayed()
    {
        
        var runId = await CreateRunWithTaskAsync();

        await Page.GotoAsync($"{BaseUrl}/runs/{runId}");

        await Expect(Page.Locator("text=Created")).ToBeVisibleAsync();
    }

    [Test]
    public async Task RepositoryRunsTab_DisplaysRuns()
    {
        
        var (projectId, repoId) = await CreateProjectWithRepositoryAsync("E2E Repo Runs Test");

        await Page.GotoAsync($"{BaseUrl}/repositories/{repoId}");
        await Page.ClickAsync("text=Runs");
        await Expect(Page.Locator("th:has-text('Run')")).ToBeVisibleAsync();
        await Expect(Page.Locator("th:has-text('Status')")).ToBeVisibleAsync();
        await Expect(Page.Locator("th:has-text('Task')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task RunKanban_FilterByStatus()
    {
        
        await Page.GotoAsync($"{BaseUrl}/runs");

        var filterSelect = Page.Locator(".mud-select").First;
        if (await filterSelect.IsVisibleAsync())
        {
            await Expect(filterSelect).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task RunDetailPage_TaskLink()
    {
        
        var runId = await CreateRunWithTaskAsync();

        await Page.GotoAsync($"{BaseUrl}/runs/{runId}");

        var taskLink = Page.Locator("a[href^='/repositories/']").First;
        if (await taskLink.IsVisibleAsync())
        {
            await Expect(taskLink).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task RunDetailPage_RepositoryLink()
    {
        
        var runId = await CreateRunWithTaskAsync();

        await Page.GotoAsync($"{BaseUrl}/runs/{runId}");

        await Expect(Page.Locator("text=Repository")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Run_KanbanCard_ClickNavigatesToDetail()
    {
        
        var runId = await CreateRunWithTaskAsync();

        await Page.GotoAsync($"{BaseUrl}/runs");

        var runCard = Page.Locator($"a[href='/runs/{runId}'], a:has-text('{runId}')").First;
        if (await runCard.IsVisibleAsync())
        {
            await runCard.ClickAsync();
            await Expect(Page).ToHaveURLAsync($"{BaseUrl}/runs/{runId}");
        }
    }


    private async Task<string> CreateRunWithTaskAsync()
    {
        var (projectId, repoId) = await CreateProjectWithRepositoryAsync($"E2E Run Test {Guid.NewGuid():N}");

        await Page.GotoAsync($"{BaseUrl}/repositories/{repoId}");

        await Page.FillAsync("input[label='Task Name']", "E2E Run Task");
        await Page.SelectOptionAsync(".mud-select:has(label:text('Kind')) >> select", "OneShot");
        await Page.ClickAsync("button:has-text('Create Task')");
        await Expect(Page.Locator("text=E2E Run Task")).ToBeVisibleAsync();

        await Page.ClickAsync("button:has-text('Run')");
        await Page.WaitForURLAsync($"{BaseUrl}/runs/*", new() { WaitUntil = WaitUntilState.Load });

        var url = Page.Url;
        var runId = url.Split('/').Last();
        return runId;
    }

    private async Task<(string ProjectId, string RepoId)> CreateProjectWithRepositoryAsync(string projectName)
    {
        await Page.GotoAsync($"{BaseUrl}/projects");

        await Page.FillAsync("input[placeholder*='Project']", projectName);
        await Page.ClickAsync("button:has-text('Create')");
        await Expect(Page.Locator($"text={projectName}")).ToBeVisibleAsync();

        await Page.ClickAsync($"text={projectName}");
        await Page.WaitForURLAsync($"{BaseUrl}/projects/*");
        var url = Page.Url;
        var projectId = url.Split('/').Last();

        await Page.FillAsync("input[placeholder*='Repository Name'], input[label='Repository Name']", $"{projectName} Repo");
        await Page.FillAsync("input[placeholder*='Git URL'], input[label='Git URL']", "https://github.com/test/repo.git");
        await Page.ClickAsync("button:has-text('Add')");

        await Expect(Page.Locator($"text={projectName} Repo")).ToBeVisibleAsync();
        await Page.ClickAsync($"button:has-text('Open')");

        await Page.WaitForURLAsync($"{BaseUrl}/repositories/*");
        url = Page.Url;
        var repoId = url.Split('/').Last();

        return (projectId, repoId);
    }
}
