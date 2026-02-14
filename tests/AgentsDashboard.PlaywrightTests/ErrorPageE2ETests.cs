using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace AgentsDashboard.PlaywrightTests;

[TestFixture]
public class ErrorPageE2ETests : PageTest
{
    private const string BaseUrl = "http://localhost:8080";

    [Test]
    public async Task ErrorPage_LoadsCorrectly()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/Error");
        await Expect(Page.Locator("h1.text-danger")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ErrorPage_ShowsErrorMessage()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/Error");
        await Expect(Page.Locator("h1.text-danger")).ToHaveTextAsync("Error.");
        await Expect(Page.Locator("h2.text-danger")).ToHaveTextAsync("An error occurred while processing your request.");
    }

    [Test]
    public async Task ErrorPage_ShowsDevelopmentModeSection()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/Error");
        await Expect(Page.Locator("h3:has-text('Development Mode')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ErrorPage_ShowsDevelopmentEnvironmentWarning()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/Error");
        await Expect(Page.Locator("text=The Development environment shouldn't be enabled for deployed applications.")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ErrorPage_HasDangerStyling()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/Error");
        var errorHeading = Page.Locator("h1.text-danger");
        await Expect(errorHeading).ToBeVisibleAsync();
    }

    [Test]
    public async Task ErrorPage_NavigationToHome_Works()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/Error");
        await Page.ClickAsync("a:has-text('Overview'), .mud-nav-link:has-text('Overview')");
        await Expect(Page).ToHaveURLAsync($"{BaseUrl}/");
    }

    [Test]
    public async Task ErrorPage_NavigationToProjects_Works()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/Error");
        await Page.ClickAsync("a:has-text('Projects'), .mud-nav-link:has-text('Projects')");
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(".*/projects.*"));
    }

    [Test]
    public async Task ErrorPage_HasPageTitle()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/Error");
        await Expect(Page).ToHaveTitleAsync("Error");
    }

    [Test]
    public async Task ErrorPage_UnauthenticatedAccess_RedirectsToLogin()
    {
        await Page.GotoAsync($"{BaseUrl}/Error");
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(".*login.*"));
    }

    [Test]
    public async Task ErrorPage_FromInvalidRoute_NavigatesCorrectly()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/nonexistent-route");
        await Page.GotoAsync($"{BaseUrl}/Error");
        await Expect(Page.Locator("h1.text-danger")).ToBeVisibleAsync();
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
