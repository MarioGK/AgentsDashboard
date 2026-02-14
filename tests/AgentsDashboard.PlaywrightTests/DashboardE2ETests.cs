using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace AgentsDashboard.PlaywrightTests;

[TestFixture]
public class DashboardE2ETests : PageTest
{
    private const string BaseUrl = "http://localhost:8080";

    [Test]
    public async Task LoginPage_Loads()
    {
        await Page.GotoAsync($"{BaseUrl}/login");
        await Expect(Page.Locator("text=Sign In").Or(Page.Locator("input[name='username']"))).ToBeVisibleAsync();
    }

    [Test]
    public async Task Login_WithValidCredentials_RedirectsToDashboard()
    {
        await Page.GotoAsync($"{BaseUrl}/login");

        await Page.FillAsync("input[name='username']", "admin");
        await Page.FillAsync("input[name='password']", "change-me");
        await Page.ClickAsync("button[type='submit']");

        await Page.WaitForURLAsync($"{BaseUrl}/**");
        await Expect(Page.Locator("text=Agents Dashboard")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Login_WithInvalidCredentials_ShowsError()
    {
        await Page.GotoAsync($"{BaseUrl}/login");

        await Page.FillAsync("input[name='username']", "invalid");
        await Page.FillAsync("input[name='password']", "wrong");
        await Page.ClickAsync("button[type='submit']");

        await Page.WaitForTimeoutAsync(1000);
        await Expect(Page).ToHaveURLAsync($"{BaseUrl}/login**");
    }

    [Test]
    public async Task Dashboard_LoadsAfterLogin()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/");
        await Expect(Page.Locator("text=Overview")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ProjectsPage_LoadsAfterLogin()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/projects");
        await Expect(Page.Locator("text=Projects")).ToBeVisibleAsync();
    }

    [Test]
    public async Task CreateProject_Flow()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/projects");

        await Page.FillAsync("input[placeholder*='Project']", "E2E Test Project");
        await Page.ClickAsync("button:has-text('Create')");

        await Expect(Page.Locator("text=E2E Test Project")).ToBeVisibleAsync();
    }

    [Test]
    public async Task RunsPage_LoadsKanbanBoard()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/runs");
        await Expect(Page.Locator("text=Runs")).ToBeVisibleAsync();
    }

    [Test]
    public async Task FindingsPage_Loads()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/findings");
        await Expect(Page.Locator("text=Findings")).ToBeVisibleAsync();
    }

    [Test]
    public async Task FindingsPage_HasSeverityFilter()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/findings");
        await Expect(Page.Locator("text=Findings")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Severity")).ToBeVisibleAsync();
    }

    [Test]
    public async Task SchedulesPage_Loads()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/schedules");
        await Expect(Page.Locator("text=Schedules")).ToBeVisibleAsync();
    }

    [Test]
    public async Task WorkersPage_Loads()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/workers");
        await Expect(Page.Locator("text=Workers")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ProvidersPage_Loads()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/providers");
        await Expect(Page.Locator("text=Provider Settings")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ProvidersPage_HasSystemSettings()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/providers");
        await Expect(Page.Locator("text=System Settings")).ToBeVisibleAsync();
    }

    [Test]
    public async Task WorkflowsPage_Loads()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/workflows");
        await Expect(Page.Locator("text=Workflows")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ImageBuilderPage_Loads()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/image-builder");
        await Expect(Page.Locator("text=Container Image Builder")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ImageBuilderPage_HasDockerfileEditor()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/image-builder");
        await Expect(Page.Locator("text=Dockerfile")).ToBeVisibleAsync();
    }

    [Test]
    public async Task AlertsPage_Loads()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/alerts");
        await Expect(Page.Locator("text=Alerts")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Navigation_AllPagesAccessible()
    {
        await LoginAsync();
        var routes = new[] { "/", "/projects", "/runs", "/findings", "/schedules", "/workers", "/workflows", "/image-builder", "/providers", "/alerts" };

        foreach (var route in routes)
        {
            await Page.GotoAsync($"{BaseUrl}{route}");
            await Expect(Page.Locator("body")).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task UnauthenticatedAccess_RedirectsToLogin()
    {
        await Page.GotoAsync($"{BaseUrl}/projects");
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(".*login.*"));
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
