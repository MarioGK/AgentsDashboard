using Microsoft.Playwright;
using TUnit.Playwright;

namespace AgentsDashboard.PlaywrightTests;

public class NavigationE2ETests : PageTest
{
    private const string BaseUrl = "http://localhost:5266";

    [Test]
    public async Task Sidebar_HasAgentsLink()
    {

        await Page.GotoAsync($"{BaseUrl}/");

        var agentsLink = Page.Locator("a[href='/agents']");
        await Expect(agentsLink).ToBeVisibleAsync();
    }

    [Test]
    public async Task Sidebar_HasWorkflowsGroup()
    {

        await Page.GotoAsync($"{BaseUrl}/");

        var workflowsGroup = Page.Locator(".mud-nav-group:has-text('Workflows')").First;
        await Expect(workflowsGroup).ToBeVisibleAsync();
    }

    [Test]
    public async Task Sidebar_WorkflowsGroup_HasStagesLink()
    {

        await Page.GotoAsync($"{BaseUrl}/");

        var workflowsGroup = Page.Locator(".mud-nav-group:has-text('Workflows')").First;
        await workflowsGroup.ClickAsync();
        await Page.WaitForTimeoutAsync(300);

        var stagesLink = Page.Locator("a[href='/workflows']");
        await Expect(stagesLink).ToBeVisibleAsync();

        var stagesText = await stagesLink.TextContentAsync();
        await Assert.That(stagesText!).Contains("Stages");
    }

    [Test]
    public async Task Sidebar_WorkflowsGroup_HasGraphLink()
    {

        await Page.GotoAsync($"{BaseUrl}/");

        var workflowsGroup = Page.Locator(".mud-nav-group:has-text('Workflows')").First;
        await workflowsGroup.ClickAsync();
        await Page.WaitForTimeoutAsync(300);

        var graphLink = Page.Locator("a[href='/workflows-v2']");
        await Expect(graphLink).ToBeVisibleAsync();

        var graphText = await graphLink.TextContentAsync();
        await Assert.That(graphText!).Contains("Graph");
    }

    [Test]
    public async Task Sidebar_WorkflowsGroup_HasDeadLettersLink()
    {

        await Page.GotoAsync($"{BaseUrl}/");

        var workflowsGroup = Page.Locator(".mud-nav-group:has-text('Workflows')").First;
        await workflowsGroup.ClickAsync();
        await Page.WaitForTimeoutAsync(300);

        var deadLettersLink = Page.Locator("a[href='/workflow-deadletters']");
        await Expect(deadLettersLink).ToBeVisibleAsync();

        var deadLettersText = await deadLettersLink.TextContentAsync();
        await Assert.That(deadLettersText!).Contains("Dead Letters");
    }

}
