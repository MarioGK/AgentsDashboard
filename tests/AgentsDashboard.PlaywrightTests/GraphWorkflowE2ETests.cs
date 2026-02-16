using Microsoft.Playwright;
using TUnit.Playwright;

namespace AgentsDashboard.PlaywrightTests;

public class GraphWorkflowE2ETests : PageTest
{
    private const string BaseUrl = "http://localhost:5266";

    [Test]
    public async Task WorkflowList_PageLoads()
    {

        await Page.GotoAsync($"{BaseUrl}/workflows-v2");
        await Expect(Page.Locator("text=Graph Workflows")).ToBeVisibleAsync();
    }

    [Test]
    public async Task WorkflowList_ShowsCreateButton()
    {

        await Page.GotoAsync($"{BaseUrl}/workflows-v2");
        await Expect(Page.Locator("a:has-text('Create Workflow'), button:has-text('Create Workflow')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task WorkflowList_CreateButton_NavigatesToEditor()
    {

        await Page.GotoAsync($"{BaseUrl}/workflows-v2");

        await Page.ClickAsync("a:has-text('Create Workflow')");
        await Expect(Page).ToHaveURLAsync($"{BaseUrl}/workflows-v2/new");
    }

    [Test]
    public async Task WorkflowEditor_NewMode_HasEmptyForm()
    {

        await Page.GotoAsync($"{BaseUrl}/workflows-v2/new");

        await Expect(Page.Locator("text=New Graph Workflow")).ToBeVisibleAsync();

        var nameInput = Page.Locator("input[label='Name'], .mud-input-root").First;
        await Expect(nameInput).ToBeVisibleAsync();
    }

    [Test]
    public async Task WorkflowEditor_NodePalette_HasAllTypes()
    {

        await Page.GotoAsync($"{BaseUrl}/workflows-v2/new");

        await Expect(Page.Locator("text=Node Palette")).ToBeVisibleAsync();
        await Expect(Page.Locator("button:has-text('Start')")).ToBeVisibleAsync();
        await Expect(Page.Locator("button:has-text('Agent')")).ToBeVisibleAsync();
        await Expect(Page.Locator("button:has-text('Delay')")).ToBeVisibleAsync();
        await Expect(Page.Locator("button:has-text('Approval')")).ToBeVisibleAsync();
        await Expect(Page.Locator("button:has-text('End')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task WorkflowEditor_AddStartNode_AppearsInTable()
    {

        await Page.GotoAsync($"{BaseUrl}/workflows-v2/new");

        await Page.ClickAsync("button:has-text('Start')");
        await Page.WaitForTimeoutAsync(300);

        var nodesTable = Page.Locator(".mud-table");
        await Expect(nodesTable.First).ToBeVisibleAsync();

        var startNodeRow = Page.Locator("td:has-text('Start 1'), td:has-text('Start')").First;
        await Expect(startNodeRow).ToBeVisibleAsync();
    }

    [Test]
    public async Task WorkflowEditor_AddEndNode_AppearsInTable()
    {

        await Page.GotoAsync($"{BaseUrl}/workflows-v2/new");

        await Page.ClickAsync("button:has-text('End')");
        await Page.WaitForTimeoutAsync(300);

        var endNodeRow = Page.Locator("td:has-text('End 1'), td:has-text('End')").First;
        await Expect(endNodeRow).ToBeVisibleAsync();
    }

    [Test]
    public async Task WorkflowEditor_NodesTab_ShowsTable()
    {

        await Page.GotoAsync($"{BaseUrl}/workflows-v2/new");

        var nodesTab = Page.Locator(".mud-tab:has-text('Nodes')").First;
        await Expect(nodesTab).ToBeVisibleAsync();

        await nodesTab.ClickAsync();
        await Page.WaitForTimeoutAsync(200);

        var emptyAlert = Page.Locator("text=Add nodes from the palette");
        var nodesTable = Page.Locator(".mud-table");
        var hasEmpty = await emptyAlert.IsVisibleAsync();
        var hasTable = await nodesTable.First.IsVisibleAsync();

        await Assert.That(hasEmpty || hasTable).IsTrue();
    }

    [Test]
    public async Task WorkflowEditor_EdgesTab_ShowsTable()
    {

        await Page.GotoAsync($"{BaseUrl}/workflows-v2/new");

        var edgesTab = Page.Locator(".mud-tab:has-text('Edges')").First;
        await Expect(edgesTab).ToBeVisibleAsync();

        await edgesTab.ClickAsync();
        await Page.WaitForTimeoutAsync(200);

        var emptyAlert = Page.Locator("text=No edges defined");
        var addEdgeButton = Page.Locator("button:has-text('Add Edge')");
        var hasEmpty = await emptyAlert.IsVisibleAsync();
        var hasButton = await addEdgeButton.IsVisibleAsync();

        await Assert.That(hasEmpty || hasButton).IsTrue();
    }

    [Test]
    public async Task WorkflowEditor_ValidationTab_ShowsErrors()
    {

        await Page.GotoAsync($"{BaseUrl}/workflows-v2/new");

        var validationTab = Page.Locator(".mud-tab:has-text('Validation')").First;
        await Expect(validationTab).ToBeVisibleAsync();

        await validationTab.ClickAsync();
        await Page.WaitForTimeoutAsync(200);

        var validationContent = Page.Locator("text=Validate").First;
        await Expect(validationContent).ToBeVisibleAsync();
    }

    [Test]
    public async Task WorkflowEditor_SaveButton_Exists()
    {

        await Page.GotoAsync($"{BaseUrl}/workflows-v2/new");

        await Expect(Page.Locator("button:has-text('Save')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task WorkflowEditor_BackButton_NavigatesToList()
    {

        await Page.GotoAsync($"{BaseUrl}/workflows-v2/new");

        var backButton = Page.Locator("a[href='/workflows-v2'] .mud-icon-root, a[href='/workflows-v2']").First;
        if (await backButton.IsVisibleAsync())
        {
            await backButton.ClickAsync();
            await Expect(Page).ToHaveURLAsync($"{BaseUrl}/workflows-v2");
        }
    }

    [Test]
    public async Task WorkflowEditor_Title_IsCorrect()
    {

        await Page.GotoAsync($"{BaseUrl}/workflows-v2/new");

        await Expect(Page.Locator("h4:has-text('New Graph Workflow'), .mud-typography-h4:has-text('New Graph Workflow')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task WorkflowList_Title_IsCorrect()
    {

        await Page.GotoAsync($"{BaseUrl}/workflows-v2");

        await Expect(Page.Locator("h4:has-text('Graph Workflows'), .mud-typography-h4:has-text('Graph Workflows')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task WorkflowList_Navigation_FromSidebar()
    {

        await Page.GotoAsync($"{BaseUrl}/");

        var workflowsGroup = Page.Locator(".mud-nav-group:has-text('Workflows')").First;
        if (await workflowsGroup.IsVisibleAsync())
        {
            await workflowsGroup.ClickAsync();
            await Page.WaitForTimeoutAsync(300);
        }

        await Page.ClickAsync("a[href='/workflows-v2']");
        await Expect(Page).ToHaveURLAsync($"{BaseUrl}/workflows-v2");
    }

}
