using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace AgentsDashboard.PlaywrightTests;

[TestFixture]
public class DeadLetterE2ETests : PageTest
{
    private const string BaseUrl = "http://localhost:8080";

    [Test]
    public async Task DeadLetterList_PageLoads()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/workflow-deadletters");
        await Expect(Page.Locator("text=Dead Letters")).ToBeVisibleAsync();
    }

    [Test]
    public async Task DeadLetterList_Title_IsCorrect()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/workflow-deadletters");

        await Expect(Page.Locator("h4:has-text('Dead Letters'), .mud-typography-h4:has-text('Dead Letters')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task DeadLetterList_ShowsTable()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/workflow-deadletters");
        await Page.WaitForTimeoutAsync(500);

        // Page shows either a table with dead letters or a success alert for no dead letters
        var table = Page.Locator(".mud-table");
        var emptyState = Page.Locator("text=No unreplayed dead letters found");
        var progressBar = Page.Locator(".mud-progress-linear");

        var hasTable = await table.IsVisibleAsync();
        var hasEmpty = await emptyState.IsVisibleAsync();
        var hasProgress = await progressBar.IsVisibleAsync();

        Assert.That(hasTable || hasEmpty || hasProgress, Is.True,
            "Dead letter page should show table, empty state, or loading indicator");
    }

    [Test]
    public async Task DeadLetterList_EmptyState_NoErrors()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/workflow-deadletters");
        await Page.WaitForTimeoutAsync(500);

        // Verify no error alerts are shown on page load
        var errorAlert = Page.Locator(".mud-alert-error, .mud-alert-filled-error");
        var errorCount = await errorAlert.CountAsync();

        Assert.That(errorCount, Is.EqualTo(0), "Dead letter page should not show error alerts on initial load");
    }

    [Test]
    public async Task DeadLetterList_Navigation_FromSidebar()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/");

        var workflowsGroup = Page.Locator(".mud-nav-group:has-text('Workflows')").First;
        if (await workflowsGroup.IsVisibleAsync())
        {
            await workflowsGroup.ClickAsync();
            await Page.WaitForTimeoutAsync(300);
        }

        await Page.ClickAsync("a[href='/workflow-deadletters']");
        await Expect(Page).ToHaveURLAsync($"{BaseUrl}/workflow-deadletters");
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
