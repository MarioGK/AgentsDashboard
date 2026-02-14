using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace AgentsDashboard.PlaywrightTests;

[TestFixture]
public class NavigationE2ETests : PageTest
{
    private const string BaseUrl = "http://localhost:8080";

    [Test]
    public async Task Sidebar_HasAgentsLink()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/");

        var agentsLink = Page.Locator("a[href='/agents']");
        await Expect(agentsLink).ToBeVisibleAsync();
    }

    [Test]
    public async Task Sidebar_HasWorkflowsGroup()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/");

        var workflowsGroup = Page.Locator(".mud-nav-group:has-text('Workflows')").First;
        await Expect(workflowsGroup).ToBeVisibleAsync();
    }

    [Test]
    public async Task Sidebar_WorkflowsGroup_HasStagesLink()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/");

        var workflowsGroup = Page.Locator(".mud-nav-group:has-text('Workflows')").First;
        await workflowsGroup.ClickAsync();
        await Page.WaitForTimeoutAsync(300);

        var stagesLink = Page.Locator("a[href='/workflows']");
        await Expect(stagesLink).ToBeVisibleAsync();

        var stagesText = await stagesLink.TextContentAsync();
        Assert.That(stagesText, Does.Contain("Stages"), "Stages link should have correct text");
    }

    [Test]
    public async Task Sidebar_WorkflowsGroup_HasGraphLink()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/");

        var workflowsGroup = Page.Locator(".mud-nav-group:has-text('Workflows')").First;
        await workflowsGroup.ClickAsync();
        await Page.WaitForTimeoutAsync(300);

        var graphLink = Page.Locator("a[href='/workflows-v2']");
        await Expect(graphLink).ToBeVisibleAsync();

        var graphText = await graphLink.TextContentAsync();
        Assert.That(graphText, Does.Contain("Graph"), "Graph link should have correct text");
    }

    [Test]
    public async Task Sidebar_WorkflowsGroup_HasDeadLettersLink()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/");

        var workflowsGroup = Page.Locator(".mud-nav-group:has-text('Workflows')").First;
        await workflowsGroup.ClickAsync();
        await Page.WaitForTimeoutAsync(300);

        var deadLettersLink = Page.Locator("a[href='/workflow-deadletters']");
        await Expect(deadLettersLink).ToBeVisibleAsync();

        var deadLettersText = await deadLettersLink.TextContentAsync();
        Assert.That(deadLettersText, Does.Contain("Dead Letters"), "Dead Letters link should have correct text");
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
