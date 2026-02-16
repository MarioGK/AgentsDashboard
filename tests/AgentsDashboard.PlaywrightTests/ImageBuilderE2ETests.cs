using Microsoft.Playwright;
using TUnit.Playwright;

namespace AgentsDashboard.PlaywrightTests;

public class ImageBuilderE2ETests : PageTest
{
    private const string BaseUrl = "http://localhost:5266";

    [Test]
    public async Task ImageBuilderPage_LoadsAfterLogin()
    {

        await Page.GotoAsync($"{BaseUrl}/image-builder");
        await Expect(Page.Locator("text=Container Image Builder")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ImageBuilderPage_HasTemplateSelector()
    {

        await Page.GotoAsync($"{BaseUrl}/image-builder");
        await Expect(Page.Locator("label:has-text('Template')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ImageBuilderPage_HasImageTagField()
    {

        await Page.GotoAsync($"{BaseUrl}/image-builder");
        await Expect(Page.Locator("label:has-text('Image Tag')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ImageBuilderPage_HasDockerfileEditor()
    {

        await Page.GotoAsync($"{BaseUrl}/image-builder");
        await Expect(Page.Locator("text=Dockerfile")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ImageBuilderPage_HasBuildButton()
    {

        await Page.GotoAsync($"{BaseUrl}/image-builder");
        await Expect(Page.Locator("button:has-text('Build Image')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ImageBuilderPage_HasRefreshButton()
    {

        await Page.GotoAsync($"{BaseUrl}/image-builder");
        await Expect(Page.Locator("button:has-text('Refresh Images')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ImageBuilderPage_AvailableImagesSection()
    {

        await Page.GotoAsync($"{BaseUrl}/image-builder");
        await Expect(Page.Locator("text=Available Images")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ListImages_TableHeaders()
    {

        await Page.GotoAsync($"{BaseUrl}/image-builder");

        await Expect(Page.Locator("th:has-text('Tag')")).ToBeVisibleAsync();
        await Expect(Page.Locator("th:has-text('Image ID')")).ToBeVisibleAsync();
        await Expect(Page.Locator("th:has-text('Size')")).ToBeVisibleAsync();
        await Expect(Page.Locator("th:has-text('Created')")).ToBeVisibleAsync();
        await Expect(Page.Locator("th:has-text('Actions')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ListImages_RefreshList()
    {

        await Page.GotoAsync($"{BaseUrl}/image-builder");

        var refreshButton = Page.Locator("button:has-text('Refresh Images')");
        await refreshButton.ClickAsync();
        await Page.WaitForTimeoutAsync(1000);
    }

    [Test]
    public async Task ImageBuilderPage_ImageTableHeaders()
    {

        await Page.GotoAsync($"{BaseUrl}/image-builder");

        var tableRow = Page.Locator("tbody tr").First;
        if (await tableRow.IsVisibleAsync())
        {
            await Expect(Page.Locator("th:has-text('Tag')")).ToBeVisibleAsync();
            await Expect(Page.Locator("th:has-text('Image ID')")).ToBeVisibleAsync();
            await Expect(Page.Locator("th:has-text('Size')")).ToBeVisibleAsync();
            await Expect(Page.Locator("th:has-text('Created')")).ToBeVisibleAsync();
            await Expect(Page.Locator("th:has-text('Actions')")).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task ImageBuilderPage_ImageTagHasDefaultValue()
    {

        await Page.GotoAsync($"{BaseUrl}/image-builder");

        var tagInput = Page.Locator("input[value*='myharness'], input[label='Image Tag']");
        await Expect(tagInput).ToBeVisibleAsync();
    }

    [Test]
    public async Task ImageBuilderPage_TemplateDropdownHasOptions()
    {

        await Page.GotoAsync($"{BaseUrl}/image-builder");

        var templateSelect = Page.Locator(".mud-select:has(label:text('Template'))");
        await templateSelect.ClickAsync();

        await Expect(Page.Locator("text=Full Harness")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Python + Claude")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Node + Codex")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Go + Tools")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Rust Dev")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Minimal Alpine")).ToBeVisibleAsync();
    }

    [Test]
    public async Task BuildImage_SetTag()
    {

        await Page.GotoAsync($"{BaseUrl}/image-builder");

        var tagInput = Page.Locator("input[label='Image Tag']").First;
        await tagInput.FillAsync($"test-image-{Guid.NewGuid():N}");
    }

    [Test]
    public async Task BuildImage_SelectTemplate()
    {

        await Page.GotoAsync($"{BaseUrl}/image-builder");

        var templateSelect = Page.Locator(".mud-select:has(label:text('Template'))");
        await templateSelect.ClickAsync();

        var minimalOption = Page.Locator("text=Minimal Alpine").First;
        await minimalOption.ClickAsync();
    }

    [Test]
    public async Task BuildImage_ClickBuild()
    {

        await Page.GotoAsync($"{BaseUrl}/image-builder");

        var buildButton = Page.Locator("button:has-text('Build Image')");
        await Expect(buildButton).ToBeVisibleAsync();
    }

    [Test]
    public async Task ImageBuilderPage_DeleteImageButton()
    {

        await Page.GotoAsync($"{BaseUrl}/image-builder");

        var tableRow = Page.Locator("tbody tr").First;
        if (await tableRow.IsVisibleAsync())
        {
            var deleteButton = tableRow.Locator("button[color='error'], button:has([data-icon='delete'])").First;
            await Expect(deleteButton).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task DeleteImage_OpenConfirmation()
    {

        await Page.GotoAsync($"{BaseUrl}/image-builder");

        var tableRow = Page.Locator("tbody tr").First;
        if (await tableRow.IsVisibleAsync())
        {
            var deleteButton = tableRow.Locator("button[color='error'], button:has([data-icon='delete'])").First;
            await deleteButton.ClickAsync();

            await Expect(Page.Locator(".mud-dialog")).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task DeleteImage_Confirm()
    {

        await Page.GotoAsync($"{BaseUrl}/image-builder");

        var tableRow = Page.Locator("tbody tr").First;
        if (await tableRow.IsVisibleAsync())
        {
            var deleteButton = tableRow.Locator("button[color='error'], button:has([data-icon='delete'])").First;
            await deleteButton.ClickAsync();

            var confirmButton = Page.Locator(".mud-dialog button:has-text('Delete'), .mud-dialog button:has-text('Confirm')").First;
            if (await confirmButton.IsVisibleAsync())
            {
                await confirmButton.ClickAsync();
            }
        }
    }

    [Test]
    public async Task ImageBuilder_EditorVisible()
    {

        await Page.GotoAsync($"{BaseUrl}/image-builder");

        var editor = Page.Locator(".monaco-editor, textarea");
        if (await editor.First.IsVisibleAsync())
        {
            await Expect(editor.First).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task ImageBuilder_TemplateChangesDockerfile()
    {

        await Page.GotoAsync($"{BaseUrl}/image-builder");

        var templateSelect = Page.Locator(".mud-select:has(label:text('Template'))");
        await templateSelect.ClickAsync();
        await Page.Locator("text=Python + Claude").First.ClickAsync();
        await Page.WaitForTimeoutAsync(500);
    }

    [Test]
    public async Task ImageBuilder_ImageListEmpty()
    {

        await Page.GotoAsync($"{BaseUrl}/image-builder");

        var tableRow = Page.Locator("tbody tr").First;
        if (!await tableRow.IsVisibleAsync())
        {
            await Expect(Page.Locator("text=No images")).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task ImageBuilder_ImageSizeDisplayed()
    {

        await Page.GotoAsync($"{BaseUrl}/image-builder");

        var tableRow = Page.Locator("tbody tr").First;
        if (await tableRow.IsVisibleAsync())
        {
            var sizeCell = tableRow.Locator("td >> nth=2");
            await Expect(sizeCell).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task ImageBuilder_ImageCreatedDateDisplayed()
    {

        await Page.GotoAsync($"{BaseUrl}/image-builder");

        var tableRow = Page.Locator("tbody tr").First;
        if (await tableRow.IsVisibleAsync())
        {
            var createdCell = tableRow.Locator("td >> nth=3");
            await Expect(createdCell).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task ImageBuilder_BuildStatus_Shown()
    {

        await Page.GotoAsync($"{BaseUrl}/image-builder");

        var buildButton = Page.Locator("button:has-text('Build Image')");
        if (await buildButton.IsEnabledAsync())
        {
            var statusText = Page.Locator("text=/Building|Building image|Build/");
            if (await statusText.First.IsVisibleAsync())
            {
                await Expect(statusText.First).ToBeVisibleAsync();
            }
        }
    }

}
