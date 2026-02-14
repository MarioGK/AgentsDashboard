using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace AgentsDashboard.PlaywrightTests;

[TestFixture]
public class SettingsE2ETests : PageTest
{
    private const string BaseUrl = "http://localhost:8080";

    [Test]
    public async Task SettingsPage_LoadsAfterLogin()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/settings");
        await Expect(Page.Locator("text=System Settings")).ToBeVisibleAsync();
    }

    [Test]
    public async Task SettingsPage_HasDockerPolicySection()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/settings");
        await Expect(Page.Locator("text=Docker Policy")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Allowed images")).ToBeVisibleAsync();
    }

    [Test]
    public async Task SettingsPage_HasRetentionSection()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/settings");
        await Expect(Page.Locator("text=Retention")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Log retention")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Run retention")).ToBeVisibleAsync();
    }

    [Test]
    public async Task SettingsPage_HasObservabilitySection()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/settings");
        await Expect(Page.Locator("text=Observability")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=VictoriaMetrics Endpoint")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=VMUI Endpoint")).ToBeVisibleAsync();
    }

    [Test]
    public async Task SettingsPage_HasSaveButton()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/settings");
        await Expect(Page.Locator("button:has-text('Save Settings')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task SettingsPage_DockerPolicyField_Editable()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/settings");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var textareas = Page.Locator("textarea");
        var count = await textareas.CountAsync();
        Assert.That(count, Is.GreaterThan(0), "Should have at least one textarea for Docker images");
    }

    [Test]
    public async Task SettingsPage_RetentionFields_Editable()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/settings");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var logRetention = Page.Locator("input[type='number']").First;
        await Expect(logRetention).ToBeVisibleAsync();
    }

    [Test]
    public async Task SettingsPage_SaveButton_TriggersSnackbar()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/settings");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Page.ClickAsync("button:has-text('Save Settings')");
        await Page.WaitForTimeoutAsync(1000);

        await Expect(Page.Locator(".mud-snackbar")).ToBeVisibleAsync();
    }

    [Test]
    public async Task SettingsPage_UnauthenticatedAccess_RedirectsToLogin()
    {
        await Page.GotoAsync($"{BaseUrl}/settings");
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
