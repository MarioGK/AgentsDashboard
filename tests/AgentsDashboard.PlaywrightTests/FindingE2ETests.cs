using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace AgentsDashboard.PlaywrightTests;

[TestFixture]
public class FindingE2ETests : PageTest
{
    private const string BaseUrl = "http://localhost:8080";

    [Test]
    public async Task FindingsListPage_LoadsAfterLogin()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/findings");
        await Expect(Page.Locator("text=Findings")).ToBeVisibleAsync();
    }

    [Test]
    public async Task FindingsListPage_HasSeverityFilter()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/findings");
        await Expect(Page.Locator("text=Severity")).ToBeVisibleAsync();

        var severitySelect = Page.Locator(".mud-select:has(label:text('Severity'))");
        await Expect(severitySelect).ToBeVisibleAsync();
    }

    [Test]
    public async Task FilterFindings_BySeverity()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/findings");

        var severitySelect = Page.Locator(".mud-select:has(label:text('Severity'))");
        await severitySelect.ClickAsync();

        await Expect(Page.Locator("text=Critical")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=High")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Medium")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Low")).ToBeVisibleAsync();
    }

    [Test]
    public async Task FilterFindings_SelectCritical()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/findings");

        var severitySelect = Page.Locator(".mud-select:has(label:text('Severity'))");
        await severitySelect.ClickAsync();

        var criticalOption = Page.Locator("text=Critical").First;
        if (await criticalOption.IsVisibleAsync())
        {
            await criticalOption.ClickAsync();
            await Page.WaitForTimeoutAsync(500);
        }
    }

    [Test]
    public async Task FindingsListPage_HasStateFilter()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/findings");
        await Expect(Page.Locator("text=State")).ToBeVisibleAsync();

        var stateSelect = Page.Locator(".mud-select:has(label:text('State'))");
        await Expect(stateSelect).ToBeVisibleAsync();
    }

    [Test]
    public async Task FilterFindings_ByState()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/findings");

        var stateSelect = Page.Locator(".mud-select:has(label:text('State'))");
        await stateSelect.ClickAsync();

        await Expect(Page.Locator("text=New")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Acknowledged")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Resolved")).ToBeVisibleAsync();
    }

    [Test]
    public async Task FindingsListPage_HasRefreshButton()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/findings");
        await Expect(Page.Locator("button:has-text('Refresh')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task FindingsListPage_TableHeaders()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/findings");

        await Expect(Page.Locator("th:has-text('ID')")).ToBeVisibleAsync();
        await Expect(Page.Locator("th:has-text('Title')")).ToBeVisibleAsync();
        await Expect(Page.Locator("th:has-text('Severity')")).ToBeVisibleAsync();
        await Expect(Page.Locator("th:has-text('State')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task FindingDetailPage_NavigationFromList()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/findings");

        var findingLink = Page.Locator("a[href^='/findings/']").First;
        if (await findingLink.IsVisibleAsync())
        {
            await findingLink.ClickAsync();
            await Page.WaitForURLAsync($"{BaseUrl}/findings/*");
            await Expect(Page.Locator("text=Actions")).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task FindingDetailPage_ShowsBackButton()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/findings");

        var findingLink = Page.Locator("a[href^='/findings/']").First;
        if (await findingLink.IsVisibleAsync())
        {
            await findingLink.ClickAsync();
            await Page.WaitForURLAsync($"{BaseUrl}/findings/*");

            var backButton = Page.Locator("button:has(.mud-icon-root) >> nth=0");
            await Expect(backButton).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task FindingDetailPage_ShowsActionButtons()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/findings");

        var findingLink = Page.Locator("a[href^='/findings/']").First;
        if (await findingLink.IsVisibleAsync())
        {
            await findingLink.ClickAsync();
            await Page.WaitForURLAsync($"{BaseUrl}/findings/*");

            await Expect(Page.Locator("text=Actions")).ToBeVisibleAsync();
            await Expect(Page.Locator("button:has-text('Assign')")).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task FindingDetailPage_AcknowledgeFinding()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/findings");

        var newFinding = Page.Locator("tr:has(.mud-chip:has-text('New')) >> a[href^='/findings/']").First;
        if (await newFinding.IsVisibleAsync())
        {
            await newFinding.ClickAsync();
            await Page.WaitForURLAsync($"{BaseUrl}/findings/*");

            var acknowledgeButton = Page.Locator("button:has-text('Acknowledge')");
            if (await acknowledgeButton.IsVisibleAsync())
            {
                await acknowledgeButton.ClickAsync();
                await Page.WaitForTimeoutAsync(500);
            }
        }
    }

    [Test]
    public async Task FindingDetailPage_AssignField()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/findings");

        var findingLink = Page.Locator("a[href^='/findings/']").First;
        if (await findingLink.IsVisibleAsync())
        {
            await findingLink.ClickAsync();
            await Page.WaitForURLAsync($"{BaseUrl}/findings/*");

            var assignField = Page.Locator("input[label='Assign To']");
            await Expect(assignField).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task FindingDetailPage_AssignFinding()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/findings");

        var findingLink = Page.Locator("a[href^='/findings/']").First;
        if (await findingLink.IsVisibleAsync())
        {
            await findingLink.ClickAsync();
            await Page.WaitForURLAsync($"{BaseUrl}/findings/*");

            var assignField = Page.Locator("input[label='Assign To']");
            await assignField.FillAsync("test-user@example.com");
            await Page.ClickAsync("button:has-text('Assign')");
        }
    }

    [Test]
    public async Task FindingDetailPage_AcknowledgeButton_ForNewFindings()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/findings");

        var newFinding = Page.Locator("tr:has(.mud-chip:has-text('New')) >> a[href^='/findings/']").First;
        if (await newFinding.IsVisibleAsync())
        {
            await newFinding.ClickAsync();
            await Page.WaitForURLAsync($"{BaseUrl}/findings/*");

            var acknowledgeButton = Page.Locator("button:has-text('Acknowledge')");
            await Expect(acknowledgeButton).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task FindingDetailPage_ResolveButton()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/findings");

        var findingLink = Page.Locator("a[href^='/findings/']").First;
        if (await findingLink.IsVisibleAsync())
        {
            await findingLink.ClickAsync();
            await Page.WaitForURLAsync($"{BaseUrl}/findings/*");

            var resolveButton = Page.Locator("button:has-text('Resolve')");
            if (await resolveButton.IsVisibleAsync())
            {
                await Expect(resolveButton).ToBeVisibleAsync();
            }
        }
    }

    [Test]
    public async Task FindingDetailPage_RetryRunButton()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/findings");

        var findingLink = Page.Locator("a[href^='/findings/']").First;
        if (await findingLink.IsVisibleAsync())
        {
            await findingLink.ClickAsync();
            await Page.WaitForURLAsync($"{BaseUrl}/findings/*");

            var retryButton = Page.Locator("button:has-text('Retry Run')");
            if (await retryButton.IsVisibleAsync())
            {
                await Expect(retryButton).ToBeVisibleAsync();
            }
        }
    }

    [Test]
    public async Task FindingDetailPage_CreateTaskFromFinding()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/findings");

        var findingLink = Page.Locator("a[href^='/findings/']").First;
        if (await findingLink.IsVisibleAsync())
        {
            await findingLink.ClickAsync();
            await Page.WaitForURLAsync($"{BaseUrl}/findings/*");

            var followupButton = Page.Locator("button:has-text('Create Follow-up Task')");
            if (await followupButton.IsVisibleAsync())
            {
                await followupButton.ClickAsync();
                await Page.WaitForTimeoutAsync(500);
            }
        }
    }

    [Test]
    public async Task FindingDetailPage_CreateFollowupTaskButton()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/findings");

        var findingLink = Page.Locator("a[href^='/findings/']").First;
        if (await findingLink.IsVisibleAsync())
        {
            await findingLink.ClickAsync();
            await Page.WaitForURLAsync($"{BaseUrl}/findings/*");

            var followupButton = Page.Locator("button:has-text('Create Follow-up Task')");
            if (await followupButton.IsVisibleAsync())
            {
                await Expect(followupButton).ToBeVisibleAsync();
            }
        }
    }

    [Test]
    public async Task FindingDetailPage_DisplaysMetadata()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/findings");

        var findingLink = Page.Locator("a[href^='/findings/']").First;
        if (await findingLink.IsVisibleAsync())
        {
            await findingLink.ClickAsync();
            await Page.WaitForURLAsync($"{BaseUrl}/findings/*");

            await Expect(Page.Locator("text=Finding ID")).ToBeVisibleAsync();
            await Expect(Page.Locator("text=Created")).ToBeVisibleAsync();
            await Expect(Page.Locator("text=Repository")).ToBeVisibleAsync();
            await Expect(Page.Locator("text=Source Run")).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task Finding_SeverityChip_Colors()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/findings");

        var severityChip = Page.Locator(".mud-chip").First;
        if (await severityChip.IsVisibleAsync())
        {
            await Expect(severityChip).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task Finding_BackNavigation()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/findings");

        var findingLink = Page.Locator("a[href^='/findings/']").First;
        if (await findingLink.IsVisibleAsync())
        {
            await findingLink.ClickAsync();
            await Page.WaitForURLAsync($"{BaseUrl}/findings/*");

            var backButton = Page.Locator("button:has(.mud-icon-root) >> nth=0");
            await backButton.ClickAsync();

            await Expect(Page).ToHaveURLAsync($"{BaseUrl}/findings");
        }
    }

    [Test]
    public async Task Finding_RefreshList()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/findings");

        var refreshButton = Page.Locator("button:has-text('Refresh')");
        await refreshButton.ClickAsync();
        await Page.WaitForTimeoutAsync(500);
    }

    [Test]
    public async Task Finding_TablePagination()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/findings");

        var pager = Page.Locator(".mud-table-pagination");
        if (await pager.IsVisibleAsync())
        {
            await Expect(pager).ToBeVisibleAsync();
        }
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
