using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace AgentsDashboard.PlaywrightTests;

[TestFixture]
public class ProviderSettingsE2ETests : PageTest
{
    private const string BaseUrl = "http://localhost:8080";

    [Test]
    public async Task ProvidersPage_Loads()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/providers");
        await Expect(Page.Locator("text=Provider Settings")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ProvidersPage_HasRepositorySelection()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/providers");
        await Expect(Page.Locator("text=Repository Selection")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ProvidersPage_HasSystemSettings()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/providers");
        await Expect(Page.Locator("text=System Settings")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ProvidersPage_HasDockerAllowedImagesField()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/providers");
        await Expect(Page.Locator("text=Docker Allowed Images")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ProvidersPage_HasRetentionFields()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/providers");
        await Expect(Page.Locator("text=Log Retention")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Run Retention")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ProvidersPage_HasVictoriaMetricsFields()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/providers");
        await Expect(Page.Locator("text=VictoriaMetrics Endpoint")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=VMUI Endpoint")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ProvidersPage_HasSaveSystemSettingsButton()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/providers");
        await Expect(Page.Locator("button:has-text('Save System Settings')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ProvidersPage_NavigationFromMenu()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/");
        await Page.ClickAsync("a[href='/providers']");
        await Expect(Page).ToHaveURLAsync($"{BaseUrl}/providers");
    }

    [Test]
    public async Task ProvidersPage_ShowsEmptyState_WhenNoRepositories()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/providers");
        var emptyAlert = Page.Locator("text=No repositories found");
        var repoSelect = Page.Locator(".mud-select");
        
        var hasEmptyState = await emptyAlert.IsVisibleAsync();
        var hasSelect = await repoSelect.IsVisibleAsync();
        
        Assert.That(hasEmptyState || hasSelect, Is.True, "Page should show either empty state or repository selector");
    }

    [Test]
    public async Task ProvidersPage_HasGlobalConfigurationHeader()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/providers");
        await Expect(Page.Locator("text=Global Configuration")).ToBeVisibleAsync();
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
