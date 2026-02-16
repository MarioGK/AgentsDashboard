using Microsoft.Playwright;
using TUnit.Playwright;

namespace AgentsDashboard.PlaywrightTests;

public class TerminalE2ETests : PageTest
{
    private const string BaseUrl = "http://localhost:5266";

    [Test]
    public async Task TerminalPage_Loads()
    {
        await Page.GotoAsync($"{BaseUrl}/terminals");
        await Expect(Page.Locator("text=Terminals")).ToBeVisibleAsync();
    }

    [Test]
    public async Task TerminalPage_ShowsNoTerminalSessions_WhenEmpty()
    {
        await Page.GotoAsync($"{BaseUrl}/terminals");
        await Page.WaitForTimeoutAsync(500);

        var emptyState = Page.Locator("text=No Terminal Sessions");
        var tabs = Page.Locator(".mud-tabs");

        var hasEmptyState = await emptyState.IsVisibleAsync();
        var hasTabs = await tabs.IsVisibleAsync();

        await Assert.That(hasEmptyState || hasTabs).IsTrue();
    }

    [Test]
    public async Task TerminalPage_NewTerminalButton_IsVisible()
    {
        await Page.GotoAsync($"{BaseUrl}/terminals");
        await Page.WaitForTimeoutAsync(500);

        await Expect(Page.Locator("button:has-text('New Terminal')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task TerminalPage_Title_IsCorrect()
    {
        await Page.GotoAsync($"{BaseUrl}/terminals");

        await Expect(Page.Locator("h4:has-text('Terminals'), .mud-typography-h4:has-text('Terminals')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task TerminalPage_Navigation_FromSidebar()
    {
        await Page.GotoAsync($"{BaseUrl}/");
        await Page.ClickAsync("a[href='/terminals']");
        await Expect(Page).ToHaveURLAsync($"{BaseUrl}/terminals");
    }

    [Test]
    public async Task TerminalPage_EmptyState_NoErrors()
    {
        await Page.GotoAsync($"{BaseUrl}/terminals");
        await Page.WaitForTimeoutAsync(500);

        var errorAlert = Page.Locator(".mud-alert-error, .mud-alert-filled-error");
        var errorCount = await errorAlert.CountAsync();

        await Assert.That(errorCount).IsEqualTo(0);
    }

    [Test]
    public async Task TerminalPage_PageTitle_ContainsTerminals()
    {
        await Page.GotoAsync($"{BaseUrl}/terminals");

        var title = await Page.TitleAsync();
        await Assert.That(title).Contains("Terminals");
    }

    [Test]
    public async Task TerminalPage_EmptyState_ShowsHelpText()
    {
        await Page.GotoAsync($"{BaseUrl}/terminals");
        await Page.WaitForTimeoutAsync(500);

        var helpText = Page.Locator("text=Click \"New Terminal\" to connect to a worker.");
        var tabs = Page.Locator(".mud-tabs");

        var hasHelp = await helpText.IsVisibleAsync();
        var hasTabs = await tabs.IsVisibleAsync();

        await Assert.That(hasHelp || hasTabs).IsTrue();
    }
}
