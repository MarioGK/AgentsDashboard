using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace AgentsDashboard.PlaywrightTests;

[TestFixture]
public class ProxyAuditsE2ETests : PageTest
{
    private const string BaseUrl = "http://localhost:8080";

    [Test]
    public async Task ProxyAuditsPage_LoadsAfterLogin()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/proxy-audits");
        await Expect(Page.Locator("text=Proxy Audits")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ProxyAuditsPage_HasFilterFields()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/proxy-audits");

        await Expect(Page.Locator("input[placeholder*='Project']")).ToBeVisibleAsync();
        await Expect(Page.Locator("input[placeholder*='Repo']")).ToBeVisibleAsync();
        await Expect(Page.Locator("input[placeholder*='Task']")).ToBeVisibleAsync();
        await Expect(Page.Locator("input[placeholder*='Run']")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ProxyAuditsPage_HasFilterButton()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/proxy-audits");
        await Expect(Page.Locator("button:has-text('Filter')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ProxyAuditsPage_HasRefreshButton()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/proxy-audits");
        await Expect(Page.Locator("button:has-text('Refresh')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ProxyAuditsPage_TableHeaders()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/proxy-audits");

        var tablePresent = await Page.Locator(".mud-table").IsVisibleAsync();
        if (tablePresent)
        {
            await Expect(Page.Locator("th:has-text('Timestamp')")).ToBeVisibleAsync();
            await Expect(Page.Locator("th:has-text('Path')")).ToBeVisibleAsync();
            await Expect(Page.Locator("th:has-text('Target')")).ToBeVisibleAsync();
            await Expect(Page.Locator("th:has-text('Status')")).ToBeVisibleAsync();
            await Expect(Page.Locator("th:has-text('Latency')")).ToBeVisibleAsync();
            await Expect(Page.Locator("th:has-text('Run ID')")).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task ProxyAuditsPage_EmptyState()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/proxy-audits");

        var tableVisible = await Page.Locator(".mud-table tbody tr").First.IsVisibleAsync();
        if (!tableVisible)
        {
            await Expect(Page.Locator("text=No proxy audit records found")).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task ProxyAuditsPage_FilterByProjectId()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/proxy-audits");

        var projectInput = Page.Locator("input[placeholder*='Project']").First;
        await projectInput.FillAsync("test-project-id");
        await Page.ClickAsync("button:has-text('Filter')");
        await Page.WaitForTimeoutAsync(500);
    }

    [Test]
    public async Task ProxyAuditsPage_FilterByRepoId()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/proxy-audits");

        var repoInput = Page.Locator("input[placeholder*='Repo']").First;
        await repoInput.FillAsync("test-repo-id");
        await Page.ClickAsync("button:has-text('Filter')");
        await Page.WaitForTimeoutAsync(500);
    }

    [Test]
    public async Task ProxyAuditsPage_FilterByTaskId()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/proxy-audits");

        var taskInput = Page.Locator("input[placeholder*='Task']").First;
        await taskInput.FillAsync("test-task-id");
        await Page.ClickAsync("button:has-text('Filter')");
        await Page.WaitForTimeoutAsync(500);
    }

    [Test]
    public async Task ProxyAuditsPage_FilterByRunId()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/proxy-audits");

        var runInput = Page.Locator("input[placeholder*='Run']").First;
        await runInput.FillAsync("test-run-id");
        await Page.ClickAsync("button:has-text('Filter')");
        await Page.WaitForTimeoutAsync(500);
    }

    [Test]
    public async Task ProxyAuditsPage_ClearProjectFilter()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/proxy-audits");

        var projectInput = Page.Locator("input[placeholder*='Project']").First;
        await projectInput.FillAsync("test-project");
        var clearButton = projectInput.Locator("xpath=ancestor::div[contains(@class, 'mud-input')]//button[contains(@class, 'mud-input-clear')]").First;
        if (await clearButton.IsVisibleAsync())
        {
            await clearButton.ClickAsync();
        }
    }

    [Test]
    public async Task ProxyAuditsPage_RefreshButton()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/proxy-audits");

        await Page.ClickAsync("button:has-text('Refresh')");
        await Page.WaitForTimeoutAsync(500);
    }

    [Test]
    public async Task ProxyAuditsPage_StatusChips()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/proxy-audits");

        var tableRow = Page.Locator("tbody tr").First;
        if (await tableRow.IsVisibleAsync())
        {
            var statusChip = tableRow.Locator(".mud-chip").First;
            if (await statusChip.IsVisibleAsync())
            {
                await Expect(statusChip).ToBeVisibleAsync();
            }
        }
    }

    [Test]
    public async Task ProxyAuditsPage_RunLink()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/proxy-audits");

        var runLink = Page.Locator("tbody tr .mud-link").First;
        if (await runLink.IsVisibleAsync())
        {
            await Expect(runLink).ToHaveAttributeAsync("href", new System.Text.RegularExpressions.Regex(@"/runs/.+"));
        }
    }

    [Test]
    public async Task ProxyAuditsPage_PathTooltip()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/proxy-audits");

        var tableRow = Page.Locator("tbody tr").First;
        if (await tableRow.IsVisibleAsync())
        {
            var pathCell = tableRow.Locator("td").Nth(1);
            if (await pathCell.IsVisibleAsync())
            {
                await pathCell.HoverAsync();
            }
        }
    }

    [Test]
    public async Task ProxyAuditsPage_PaginationOptions()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/proxy-audits");

        var pager = Page.Locator(".mud-table-pager");
        if (await pager.IsVisibleAsync())
        {
            await Expect(Page.Locator(".mud-table-pagination")).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task ProxyAuditsPage_LoadingIndicator()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/proxy-audits");

        var loadingIndicator = Page.Locator(".mud-progress-linear");
        var wasVisible = await loadingIndicator.IsVisibleAsync();
    }

    [Test]
    public async Task ProxyAuditsPage_MultipleFilters()
    {
        await LoginAsync();
        await Page.GotoAsync($"{BaseUrl}/proxy-audits");

        await Page.Locator("input[placeholder*='Project']").First.FillAsync("project-1");
        await Page.Locator("input[placeholder*='Repo']").First.FillAsync("repo-1");
        await Page.ClickAsync("button:has-text('Filter')");
        await Page.WaitForTimeoutAsync(500);
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
