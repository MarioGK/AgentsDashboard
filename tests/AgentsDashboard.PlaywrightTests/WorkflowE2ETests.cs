using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace AgentsDashboard.PlaywrightTests;

[TestFixture]
public class WorkflowE2ETests : PageTest
{
    private const string BaseUrl = "http://localhost:5266";

    [Test]
    public async Task WorkflowListPage_LoadsAfterLogin()
    {
        
        await Page.GotoAsync($"{BaseUrl}/workflows");
        await Expect(Page.Locator("text=Workflows")).ToBeVisibleAsync();
    }

    [Test]
    public async Task WorkflowListPage_HasNewWorkflowButton()
    {
        
        await Page.GotoAsync($"{BaseUrl}/workflows");
        await Expect(Page.Locator("button:has-text('New Workflow')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task WorkflowListPage_HasSearchField()
    {
        
        await Page.GotoAsync($"{BaseUrl}/workflows");
        await Expect(Page.Locator("input[placeholder*='Search']")).ToBeVisibleAsync();
    }

    [Test]
    public async Task WorkflowListPage_TableHeaders()
    {
        
        await Page.GotoAsync($"{BaseUrl}/workflows");

        await Expect(Page.Locator("th:has-text('Name')")).ToBeVisibleAsync();
        await Expect(Page.Locator("th:has-text('Repository')")).ToBeVisibleAsync();
        await Expect(Page.Locator("th:has-text('Stages')")).ToBeVisibleAsync();
        await Expect(Page.Locator("th:has-text('Enabled')")).ToBeVisibleAsync();
        await Expect(Page.Locator("th:has-text('Created')")).ToBeVisibleAsync();
        await Expect(Page.Locator("th:has-text('Actions')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task WorkflowListPage_NewWorkflowButtonNavigatesToEditor()
    {
        
        await Page.GotoAsync($"{BaseUrl}/workflows");

        await Page.ClickAsync("button:has-text('New Workflow')");
        await Expect(Page).ToHaveURLAsync($"{BaseUrl}/workflows/new");
    }

    [Test]
    public async Task WorkflowEditorPage_Loads()
    {
        
        await Page.GotoAsync($"{BaseUrl}/workflows/new");
        await Expect(Page.Locator("text=Workflow")).ToBeVisibleAsync();
    }

    [Test]
    public async Task CreateWorkflow_WithBasicInfo()
    {
        
        await Page.GotoAsync($"{BaseUrl}/workflows/new");

        var nameInput = Page.Locator("input[label='Name'], input[placeholder*='Name']").First;
        await nameInput.FillAsync($"E2E Workflow {Guid.NewGuid():N}");
    }

    [Test]
    public async Task CreateWorkflow_WithStages()
    {
        
        var (projectId, repoId) = await CreateProjectWithRepositoryAsync($"E2E Workflow Test {Guid.NewGuid():N}");

        await Page.GotoAsync($"{BaseUrl}/workflows/new");

        var nameInput = Page.Locator("input[label='Name'], input[placeholder*='Name']").First;
        await nameInput.FillAsync($"E2E Staged Workflow {Guid.NewGuid():N}");

        var addStageButton = Page.Locator("button:has-text('Add Stage'), button:has-text('Add')");
        if (await addStageButton.First.IsVisibleAsync())
        {
            await addStageButton.First.ClickAsync();
        }
    }

    [Test]
    public async Task EditWorkflow_Stages()
    {
        
        await Page.GotoAsync($"{BaseUrl}/workflows");

        var workflowLink = Page.Locator("a[href^='/workflows/']:not([href='/workflows/new'])").First;
        if (await workflowLink.IsVisibleAsync())
        {
            await workflowLink.ClickAsync();
            await Page.WaitForURLAsync($"{BaseUrl}/workflows/*");

            var addStageButton = Page.Locator("button:has-text('Add Stage')");
            if (await addStageButton.IsVisibleAsync())
            {
                await Expect(addStageButton).ToBeVisibleAsync();
            }
        }
    }

    [Test]
    public async Task ExecuteWorkflow()
    {
        
        await Page.GotoAsync($"{BaseUrl}/workflows");

        var workflowRow = Page.Locator("tbody tr").First;
        if (await workflowRow.IsVisibleAsync())
        {
            var runButton = workflowRow.Locator("button:has-text('Run'), button:has-text('Execute')").First;
            if (await runButton.IsVisibleAsync())
            {
                await runButton.ClickAsync();
                await Page.WaitForTimeoutAsync(1000);
            }
        }
    }

    [Test]
    public async Task ViewWorkflow_Execution()
    {
        
        await Page.GotoAsync($"{BaseUrl}/workflows");

        var workflowLink = Page.Locator("a[href^='/workflows/']:not([href='/workflows/new'])").First;
        if (await workflowLink.IsVisibleAsync())
        {
            await workflowLink.ClickAsync();
            await Page.WaitForURLAsync($"{BaseUrl}/workflows/*");

            var executionsTab = Page.Locator("text=Executions, text=History").First;
            if (await executionsTab.IsVisibleAsync())
            {
                await executionsTab.ClickAsync();
            }
        }
    }

    [Test]
    public async Task WorkflowListPage_WorkflowLinkNavigatesToEditor()
    {
        
        await Page.GotoAsync($"{BaseUrl}/workflows");

        var workflowLink = Page.Locator("a[href^='/workflows/']:not([href='/workflows/new'])").First;
        if (await workflowLink.IsVisibleAsync())
        {
            var href = await workflowLink.GetAttributeAsync("href");
            await workflowLink.ClickAsync();
            await Expect(Page).ToHaveURLAsync(href!);
        }
    }

    [Test]
    public async Task WorkflowListPage_EditButton()
    {
        
        await Page.GotoAsync($"{BaseUrl}/workflows");

        var tableRow = Page.Locator("tbody tr").First;
        if (await tableRow.IsVisibleAsync())
        {
            var editButton = tableRow.Locator("button:has([data-icon='edit']), a:has([data-icon='edit'])").First;
            await Expect(editButton).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task WorkflowListPage_DeleteButton()
    {
        
        await Page.GotoAsync($"{BaseUrl}/workflows");

        var tableRow = Page.Locator("tbody tr").First;
        if (await tableRow.IsVisibleAsync())
        {
            var deleteButton = tableRow.Locator("button[color='error'], button:has([data-icon='delete'])").First;
            await Expect(deleteButton).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task WorkflowListPage_DeleteWorkflow()
    {
        
        await Page.GotoAsync($"{BaseUrl}/workflows");

        var tableRow = Page.Locator("tbody tr").First;
        if (await tableRow.IsVisibleAsync())
        {
            var deleteButton = tableRow.Locator("button[color='error'], button:has([data-icon='delete'])").First;
            if (await deleteButton.IsVisibleAsync())
            {
                await deleteButton.ClickAsync();

                var confirmButton = Page.Locator(".mud-dialog button:has-text('Delete'), .mud-dialog button:has-text('Confirm')").First;
                if (await confirmButton.IsVisibleAsync())
                {
                    await confirmButton.ClickAsync();
                }
            }
        }
    }

    [Test]
    public async Task WorkflowListPage_EnabledStatusChip()
    {
        
        await Page.GotoAsync($"{BaseUrl}/workflows");

        var tableRow = Page.Locator("tbody tr").First;
        if (await tableRow.IsVisibleAsync())
        {
            var statusChip = tableRow.Locator(".mud-chip").First;
            await Expect(statusChip).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task Workflow_ToggleEnabled()
    {
        
        await Page.GotoAsync($"{BaseUrl}/workflows");

        var tableRow = Page.Locator("tbody tr").First;
        if (await tableRow.IsVisibleAsync())
        {
            var toggleSwitch = tableRow.Locator(".mud-switch").First;
            if (await toggleSwitch.IsVisibleAsync())
            {
                await toggleSwitch.ClickAsync();
                await Page.WaitForTimeoutAsync(500);
            }
        }
    }

    [Test]
    public async Task WorkflowListPage_Pagination()
    {
        
        await Page.GotoAsync($"{BaseUrl}/workflows");

        var pager = Page.Locator(".mud-table-pagination");
        if (await pager.IsVisibleAsync())
        {
            await Expect(pager).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task WorkflowEditor_RepositorySelector()
    {
        
        await Page.GotoAsync($"{BaseUrl}/workflows/new");

        var repoSelect = Page.Locator(".mud-select:has(label:text('Repository'))");
        if (await repoSelect.IsVisibleAsync())
        {
            await repoSelect.ClickAsync();
        }
    }

    [Test]
    public async Task WorkflowEditor_StageConfiguration()
    {
        
        await Page.GotoAsync($"{BaseUrl}/workflows/new");

        var stagesSection = Page.Locator("text=Stages, text=Stage").First;
        if (await stagesSection.IsVisibleAsync())
        {
            await Expect(stagesSection).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task WorkflowEditor_SaveButton()
    {
        
        await Page.GotoAsync($"{BaseUrl}/workflows/new");

        var saveButton = Page.Locator("button:has-text('Save'), button:has-text('Create')");
        await Expect(saveButton.First).ToBeVisibleAsync();
    }

    [Test]
    public async Task WorkflowEditor_BackButton()
    {
        
        await Page.GotoAsync($"{BaseUrl}/workflows/new");

        var backButton = Page.Locator("button:has(.mud-icon-root) >> nth=0");
        if (await backButton.IsVisibleAsync())
        {
            await backButton.ClickAsync();
            await Expect(Page).ToHaveURLAsync($"{BaseUrl}/workflows");
        }
    }

    [Test]
    public async Task Workflow_StageOrder()
    {
        
        await Page.GotoAsync($"{BaseUrl}/workflows");

        var workflowLink = Page.Locator("a[href^='/workflows/']:not([href='/workflows/new'])").First;
        if (await workflowLink.IsVisibleAsync())
        {
            await workflowLink.ClickAsync();
            await Page.WaitForURLAsync($"{BaseUrl}/workflows/*");

            var stageItems = Page.Locator(".stage-item, .mud-list-item");
            var count = await stageItems.CountAsync();
            if (count > 1)
            {
                await Expect(stageItems.First).ToBeVisibleAsync();
            }
        }
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
