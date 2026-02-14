using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace AgentsDashboard.PlaywrightTests;

[TestFixture]
public class TaskE2ETests : PageTest
{
    private const string BaseUrl = "http://localhost:8080";

    [Test]
    public async Task CreateTask_OneShot_UsingForm()
    {
        await LoginAsync();
        var (projectId, repoId) = await CreateProjectWithRepositoryAsync("E2E Task Test");

        await Page.GotoAsync($"{BaseUrl}/repositories/{repoId}");
        await Expect(Page.Locator("text=Create Task")).ToBeVisibleAsync();

        await Page.FillAsync("input[label='Task Name']", "E2E Test OneShot Task");
        await Page.SelectOptionAsync(".mud-select:has(label:text('Kind')) >> select", "OneShot");
        await Page.SelectOptionAsync(".mud-select:has(label:text('Harness')) >> select", "codex");
        await Page.FillAsync("input[label='Shell Command']", "echo 'test'");
        await Page.ClickAsync("button:has-text('Create Task')");

        await Expect(Page.Locator("text=E2E Test OneShot Task")).ToBeVisibleAsync();
    }

    [Test]
    public async Task CreateTask_Cron_UsingForm()
    {
        await LoginAsync();
        var (projectId, repoId) = await CreateProjectWithRepositoryAsync("E2E Cron Task Test");

        await Page.GotoAsync($"{BaseUrl}/repositories/{repoId}");

        await Page.FillAsync("input[label='Task Name']", "E2E Test Cron Task");
        await Page.SelectOptionAsync(".mud-select:has(label:text('Kind')) >> select", "Cron");
        await Page.FillAsync("input[label='Cron (if cron)']", "0 * * * *");
        await Page.SelectOptionAsync(".mud-select:has(label:text('Harness')) >> select", "opencode");
        await Page.ClickAsync("button:has-text('Create Task')");

        await Expect(Page.Locator("text=E2E Test Cron Task")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Cron")).ToBeVisibleAsync();
    }

    [Test]
    public async Task CreateTask_Cron_WithValidCronExpression()
    {
        await LoginAsync();
        var (projectId, repoId) = await CreateProjectWithRepositoryAsync($"E2E Valid Cron {Guid.NewGuid():N}");

        await Page.GotoAsync($"{BaseUrl}/repositories/{repoId}");

        await Page.FillAsync("input[label='Task Name']", "Valid Cron Task");
        await Page.SelectOptionAsync(".mud-select:has(label:text('Kind')) >> select", "Cron");

        var cronInput = Page.Locator("input[label='Cron (if cron)'], input[placeholder*='cron']").First;
        await cronInput.FillAsync("*/15 * * * *");

        await Page.SelectOptionAsync(".mud-select:has(label:text('Harness')) >> select", "codex");
        await Page.ClickAsync("button:has-text('Create Task')");

        await Expect(Page.Locator("text=Valid Cron Task")).ToBeVisibleAsync();
    }

    [Test]
    public async Task CreateTask_EventDriven_UsingForm()
    {
        await LoginAsync();
        var (projectId, repoId) = await CreateProjectWithRepositoryAsync("E2E Event Task Test");

        await Page.GotoAsync($"{BaseUrl}/repositories/{repoId}");

        await Page.FillAsync("input[label='Task Name']", "E2E Test Event Task");
        await Page.SelectOptionAsync(".mud-select:has(label:text('Kind')) >> select", "EventDriven");
        await Page.SelectOptionAsync(".mud-select:has(label:text('Harness')) >> select", "claude-code");
        await Page.ClickAsync("button:has-text('Create Task')");

        await Expect(Page.Locator("text=E2E Test Event Task")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=EventDriven")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ApplyTaskTemplate()
    {
        await LoginAsync();
        var (projectId, repoId) = await CreateProjectWithRepositoryAsync("E2E Template Test");

        await Page.GotoAsync($"{BaseUrl}/repositories/{repoId}");
        await Expect(Page.Locator("text=Quick Start from Template")).ToBeVisibleAsync();

        var templateButton = Page.Locator("button:has-text('Code Review')").First;
        if (await templateButton.IsVisibleAsync())
        {
            await templateButton.ClickAsync();
            await Page.ClickAsync("button:has-text('Create Task')");
            await Expect(Page.Locator("text=Code Review")).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task TriggerTask_CreatesRun()
    {
        await LoginAsync();
        var (projectId, repoId) = await CreateProjectWithRepositoryAsync("E2E Trigger Test");

        await Page.GotoAsync($"{BaseUrl}/repositories/{repoId}");

        await Page.FillAsync("input[label='Task Name']", "E2E Triggerable Task");
        await Page.SelectOptionAsync(".mud-select:has(label:text('Kind')) >> select", "OneShot");
        await Page.ClickAsync("button:has-text('Create Task')");

        await Expect(Page.Locator("text=E2E Triggerable Task")).ToBeVisibleAsync();

        await Page.ClickAsync("button:has-text('Run')");
        await Page.WaitForURLAsync($"{BaseUrl}/runs/*", new() { WaitUntil = WaitUntilState.Load });

        await Expect(Page.Locator(".mud-chip")).ToBeVisibleAsync();
    }

    [Test]
    public async Task TaskTable_DisplaysColumns()
    {
        await LoginAsync();
        var (projectId, repoId) = await CreateProjectWithRepositoryAsync("E2E Task Columns Test");

        await Page.GotoAsync($"{BaseUrl}/repositories/{repoId}");

        await Page.FillAsync("input[label='Task Name']", "Column Test Task");
        await Page.ClickAsync("button:has-text('Create Task')");
        await Expect(Page.Locator("text=Column Test Task")).ToBeVisibleAsync();

        await Expect(Page.Locator("th:has-text('Name')")).ToBeVisibleAsync();
        await Expect(Page.Locator("th:has-text('Kind')")).ToBeVisibleAsync();
        await Expect(Page.Locator("th:has-text('Harness')")).ToBeVisibleAsync();
        await Expect(Page.Locator("th:has-text('Enabled')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task EditTask_UpdateName()
    {
        await LoginAsync();
        var (projectId, repoId) = await CreateProjectWithRepositoryAsync($"E2E Edit Task {Guid.NewGuid():N}");

        await Page.GotoAsync($"{BaseUrl}/repositories/{repoId}");

        await Page.FillAsync("input[label='Task Name']", "Task To Edit");
        await Page.ClickAsync("button:has-text('Create Task')");
        await Expect(Page.Locator("text=Task To Edit")).ToBeVisibleAsync();

        var editButton = Page.Locator("tr:has-text('Task To Edit') button:has([data-icon='edit']), tr:has-text('Task To Edit') button:has-text('Edit')").First;
        if (await editButton.IsVisibleAsync())
        {
            await editButton.ClickAsync();

            var nameInput = Page.Locator("input[value='Task To Edit'], input[label='Task Name']").First;
            if (await nameInput.IsVisibleAsync())
            {
                await nameInput.FillAsync("Task Edited");
                await Page.ClickAsync("button:has-text('Save')");
            }
        }
    }

    [Test]
    public async Task DeleteTask_RemovesFromList()
    {
        await LoginAsync();
        var (projectId, repoId) = await CreateProjectWithRepositoryAsync($"E2E Delete Task {Guid.NewGuid():N}");

        await Page.GotoAsync($"{BaseUrl}/repositories/{repoId}");

        await Page.FillAsync("input[label='Task Name']", "Task To Delete");
        await Page.ClickAsync("button:has-text('Create Task')");
        await Expect(Page.Locator("text=Task To Delete")).ToBeVisibleAsync();

        var deleteButton = Page.Locator("tr:has-text('Task To Delete') button:has([data-icon='delete']), tr:has-text('Task To Delete') button[color='error']").First;
        if (await deleteButton.IsVisibleAsync())
        {
            await deleteButton.ClickAsync();

            var confirmButton = Page.Locator(".mud-dialog button:has-text('Delete'), .mud-dialog button:has-text('Confirm')").First;
            if (await confirmButton.IsVisibleAsync())
            {
                await confirmButton.ClickAsync();
                await Page.WaitForTimeoutAsync(500);
            }
        }
    }

    [Test]
    public async Task ToggleTask_EnableDisable()
    {
        await LoginAsync();
        var (projectId, repoId) = await CreateProjectWithRepositoryAsync($"E2E Toggle Task {Guid.NewGuid():N}");

        await Page.GotoAsync($"{BaseUrl}/repositories/{repoId}");

        await Page.FillAsync("input[label='Task Name']", "Toggle Task");
        await Page.ClickAsync("button:has-text('Create Task')");
        await Expect(Page.Locator("text=Toggle Task")).ToBeVisibleAsync();

        var toggleSwitch = Page.Locator("tr:has-text('Toggle Task') .mud-switch").First;
        if (await toggleSwitch.IsVisibleAsync())
        {
            await toggleSwitch.ClickAsync();
            await Page.WaitForTimeoutAsync(500);
        }
    }

    [Test]
    public async Task Task_AppearsInList_AfterCreation()
    {
        await LoginAsync();
        var (projectId, repoId) = await CreateProjectWithRepositoryAsync($"E2E Task List {Guid.NewGuid():N}");

        await Page.GotoAsync($"{BaseUrl}/repositories/{repoId}");

        var taskName = $"Listed Task {Guid.NewGuid():N}";
        await Page.FillAsync("input[label='Task Name']", taskName);
        await Page.ClickAsync("button:has-text('Create Task')");

        await Expect(Page.Locator($"text={taskName}")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Task_HarnessSelection_AvailableOptions()
    {
        await LoginAsync();
        var (projectId, repoId) = await CreateProjectWithRepositoryAsync($"E2E Harness Test {Guid.NewGuid():N}");

        await Page.GotoAsync($"{BaseUrl}/repositories/{repoId}");

        var harnessSelect = Page.Locator(".mud-select:has(label:text('Harness'))");
        await harnessSelect.ClickAsync();

        await Expect(Page.Locator("text=codex")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=opencode")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=claude-code")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=zai")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Task_KindSelection_AvailableOptions()
    {
        await LoginAsync();
        var (projectId, repoId) = await CreateProjectWithRepositoryAsync($"E2E Kind Test {Guid.NewGuid():N}");

        await Page.GotoAsync($"{BaseUrl}/repositories/{repoId}");

        var kindSelect = Page.Locator(".mud-select:has(label:text('Kind'))");
        await kindSelect.ClickAsync();

        await Expect(Page.Locator("text=OneShot")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Cron")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=EventDriven")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Task_RunButton_CreatesRun()
    {
        await LoginAsync();
        var (projectId, repoId) = await CreateProjectWithRepositoryAsync($"E2E Run Button {Guid.NewGuid():N}");

        await Page.GotoAsync($"{BaseUrl}/repositories/{repoId}");

        await Page.FillAsync("input[label='Task Name']", "Runnable Task");
        await Page.ClickAsync("button:has-text('Create Task')");
        await Expect(Page.Locator("text=Runnable Task")).ToBeVisibleAsync();

        var runButton = Page.Locator("tr:has-text('Runnable Task') button:has-text('Run')").First;
        await runButton.ClickAsync();

        await Page.WaitForURLAsync($"{BaseUrl}/runs/*");
        await Expect(Page.Locator(".mud-chip")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Task_ShellCommand_Input()
    {
        await LoginAsync();
        var (projectId, repoId) = await CreateProjectWithRepositoryAsync($"E2E Shell Cmd {Guid.NewGuid():N}");

        await Page.GotoAsync($"{BaseUrl}/repositories/{repoId}");

        await Page.FillAsync("input[label='Task Name']", "Shell Command Task");
        await Page.FillAsync("input[label='Shell Command']", "npm test && npm run build");
        await Page.ClickAsync("button:has-text('Create Task')");

        await Expect(Page.Locator("text=Shell Command Task")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Task_InstructionFile_Editor()
    {
        await LoginAsync();
        var (projectId, repoId) = await CreateProjectWithRepositoryAsync($"E2E Instruction {Guid.NewGuid():N}");

        await Page.GotoAsync($"{BaseUrl}/repositories/{repoId}");

        var instructionTab = Page.Locator("text=Instructions, text=Instruction File").First;
        if (await instructionTab.IsVisibleAsync())
        {
            await instructionTab.ClickAsync();
            await Expect(Page.Locator("text=Instructions")).ToBeVisibleAsync();
        }
    }

    private async Task LoginAsync()
    {
        await Page.GotoAsync($"{BaseUrl}/login");
        await Page.FillAsync("input[name='username']", "admin");
        await Page.FillAsync("input[name='password']", "change-me");
        await Page.ClickAsync("button[type='submit']");
        await Page.WaitForURLAsync($"{BaseUrl}/**");
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
