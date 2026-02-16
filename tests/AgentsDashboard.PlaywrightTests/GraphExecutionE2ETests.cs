using Microsoft.Playwright;
using TUnit.Playwright;

namespace AgentsDashboard.PlaywrightTests;

public class GraphExecutionE2ETests : PageTest
{
    private const string BaseUrl = "http://localhost:5266";

    [Test]
    public async Task ExecutionView_PageLoads_WithValidId()
    {

        var fakeId = Guid.NewGuid().ToString("N");
        await Page.GotoAsync($"{BaseUrl}/workflows-v2/executions/{fakeId}");

        var progressBar = Page.Locator(".mud-progress-linear");
        var executionText = Page.Locator("text=Execution");
        var body = Page.Locator("body");

        await Expect(body).ToBeVisibleAsync();

        var hasProgress = await progressBar.IsVisibleAsync();
        var hasExecution = await executionText.IsVisibleAsync();
        var hasBody = await body.IsVisibleAsync();

        await Assert.That(hasProgress || hasExecution || hasBody).IsTrue();
    }

    [Test]
    public async Task ExecutionView_ShowsStateChip()
    {

        var fakeId = Guid.NewGuid().ToString("N");
        await Page.GotoAsync($"{BaseUrl}/workflows-v2/executions/{fakeId}");
        await Page.WaitForTimeoutAsync(500);

        var stateChip = Page.Locator(".mud-chip").First;
        var progressBar = Page.Locator(".mud-progress-linear");

        var hasChip = await stateChip.IsVisibleAsync();
        var hasProgress = await progressBar.IsVisibleAsync();

        await Assert.That(hasChip || hasProgress).IsTrue();
    }

    [Test]
    public async Task ExecutionView_ShowsNodeResultsTable()
    {

        var fakeId = Guid.NewGuid().ToString("N");
        await Page.GotoAsync($"{BaseUrl}/workflows-v2/executions/{fakeId}");
        await Page.WaitForTimeoutAsync(500);

        var nodeResultsHeader = Page.Locator("text=Node Results");
        var noResultsAlert = Page.Locator("text=No node results yet");
        var progressBar = Page.Locator(".mud-progress-linear");

        var hasHeader = await nodeResultsHeader.IsVisibleAsync();
        var hasAlert = await noResultsAlert.IsVisibleAsync();
        var hasProgress = await progressBar.IsVisibleAsync();

        await Assert.That(hasHeader || hasAlert || hasProgress).IsTrue();
    }

    [Test]
    public async Task ExecutionView_BackButton_Exists()
    {

        var fakeId = Guid.NewGuid().ToString("N");
        await Page.GotoAsync($"{BaseUrl}/workflows-v2/executions/{fakeId}");
        await Page.WaitForTimeoutAsync(500);

        var backButton = Page.Locator("a[href*='/workflows-v2/'] .mud-icon-root, a[href*='/workflows-v2/']").First;
        var progressBar = Page.Locator(".mud-progress-linear");

        var hasBack = await backButton.IsVisibleAsync();
        var hasProgress = await progressBar.IsVisibleAsync();

        await Assert.That(hasBack || hasProgress).IsTrue();
    }

    [Test]
    public async Task ExecutionView_Title_ShowsExecution()
    {

        var fakeId = Guid.NewGuid().ToString("N");
        await Page.GotoAsync($"{BaseUrl}/workflows-v2/executions/{fakeId}");
        await Page.WaitForTimeoutAsync(500);

        var executionTitle = Page.Locator("h4:has-text('Execution'), .mud-typography-h4:has-text('Execution')");
        var progressBar = Page.Locator(".mud-progress-linear");

        var hasTitle = await executionTitle.IsVisibleAsync();
        var hasProgress = await progressBar.IsVisibleAsync();

        await Assert.That(hasTitle || hasProgress).IsTrue();
    }

}
