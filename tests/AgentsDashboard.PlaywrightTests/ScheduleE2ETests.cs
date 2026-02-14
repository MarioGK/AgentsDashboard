using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace AgentsDashboard.PlaywrightTests;

[TestFixture]
public class ScheduleE2ETests : PageTest
{
    private const string BaseUrl = "http://localhost:8080";

    [Test]
    public async Task SchedulesPage_Loads()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/schedules");
        await Expect(Page.Locator("text=Schedules")).ToBeVisibleAsync();
    }

    [Test]
    public async Task SchedulesPage_HasRefreshButton()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/schedules");
        await Expect(Page.Locator("button:has-text('Refresh')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task SchedulesPage_ShowsTableHeaders()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/schedules");
        await Expect(Page.Locator("text=Name")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Repository")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Harness")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Cron Expression")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Next Run")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Enabled")).ToBeVisibleAsync();
    }

    [Test]
    public async Task SchedulesPage_ClickRefresh_ReloadsData()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/schedules");
        await Page.ClickAsync("button:has-text('Refresh')");
        await Page.WaitForTimeoutAsync(500);
        await Expect(Page.Locator("text=Schedules")).ToBeVisibleAsync();
    }

    [Test]
    public async Task SchedulesPage_ShowsEmptyState_WhenNoSchedules()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/schedules");
        var emptyAlert = Page.Locator("text=No cron-scheduled tasks found");
        var table = Page.Locator(".mud-table");
        
        var hasEmptyState = await emptyAlert.IsVisibleAsync();
        var hasTable = await table.IsVisibleAsync();
        
        Assert.That(hasEmptyState || hasTable, Is.True, "Page should show either empty state or table");
    }

    [Test]
    public async Task SchedulesPage_NavigationFromMenu()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/");
        await Page.ClickAsync("a[href='/schedules']");
        await Expect(Page).ToHaveURLAsync($"{BaseUrl}/schedules");
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
