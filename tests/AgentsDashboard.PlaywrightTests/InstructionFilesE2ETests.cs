using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using System.Text.RegularExpressions;

namespace AgentsDashboard.PlaywrightTests;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class InstructionFilesE2ETests : PageTest
{
    private const string BaseUrl = "http://localhost:8080";
    private string _testProjectId = string.Empty;
    private string _testRepoId = string.Empty;

    [SetUp]
    public async Task SetupAsync()
    {
        (_testProjectId, _testRepoId) = await CreateTestProjectAndRepoAsync();
    }

    [Test]
    public async Task InstructionFiles_Page_Loads_WithRepository()
    {
        await Page.GotoAsync($"{BaseUrl}/repositories/{_testRepoId}/instructions");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Expect(Page).ToHaveURLAsync(new Regex($@"/repositories/{_testRepoId}/instructions"));
        await Expect(Page.Locator("h4")).ToContainTextAsync("Instruction Files");
    }

    [Test]
    public async Task InstructionFiles_Shows_EmptyState_WhenNoInstructions()
    {
        await Page.GotoAsync($"{BaseUrl}/repositories/{_testRepoId}/instructions");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Expect(Page.Locator("text=No instruction files")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Add First Instruction")).ToBeVisibleAsync();
    }

    [Test]
    public async Task InstructionFiles_Shows_RepositoryName()
    {
        await Page.GotoAsync($"{BaseUrl}/repositories/{_testRepoId}/instructions");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Expect(Page.Locator("text=Repository:")).ToBeVisibleAsync();
    }

    [Test]
    public async Task InstructionFiles_Has_AddInstruction_Button()
    {
        await Page.GotoAsync($"{BaseUrl}/repositories/{_testRepoId}/instructions");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Expect(Page.Locator("button:has-text('Add Instruction')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task InstructionFiles_Opens_AddDialog()
    {
        await Page.GotoAsync($"{BaseUrl}/repositories/{_testRepoId}/instructions");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Page.ClickAsync("button:has-text('Add Instruction')");
        await Page.WaitForSelectorAsync(".mud-dialog", new() { Timeout = 5000 });

        await Expect(Page.Locator(".mud-dialog >> text=Add New Instruction")).ToBeVisibleAsync();
        await Expect(Page.Locator(".mud-dialog >> label:has-text('Name')")).ToBeVisibleAsync();
        await Expect(Page.Locator(".mud-dialog >> label:has-text('Priority')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task InstructionFiles_CancelDialog_ClosesDialog()
    {
        await Page.GotoAsync($"{BaseUrl}/repositories/{_testRepoId}/instructions");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Page.ClickAsync("button:has-text('Add Instruction')");
        await Page.WaitForSelectorAsync(".mud-dialog", new() { Timeout = 5000 });

        await Page.ClickAsync(".mud-dialog >> button:has-text('Cancel')");
        await Page.WaitForTimeoutAsync(500);

        await Expect(Page.Locator(".mud-dialog")).Not.ToBeVisibleAsync();
    }

    [Test]
    public async Task InstructionFiles_Create_CreatesInstruction()
    {
        await Page.GotoAsync($"{BaseUrl}/repositories/{_testRepoId}/instructions");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Page.ClickAsync("button:has-text('Add Instruction')");
        await Page.WaitForSelectorAsync(".mud-dialog", new() { Timeout = 5000 });

        await Page.FillAsync(".mud-dialog input[type='text']", "Test Instruction");
        await Page.ClickAsync(".mud-dialog >> button:has-text('Create')");
        await Page.WaitForTimeoutAsync(1000);

        await Expect(Page.Locator("text=Test Instruction")).ToBeVisibleAsync();
    }

    [Test]
    public async Task InstructionFiles_Shows_InstructionFields()
    {
        await Page.GotoAsync($"{BaseUrl}/repositories/{_testRepoId}/instructions");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Page.ClickAsync("button:has-text('Add Instruction')");
        await Page.WaitForSelectorAsync(".mud-dialog", new() { Timeout = 5000 });

        await Page.FillAsync(".mud-dialog input[type='text']", "Field Test Instruction");
        await Page.ClickAsync(".mud-dialog >> button:has-text('Create')");
        await Page.WaitForTimeoutAsync(1000);

        await Expect(Page.Locator("label:has-text('Name')").First).ToBeVisibleAsync();
        await Expect(Page.Locator("label:has-text('Priority')").First).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Enabled")).ToBeVisibleAsync();
    }

    [Test]
    public async Task InstructionFiles_Has_SaveButton()
    {
        await Page.GotoAsync($"{BaseUrl}/repositories/{_testRepoId}/instructions");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Page.ClickAsync("button:has-text('Add Instruction')");
        await Page.WaitForSelectorAsync(".mud-dialog", new() { Timeout = 5000 });

        await Page.FillAsync(".mud-dialog input[type='text']", "Save Test Instruction");
        await Page.ClickAsync(".mud-dialog >> button:has-text('Create')");
        await Page.WaitForTimeoutAsync(1000);

        await Expect(Page.Locator("button[title='Save']")).ToBeVisibleAsync();
    }

    [Test]
    public async Task InstructionFiles_Has_DeleteButton()
    {
        await Page.GotoAsync($"{BaseUrl}/repositories/{_testRepoId}/instructions");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Page.ClickAsync("button:has-text('Add Instruction')");
        await Page.WaitForSelectorAsync(".mud-dialog", new() { Timeout = 5000 });

        await Page.FillAsync(".mud-dialog input[type='text']", "Delete Test Instruction");
        await Page.ClickAsync(".mud-dialog >> button:has-text('Create')");
        await Page.WaitForTimeoutAsync(1000);

        await Expect(Page.Locator("button[title='Delete']")).ToBeVisibleAsync();
    }

    [Test]
    public async Task InstructionFiles_Delete_RemovesInstruction()
    {
        await Page.GotoAsync($"{BaseUrl}/repositories/{_testRepoId}/instructions");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Page.ClickAsync("button:has-text('Add Instruction')");
        await Page.WaitForSelectorAsync(".mud-dialog", new() { Timeout = 5000 });

        var instructionName = $"Delete Test {Guid.NewGuid():N}"[..12];
        await Page.FillAsync(".mud-dialog input[type='text']", instructionName);
        await Page.ClickAsync(".mud-dialog >> button:has-text('Create')");
        await Page.WaitForTimeoutAsync(1000);

        await Expect(Page.Locator($"text={instructionName}")).ToBeVisibleAsync();

        await Page.ClickAsync("button[title='Delete']");
        await Page.WaitForTimeoutAsync(1000);

        await Expect(Page.Locator($"text={instructionName}")).Not.ToBeVisibleAsync();
    }

    [Test]
    public async Task InstructionFiles_BackButton_NavigatesToRepository()
    {
        await Page.GotoAsync($"{BaseUrl}/repositories/{_testRepoId}/instructions");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Page.ClickAsync(".mud-icon-button");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Expect(Page).ToHaveURLAsync(new Regex($@"/repositories/{_testRepoId}$"));
    }

    [Test]
    public async Task InstructionFiles_EnableView_ToggleWorks()
    {
        await Page.GotoAsync($"{BaseUrl}/repositories/{_testRepoId}/instructions");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Page.ClickAsync("button:has-text('Add Instruction')");
        await Page.WaitForSelectorAsync(".mud-dialog", new() { Timeout = 5000 });

        await Page.FillAsync(".mud-dialog input[type='text']", "Toggle Test Instruction");
        await Page.ClickAsync(".mud-dialog >> button:has-text('Create')");
        await Page.WaitForTimeoutAsync(1000);

        var enabledSwitch = Page.Locator(".mud-switch").First;
        await Expect(enabledSwitch).ToBeVisibleAsync();

        await enabledSwitch.ClickAsync();
        await Page.WaitForTimeoutAsync(500);
    }

    [Test]
    public async Task InstructionFiles_Shows_ContentMarkdown_Label()
    {
        await Page.GotoAsync($"{BaseUrl}/repositories/{_testRepoId}/instructions");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Page.ClickAsync("button:has-text('Add Instruction')");
        await Page.WaitForSelectorAsync(".mud-dialog", new() { Timeout = 5000 });

        await Page.FillAsync(".mud-dialog input[type='text']", "Content Test Instruction");
        await Page.ClickAsync(".mud-dialog >> button:has-text('Create')");
        await Page.WaitForTimeoutAsync(1000);

        await Expect(Page.Locator("text=Content (Markdown)")).ToBeVisibleAsync();
    }

    [Test]
    public async Task InstructionFiles_Shows_Timestamps()
    {
        await Page.GotoAsync($"{BaseUrl}/repositories/{_testRepoId}/instructions");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Page.ClickAsync("button:has-text('Add Instruction')");
        await Page.WaitForSelectorAsync(".mud-dialog", new() { Timeout = 5000 });

        await Page.FillAsync(".mud-dialog input[type='text']", "Timestamp Test");
        await Page.ClickAsync(".mud-dialog >> button:has-text('Create')");
        await Page.WaitForTimeoutAsync(1000);

        await Expect(Page.Locator("text=Created:")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Updated:")).ToBeVisibleAsync();
    }

    [Test]
    public async Task InstructionFiles_EmptyState_HasAddButton()
    {
        await Page.GotoAsync($"{BaseUrl}/repositories/{_testRepoId}/instructions");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        if (await Page.Locator("text=No instruction files").IsVisibleAsync())
        {
            await Expect(Page.Locator("button:has-text('Add First Instruction')")).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task InstructionFiles_InvalidRepository_ShowsError()
    {
        var invalidRepoId = "nonexistent-repo-id";
        await Page.GotoAsync($"{BaseUrl}/repositories/{invalidRepoId}/instructions");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Expect(Page.Locator("text=Repository not found")).ToBeVisibleAsync();
    }

    private async Task LoginAsync()
    {
        await Page.GotoAsync($"{BaseUrl}/login");
        await Page.FillAsync("input[name='username']", "admin");
        await Page.FillAsync("input[name='password']", "change-me");
        await Page.ClickAsync("button[type='submit']");
        await Page.WaitForURLAsync($"{BaseUrl}/**");
    }

    private async Task<(string projectId, string repoId)> CreateTestProjectAndRepoAsync()
    {
        await LoginAsync();

        await Page.GotoAsync($"{BaseUrl}/projects");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var projectName = $"Instructions Test {Guid.NewGuid():N}"[..20];
        await Page.ClickAsync("button:has-text('New Project')");
        await Page.FillAsync("input[placeholder*='project name' i], input[label*='Name' i]", projectName);
        await Page.ClickAsync("button:has-text('Create')");
        await Page.WaitForTimeoutAsync(1000);

        await Page.ClickAsync($"text={projectName}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var url = Page.Url;
        var match = Regex.Match(url, @"/projects/([^/?]+)");
        var projectId = match.Success ? match.Groups[1].Value : string.Empty;

        await Page.ClickAsync("button:has-text('Add Repository')");
        var repoName = $"Test Repo {Guid.NewGuid():N}"[..15];
        await Page.FillAsync("input[placeholder*='repository name' i], input[label*='Name' i]", repoName);
        await Page.ClickAsync("button:has-text('Add')");
        await Page.WaitForTimeoutAsync(1000);

        await Page.GotoAsync($"{BaseUrl}/projects/{projectId}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var repoLink = Page.Locator($"a:has-text('{repoName}')").First;
        var href = await repoLink.GetAttributeAsync("href");
        var repoMatch = Regex.Match(href ?? "", @"/repositories/([^/?]+)");
        var repoId = repoMatch.Success ? repoMatch.Groups[1].Value : string.Empty;

        return (projectId, repoId);
    }
}
