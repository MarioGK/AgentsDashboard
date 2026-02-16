using Microsoft.Playwright;
using TUnit.Playwright;

namespace AgentsDashboard.PlaywrightTests;

public class SettingsE2ETests : PageTest
{
    private const string BaseUrl = "http://localhost:5266";

    [Test]
    public async Task SettingsPage_Loads()
    {
        await Page.GotoAsync($"{BaseUrl}/settings");
        await Expect(Page.Locator("text=System Settings")).ToBeVisibleAsync();
    }

    [Test]
    public async Task SettingsPage_HasDockerPolicySection()
    {
        await Page.GotoAsync($"{BaseUrl}/settings");
        await Expect(Page.Locator("text=Docker Policy")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Allowed images")).ToBeVisibleAsync();
    }

    [Test]
    public async Task SettingsPage_HasRetentionSection()
    {
        await Page.GotoAsync($"{BaseUrl}/settings");
        await Expect(Page.Locator("text=Retention")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Log retention")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Run retention")).ToBeVisibleAsync();
    }

    [Test]
    public async Task SettingsPage_HasObservabilitySection()
    {
        await Page.GotoAsync($"{BaseUrl}/settings");
        await Expect(Page.Locator("text=Observability")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=VictoriaMetrics Endpoint")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=VMUI Endpoint")).ToBeVisibleAsync();
    }

    [Test]
    public async Task SettingsPage_HasSaveButton()
    {
        await Page.GotoAsync($"{BaseUrl}/settings");
        await Expect(Page.Locator("button:has-text('Save Settings')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task SettingsPage_DockerPolicyField_Editable()
    {
        await Page.GotoAsync($"{BaseUrl}/settings");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var textareas = Page.Locator("textarea");
        var count = await textareas.CountAsync();
        await Assert.That(count).IsGreaterThan(0);
    }

    [Test]
    public async Task SettingsPage_RetentionFields_Editable()
    {
        await Page.GotoAsync($"{BaseUrl}/settings");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var logRetention = Page.Locator("input[type='number']").First;
        await Expect(logRetention).ToBeVisibleAsync();
    }

    [Test]
    public async Task SettingsPage_SaveButton_TriggersSnackbar()
    {
        await Page.GotoAsync($"{BaseUrl}/settings");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Page.ClickAsync("button:has-text('Save Settings')");
        await Page.WaitForTimeoutAsync(1000);

        await Expect(Page.Locator(".mud-snackbar")).ToBeVisibleAsync();
    }

    [Test]
    public async Task SettingsPage_AllowsAnonymousAccess()
    {
        await Page.GotoAsync($"{BaseUrl}/settings");
        await Expect(Page).ToHaveURLAsync($"{BaseUrl}/settings");
    }
}
