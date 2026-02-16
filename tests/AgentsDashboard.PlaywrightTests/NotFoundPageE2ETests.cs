using Microsoft.Playwright;
using TUnit.Playwright;

namespace AgentsDashboard.PlaywrightTests;

public class NotFoundPageE2ETests : PageTest
{
    private const string BaseUrl = "http://localhost:5266";

    [Test]
    public async Task NotFoundPage_LoadsCorrectly()
    {
        await Page.GotoAsync($"{BaseUrl}/not-found");
        await Expect(Page.Locator("h3:has-text('Not Found')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task NotFoundPage_ShowsNotFoundMessage()
    {
        await Page.GotoAsync($"{BaseUrl}/not-found");
        await Expect(Page.Locator("h3")).ToHaveTextAsync("Not Found");
    }

    [Test]
    public async Task NotFoundPage_ShowsApologyMessage()
    {
        await Page.GotoAsync($"{BaseUrl}/not-found");
        await Expect(Page.Locator("text=Sorry, the content you are looking for does not exist.")).ToBeVisibleAsync();
    }

    [Test]
    public async Task NotFoundPage_HasMainLayout()
    {
        await Page.GotoAsync($"{BaseUrl}/not-found");
        await Expect(Page.Locator("text=Agents Dashboard")).ToBeVisibleAsync();
    }

    [Test]
    public async Task NotFoundPage_NavigationDrawer_IsVisible()
    {
        await Page.GotoAsync($"{BaseUrl}/not-found");
        await Expect(Page.Locator(".mud-drawer")).ToBeVisibleAsync();
    }

    [Test]
    public async Task NotFoundPage_NavigationToHome_Works()
    {
        await Page.GotoAsync($"{BaseUrl}/not-found");
        await Page.ClickAsync(".mud-nav-link:has-text('Overview')");
        await Expect(Page).ToHaveURLAsync($"{BaseUrl}/");
    }

    [Test]
    public async Task NotFoundPage_NavigationToProjects_Works()
    {
        await Page.GotoAsync($"{BaseUrl}/not-found");
        await Page.ClickAsync(".mud-nav-link:has-text('Projects')");
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(".*/projects.*"));
    }

    [Test]
    public async Task NotFoundPage_NavigationToRuns_Works()
    {
        await Page.GotoAsync($"{BaseUrl}/not-found");
        await Page.ClickAsync(".mud-nav-link:has-text('Runs')");
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(".*/runs.*"));
    }

    [Test]
    public async Task NotFoundPage_NavigationToFindings_Works()
    {
        await Page.GotoAsync($"{BaseUrl}/not-found");
        await Page.ClickAsync(".mud-nav-link:has-text('Findings')");
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(".*/findings.*"));
    }

    [Test]
    public async Task NotFoundPage_AllNavigationLinks_Work()
    {
        var routes = new[] { "/", "/projects", "/runs", "/findings", "/schedules", "/workers" };

        foreach (var route in routes)
        {
            await Page.GotoAsync($"{BaseUrl}/not-found");
            var linkText = route switch
            {
                "/" => "Overview",
                "/projects" => "Projects",
                "/runs" => "Runs",
                "/findings" => "Findings",
                "/schedules" => "Schedules",
                "/workers" => "Workers",
                _ => "Overview"
            };
            await Page.ClickAsync($".mud-nav-link:has-text('{linkText}')");
            await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex($".*{route}.*"));
        }
    }

    [Test]
    public async Task NotFoundPage_DirectAccess_NoAuthenticationRequired()
    {
        await Page.GotoAsync($"{BaseUrl}/not-found");
        await Expect(Page).ToHaveURLAsync($"{BaseUrl}/not-found");
    }

    [Test]
    public async Task NotFoundPage_DisplaysCorrectly()
    {
        await Page.GotoAsync($"{BaseUrl}/not-found");
        await Expect(Page.Locator("h3:has-text('Not Found')")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Agents Dashboard")).ToBeVisibleAsync();
    }
}
