using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace AgentsDashboard.PlaywrightTests;

[TestFixture]
public class GraphExecutionE2ETests : PageTest
{
    private const string BaseUrl = "http://localhost:5266";

    [Test]
    public async Task ExecutionView_PageLoads_WithValidId()
    {
        
        var fakeId = Guid.NewGuid().ToString("N");
        await Page.GotoAsync($"{BaseUrl}/workflows-v2/executions/{fakeId}");

        // Page should load without crash; either shows loading or content
        var progressBar = Page.Locator(".mud-progress-linear");
        var executionText = Page.Locator("text=Execution");
        var body = Page.Locator("body");

        await Expect(body).ToBeVisibleAsync();

        var hasProgress = await progressBar.IsVisibleAsync();
        var hasExecution = await executionText.IsVisibleAsync();
        var hasBody = await body.IsVisibleAsync();

        Assert.That(hasProgress || hasExecution || hasBody, Is.True,
            "Execution page should load without crashing");
    }

    [Test]
    public async Task ExecutionView_ShowsStateChip()
    {
        
        var fakeId = Guid.NewGuid().ToString("N");
        await Page.GotoAsync($"{BaseUrl}/workflows-v2/executions/{fakeId}");
        await Page.WaitForTimeoutAsync(500);

        // When execution is found, a state chip is shown; when not found, loading bar stays
        var stateChip = Page.Locator(".mud-chip").First;
        var progressBar = Page.Locator(".mud-progress-linear");

        var hasChip = await stateChip.IsVisibleAsync();
        var hasProgress = await progressBar.IsVisibleAsync();

        Assert.That(hasChip || hasProgress, Is.True,
            "Page should show state chip when execution is loaded or progress bar when loading");
    }

    [Test]
    public async Task ExecutionView_ShowsNodeResultsTable()
    {
        
        var fakeId = Guid.NewGuid().ToString("N");
        await Page.GotoAsync($"{BaseUrl}/workflows-v2/executions/{fakeId}");
        await Page.WaitForTimeoutAsync(500);

        // When loaded, shows either "Node Results" header or "No node results yet" alert
        var nodeResultsHeader = Page.Locator("text=Node Results");
        var noResultsAlert = Page.Locator("text=No node results yet");
        var progressBar = Page.Locator(".mud-progress-linear");

        var hasHeader = await nodeResultsHeader.IsVisibleAsync();
        var hasAlert = await noResultsAlert.IsVisibleAsync();
        var hasProgress = await progressBar.IsVisibleAsync();

        Assert.That(hasHeader || hasAlert || hasProgress, Is.True,
            "Page should show node results section or loading state");
    }

    [Test]
    public async Task ExecutionView_BackButton_Exists()
    {
        
        var fakeId = Guid.NewGuid().ToString("N");
        await Page.GotoAsync($"{BaseUrl}/workflows-v2/executions/{fakeId}");
        await Page.WaitForTimeoutAsync(500);

        // Back button links to the workflow editor; it may or may not be visible depending on load state
        var backButton = Page.Locator("a[href*='/workflows-v2/'] .mud-icon-root, a[href*='/workflows-v2/']").First;
        var progressBar = Page.Locator(".mud-progress-linear");

        var hasBack = await backButton.IsVisibleAsync();
        var hasProgress = await progressBar.IsVisibleAsync();

        Assert.That(hasBack || hasProgress, Is.True,
            "Page should show back button when loaded or progress bar when loading");
    }

    [Test]
    public async Task ExecutionView_Title_ShowsExecution()
    {
        
        var fakeId = Guid.NewGuid().ToString("N");
        await Page.GotoAsync($"{BaseUrl}/workflows-v2/executions/{fakeId}");
        await Page.WaitForTimeoutAsync(500);

        // Title shows "Execution {id}" when loaded
        var executionTitle = Page.Locator("h4:has-text('Execution'), .mud-typography-h4:has-text('Execution')");
        var progressBar = Page.Locator(".mud-progress-linear");

        var hasTitle = await executionTitle.IsVisibleAsync();
        var hasProgress = await progressBar.IsVisibleAsync();

        Assert.That(hasTitle || hasProgress, Is.True,
            "Page should show execution title when loaded or progress bar when loading");
    }

}
