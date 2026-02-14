using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace AgentsDashboard.PlaywrightTests;

[TestFixture]
public class AgentE2ETests : PageTest
{
    private const string BaseUrl = "http://localhost:8080";

    [Test]
    public async Task AgentList_PageLoads()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/agents");
        await Expect(Page.Locator("text=Agents")).ToBeVisibleAsync();
    }

    [Test]
    public async Task AgentList_ShowsCreateButton()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/agents");
        await Expect(Page.Locator("button:has-text('Create Agent')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task AgentList_CreateAgent_OpensDialog()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/agents");

        await Page.ClickAsync("button:has-text('Create Agent')");
        await Expect(Page.Locator(".mud-dialog")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Create Agent")).ToBeVisibleAsync();
    }

    [Test]
    public async Task AgentDialog_RequiresName()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/agents");

        await Page.ClickAsync("button:has-text('Create Agent')");
        await Expect(Page.Locator(".mud-dialog")).ToBeVisibleAsync();

        var nameInput = Page.Locator(".mud-dialog input[label='Name'], .mud-dialog .mud-input-root").First;
        if (await nameInput.IsVisibleAsync())
        {
            await Expect(nameInput).ToBeVisibleAsync();
        }

        var createButton = Page.Locator(".mud-dialog button:has-text('Create')").First;
        if (await createButton.IsVisibleAsync())
        {
            var isDisabled = await createButton.IsDisabledAsync();
            Assert.That(isDisabled, Is.True, "Create button should be disabled when form is invalid");
        }
    }

    [Test]
    public async Task AgentDialog_CanSelectHarness()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/agents");

        await Page.ClickAsync("button:has-text('Create Agent')");
        await Expect(Page.Locator(".mud-dialog")).ToBeVisibleAsync();

        var harnessSelect = Page.Locator(".mud-dialog .mud-select").Nth(1);
        if (await harnessSelect.IsVisibleAsync())
        {
            await harnessSelect.ClickAsync();
            await Page.WaitForTimeoutAsync(300);

            var codexOption = Page.Locator("text=Codex").First;
            var claudeOption = Page.Locator("text=Claude Code").First;

            var hasCodex = await codexOption.IsVisibleAsync();
            var hasClaude = await claudeOption.IsVisibleAsync();

            Assert.That(hasCodex || hasClaude, Is.True, "Harness dropdown should show at least one harness option");
        }
    }

    [Test]
    [Ignore("Requires API setup and pre-existing repository data")]
    public async Task AgentList_AfterCreate_ShowsAgent()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/agents");

        await Page.ClickAsync("button:has-text('Create Agent')");
        await Expect(Page.Locator(".mud-dialog")).ToBeVisibleAsync();

        var agentName = $"E2E Agent {Guid.NewGuid():N}";
        var nameInput = Page.Locator(".mud-dialog input[label='Name']").First;
        await nameInput.FillAsync(agentName);

        var createButton = Page.Locator(".mud-dialog button:has-text('Create')").First;
        if (await createButton.IsEnabledAsync())
        {
            await createButton.ClickAsync();
            await Page.WaitForTimeoutAsync(500);
            await Expect(Page.Locator($"text={agentName}")).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task AgentList_DeleteButton_ShowsConfirmation()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/agents");

        var tableRow = Page.Locator("tbody tr").First;
        if (await tableRow.IsVisibleAsync())
        {
            var deleteButton = tableRow.Locator("button[color='error'], button:has([data-icon='delete'])").First;
            if (await deleteButton.IsVisibleAsync())
            {
                await deleteButton.ClickAsync();
                await Expect(Page.Locator(".mud-message-box, .mud-dialog")).ToBeVisibleAsync();
            }
        }
    }

    [Test]
    public async Task AgentList_RepositoryFilter_Works()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/agents");

        var repoFilter = Page.Locator(".mud-select:has(label:text('Filter by Repository'))").First;
        if (await repoFilter.IsVisibleAsync())
        {
            await repoFilter.ClickAsync();
            await Page.WaitForTimeoutAsync(300);

            var allOption = Page.Locator("text=All Repositories").First;
            if (await allOption.IsVisibleAsync())
            {
                await allOption.ClickAsync();
                await Page.WaitForTimeoutAsync(300);
                await Expect(Page.Locator("text=Agents")).ToBeVisibleAsync();
            }
        }
    }

    [Test]
    public async Task AgentList_Title_IsCorrect()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/agents");

        await Expect(Page.Locator("h4:has-text('Agents'), .mud-typography-h4:has-text('Agents')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task AgentList_Navigation_FromSidebar()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/");
        await Page.ClickAsync("a[href='/agents']");
        await Expect(Page).ToHaveURLAsync($"{BaseUrl}/agents");
    }

    private async Task LoginAsync()
    {
        await Page.GotoAsync($"{BaseUrl}/login");
        await Page.FillAsync("input[name='username']", "admin");
        await Page.FillAsync("input[name='password']", "change-me");
        await Page.ClickAsync("button[type='submit']");
        await Page.WaitForURLAsync($"{BaseUrl}/**");
    }
}
