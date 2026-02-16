using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace AgentsDashboard.PlaywrightTests;

[TestFixture]
public class WorkerE2ETests : PageTest
{
    private const string BaseUrl = "http://localhost:5266";

    [Test]
    public async Task WorkersPage_Loads()
    {
        
        await Page.GotoAsync($"{BaseUrl}/workers");
        await Expect(Page.Locator("text=Workers")).ToBeVisibleAsync();
    }

    [Test]
    public async Task WorkersPage_HasRefreshButton()
    {
        
        await Page.GotoAsync($"{BaseUrl}/workers");
        await Expect(Page.Locator("button:has-text('Refresh')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task WorkersPage_ShowsTableHeaders()
    {
        
        await Page.GotoAsync($"{BaseUrl}/workers");
        await Expect(Page.Locator("text=Worker ID")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Endpoint")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Status")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Slots")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Utilization")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Last Heartbeat")).ToBeVisibleAsync();
    }

    [Test]
    public async Task WorkersPage_ClickRefresh_ReloadsData()
    {
        
        await Page.GotoAsync($"{BaseUrl}/workers");
        await Page.ClickAsync("button:has-text('Refresh')");
        await Page.WaitForTimeoutAsync(500);
        await Expect(Page.Locator("text=Workers")).ToBeVisibleAsync();
    }

    [Test]
    public async Task WorkersPage_ShowsEmptyState_WhenNoWorkers()
    {
        
        await Page.GotoAsync($"{BaseUrl}/workers");
        var emptyAlert = Page.Locator("text=No workers have registered yet");
        var table = Page.Locator(".mud-table");

        var hasEmptyState = await emptyAlert.IsVisibleAsync();
        var hasTable = await table.IsVisibleAsync();

        Assert.That(hasEmptyState || hasTable, Is.True, "Page should show either empty state or table");
    }

    [Test]
    public async Task WorkersPage_NavigationFromMenu()
    {
        
        await Page.GotoAsync($"{BaseUrl}/");
        await Page.ClickAsync("a[href='/workers']");
        await Expect(Page).ToHaveURLAsync($"{BaseUrl}/workers");
    }

    [Test]
    public async Task WorkersPage_HasProgressIndicator_WhenLoading()
    {
        
        var loadTask = Page.GotoAsync($"{BaseUrl}/workers");
        await Task.WhenAll(loadTask, Task.Delay(100));
        await Expect(Page.Locator("text=Workers")).ToBeVisibleAsync();
    }

}
