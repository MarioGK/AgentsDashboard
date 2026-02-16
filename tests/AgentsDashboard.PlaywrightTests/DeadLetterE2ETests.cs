using Microsoft.Playwright;
using TUnit.Playwright;

namespace AgentsDashboard.PlaywrightTests;

public class DeadLetterE2ETests : PageTest
{
    private const string BaseUrl = "http://localhost:5266";

    [Test]
    public async Task DeadLetterList_PageLoads()
    {

        await Page.GotoAsync($"{BaseUrl}/workflow-deadletters");
        await Expect(Page.Locator("text=Dead Letters")).ToBeVisibleAsync();
    }

    [Test]
    public async Task DeadLetterList_Title_IsCorrect()
    {

        await Page.GotoAsync($"{BaseUrl}/workflow-deadletters");

        await Expect(Page.Locator("h4:has-text('Dead Letters'), .mud-typography-h4:has-text('Dead Letters')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task DeadLetterList_ShowsTable()
    {

        await Page.GotoAsync($"{BaseUrl}/workflow-deadletters");
        await Page.WaitForTimeoutAsync(500);

        var table = Page.Locator(".mud-table");
        var emptyState = Page.Locator("text=No unreplayed dead letters found");
        var progressBar = Page.Locator(".mud-progress-linear");

        var hasTable = await table.IsVisibleAsync();
        var hasEmpty = await emptyState.IsVisibleAsync();
        var hasProgress = await progressBar.IsVisibleAsync();

        await Assert.That(hasTable || hasEmpty || hasProgress).IsTrue();
    }

    [Test]
    public async Task DeadLetterList_EmptyState_NoErrors()
    {

        await Page.GotoAsync($"{BaseUrl}/workflow-deadletters");
        await Page.WaitForTimeoutAsync(500);

        var errorAlert = Page.Locator(".mud-alert-error, .mud-alert-filled-error");
        var errorCount = await errorAlert.CountAsync();

        await Assert.That(errorCount).IsEqualTo(0);
    }

    [Test]
    public async Task DeadLetterList_Navigation_FromSidebar()
    {

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

}
