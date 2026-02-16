using Microsoft.Playwright;
using TUnit.Playwright;

namespace AgentsDashboard.PlaywrightTests;

public class DashboardE2ETests : PageTest
{
    private const string BaseUrl = "http://localhost:5266";

    [Test]
    public async Task Dashboard_Loads()
    {
        await Page.GotoAsync($"{BaseUrl}/");
        await Expect(Page.Locator("text=Overview")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ProjectsPage_Loads()
    {
        await Page.GotoAsync($"{BaseUrl}/projects");
        await Expect(Page.Locator("text=Projects")).ToBeVisibleAsync();
    }

    [Test]
    public async Task CreateProject_Flow()
    {
        await Page.GotoAsync($"{BaseUrl}/projects");

        await Page.FillAsync("input[placeholder*='Project']", "E2E Test Project");
        await Page.ClickAsync("button:has-text('Create')");

        await Expect(Page.Locator("text=E2E Test Project")).ToBeVisibleAsync();
    }

    [Test]
    public async Task RunsPage_LoadsKanbanBoard()
    {
        await Page.GotoAsync($"{BaseUrl}/runs");
        await Expect(Page.Locator("text=Runs")).ToBeVisibleAsync();
    }

    [Test]
    public async Task FindingsPage_Loads()
    {
        await Page.GotoAsync($"{BaseUrl}/findings");
        await Expect(Page.Locator("text=Findings")).ToBeVisibleAsync();
    }

    [Test]
    public async Task FindingsPage_HasSeverityFilter()
    {
        await Page.GotoAsync($"{BaseUrl}/findings");
        await Expect(Page.Locator("text=Findings")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Severity")).ToBeVisibleAsync();
    }

    [Test]
    public async Task SchedulesPage_Loads()
    {
        await Page.GotoAsync($"{BaseUrl}/schedules");
        await Expect(Page.Locator("text=Schedules")).ToBeVisibleAsync();
    }

    [Test]
    public async Task WorkersPage_Loads()
    {
        await Page.GotoAsync($"{BaseUrl}/workers");
        await Expect(Page.Locator("text=Workers")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ProvidersPage_Loads()
    {
        await Page.GotoAsync($"{BaseUrl}/providers");
        await Expect(Page.Locator("text=Provider Settings")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ProvidersPage_HasSystemSettings()
    {
        await Page.GotoAsync($"{BaseUrl}/providers");
        await Expect(Page.Locator("text=System Settings")).ToBeVisibleAsync();
    }

    [Test]
    public async Task WorkflowsPage_Loads()
    {
        await Page.GotoAsync($"{BaseUrl}/workflows");
        await Expect(Page.Locator("text=Workflows")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ImageBuilderPage_Loads()
    {
        await Page.GotoAsync($"{BaseUrl}/image-builder");
        await Expect(Page.Locator("text=Container Image Builder")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ImageBuilderPage_HasDockerfileEditor()
    {
        await Page.GotoAsync($"{BaseUrl}/image-builder");
        await Expect(Page.Locator("text=Dockerfile")).ToBeVisibleAsync();
    }

    [Test]
    public async Task AlertsPage_Loads()
    {
        await Page.GotoAsync($"{BaseUrl}/alerts");
        await Expect(Page.Locator("text=Alerts")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Navigation_AllPagesAccessible()
    {
        var routes = new[] { "/", "/projects", "/runs", "/findings", "/schedules", "/workers", "/workflows", "/image-builder", "/providers", "/alerts" };

        foreach (var route in routes)
        {
            await Page.GotoAsync($"{BaseUrl}{route}");
            await Expect(Page.Locator("body")).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task Access_AllowsAnonymousAccess()
    {
        await Page.GotoAsync($"{BaseUrl}/projects");
        await Expect(Page).ToHaveURLAsync($"{BaseUrl}/projects");
    }
}
