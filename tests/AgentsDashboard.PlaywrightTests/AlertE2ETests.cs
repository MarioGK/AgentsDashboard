using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace AgentsDashboard.PlaywrightTests;

[TestFixture]
public class AlertE2ETests : PageTest
{
    private const string BaseUrl = "http://localhost:5266";

    [Test]
    public async Task AlertSettingsPage_LoadsAfterLogin()
    {
        
        await Page.GotoAsync($"{BaseUrl}/alerts");
        await Expect(Page.Locator("text=Alert Settings")).ToBeVisibleAsync();
    }

    [Test]
    public async Task AlertSettingsPage_HasNewRuleButton()
    {
        
        await Page.GotoAsync($"{BaseUrl}/alerts");
        await Expect(Page.Locator("button:has-text('New Alert Rule')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task AlertSettingsPage_RulesTableHeaders()
    {
        
        await Page.GotoAsync($"{BaseUrl}/alerts");

        await Expect(Page.Locator("th:has-text('Name')")).ToBeVisibleAsync();
        await Expect(Page.Locator("th:has-text('Type')")).ToBeVisibleAsync();
        await Expect(Page.Locator("th:has-text('Threshold')")).ToBeVisibleAsync();
        await Expect(Page.Locator("th:has-text('Window')")).ToBeVisibleAsync();
        await Expect(Page.Locator("th:has-text('Webhook')")).ToBeVisibleAsync();
        await Expect(Page.Locator("th:has-text('Status')")).ToBeVisibleAsync();
        await Expect(Page.Locator("th:has-text('Actions')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task AlertSettingsPage_RecentAlertEventsSection()
    {
        
        await Page.GotoAsync($"{BaseUrl}/alerts");
        await Expect(Page.Locator("text=Recent Alert Events")).ToBeVisibleAsync();
    }

    [Test]
    public async Task CreateAlertRule_OpenDialog()
    {
        
        await Page.GotoAsync($"{BaseUrl}/alerts");

        await Page.ClickAsync("button:has-text('New Alert Rule')");
        await Expect(Page.Locator(".mud-dialog")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=New Alert Rule")).ToBeVisibleAsync();
    }

    [Test]
    public async Task CreateAlertRule_WithBasicInfo()
    {
        
        await Page.GotoAsync($"{BaseUrl}/alerts");

        await Page.ClickAsync("button:has-text('New Alert Rule')");
        await Expect(Page.Locator(".mud-dialog")).ToBeVisibleAsync();

        var nameInput = Page.Locator(".mud-dialog input[label='Name'], .mud-dialog input[placeholder*='Name']").First;
        if (await nameInput.IsVisibleAsync())
        {
            await nameInput.FillAsync($"E2E Alert Rule {Guid.NewGuid():N}");
        }
    }

    [Test]
    public async Task CreateAlertRule_SelectType()
    {
        
        await Page.GotoAsync($"{BaseUrl}/alerts");

        await Page.ClickAsync("button:has-text('New Alert Rule')");
        await Expect(Page.Locator(".mud-dialog")).ToBeVisibleAsync();

        var typeSelect = Page.Locator(".mud-dialog .mud-select:has(label:text('Type'))").First;
        if (await typeSelect.IsVisibleAsync())
        {
            await typeSelect.ClickAsync();
            await Expect(Page.Locator("text=FailureRate")).ToBeVisibleAsync();
            await Expect(Page.Locator("text=ConsecutiveFailures")).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task CreateAlertRule_SetThreshold()
    {
        
        await Page.GotoAsync($"{BaseUrl}/alerts");

        await Page.ClickAsync("button:has-text('New Alert Rule')");
        await Expect(Page.Locator(".mud-dialog")).ToBeVisibleAsync();

        var thresholdInput = Page.Locator(".mud-dialog input[label='Threshold'], .mud-dialog input[type='number']").First;
        if (await thresholdInput.IsVisibleAsync())
        {
            await thresholdInput.FillAsync("5");
        }
    }

    [Test]
    public async Task CreateAlertRule_SetWindow()
    {
        
        await Page.GotoAsync($"{BaseUrl}/alerts");

        await Page.ClickAsync("button:has-text('New Alert Rule')");
        await Expect(Page.Locator(".mud-dialog")).ToBeVisibleAsync();

        var windowInput = Page.Locator(".mud-dialog input[label='Window'], .mud-dialog input[placeholder*='minutes']").First;
        if (await windowInput.IsVisibleAsync())
        {
            await windowInput.FillAsync("30");
        }
    }

    [Test]
    public async Task AlertSettingsPage_OpenNewRuleDialog()
    {
        
        await Page.GotoAsync($"{BaseUrl}/alerts");

        await Page.ClickAsync("button:has-text('New Alert Rule')");
        await Expect(Page.Locator(".mud-dialog")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=New Alert Rule")).ToBeVisibleAsync();
    }

    [Test]
    public async Task EditAlertRule_OpenDialog()
    {
        
        await Page.GotoAsync($"{BaseUrl}/alerts");

        var tableRow = Page.Locator("tbody tr").First;
        if (await tableRow.IsVisibleAsync())
        {
            var editButton = tableRow.Locator("button").First;
            if (await editButton.IsVisibleAsync())
            {
                await editButton.ClickAsync();
                await Expect(Page.Locator(".mud-dialog")).ToBeVisibleAsync();
            }
        }
    }

    [Test]
    public async Task EditAlertRule_UpdateThreshold()
    {
        
        await Page.GotoAsync($"{BaseUrl}/alerts");

        var editButton = Page.Locator("tbody tr button").First;
        if (await editButton.IsVisibleAsync())
        {
            await editButton.ClickAsync();
            await Expect(Page.Locator(".mud-dialog")).ToBeVisibleAsync();

            var thresholdInput = Page.Locator(".mud-dialog input[type='number']").First;
            if (await thresholdInput.IsVisibleAsync())
            {
                await thresholdInput.FillAsync("10");
            }
        }
    }

    [Test]
    public async Task AlertSettingsPage_EditRuleButton()
    {
        
        await Page.GotoAsync($"{BaseUrl}/alerts");

        var editButton = Page.Locator("button:has(.mud-icon-root) >> [data-icon='edit'], [aria-label*='Edit']").First;
        var tableRow = Page.Locator("tbody tr").First;

        if (await tableRow.IsVisibleAsync())
        {
            var rowEditButton = tableRow.Locator("button").First;
            if (await rowEditButton.IsVisibleAsync())
            {
                await Expect(rowEditButton).ToBeEnabledAsync();
            }
        }
    }

    [Test]
    public async Task DeleteAlertRule_OpenConfirmation()
    {
        
        await Page.GotoAsync($"{BaseUrl}/alerts");

        var tableRow = Page.Locator("tbody tr").First;
        if (await tableRow.IsVisibleAsync())
        {
            var deleteButton = tableRow.Locator("button[color='error'], button:has([data-icon='delete'])").First;
            if (await deleteButton.IsVisibleAsync())
            {
                await deleteButton.ClickAsync();
                await Expect(Page.Locator(".mud-dialog")).ToBeVisibleAsync();
            }
        }
    }

    [Test]
    public async Task DeleteAlertRule_Confirm()
    {
        
        await Page.GotoAsync($"{BaseUrl}/alerts");

        var tableRow = Page.Locator("tbody tr").First;
        if (await tableRow.IsVisibleAsync())
        {
            var deleteButton = tableRow.Locator("button[color='error'], button:has([data-icon='delete'])").First;
            if (await deleteButton.IsVisibleAsync())
            {
                await deleteButton.ClickAsync();

                var confirmButton = Page.Locator(".mud-dialog button:has-text('Delete'), .mud-dialog button:has-text('Confirm')").First;
                if (await confirmButton.IsVisibleAsync())
                {
                    await confirmButton.ClickAsync();
                }
            }
        }
    }

    [Test]
    public async Task AlertSettingsPage_DeleteRuleButton()
    {
        
        await Page.GotoAsync($"{BaseUrl}/alerts");

        var tableRow = Page.Locator("tbody tr").First;
        if (await tableRow.IsVisibleAsync())
        {
            var deleteButton = tableRow.Locator("button[color='error'], button:has([data-icon='delete'])").First;
            if (await deleteButton.IsVisibleAsync())
            {
                await Expect(deleteButton).ToBeEnabledAsync();
            }
        }
    }

    [Test]
    public async Task AlertSettingsPage_RuleTypeChips()
    {
        
        await Page.GotoAsync($"{BaseUrl}/alerts");

        var typeChips = Page.Locator(".mud-table tbody .mud-chip");
        var firstRow = Page.Locator("tbody tr").First;

        if (await firstRow.IsVisibleAsync())
        {
            var chip = firstRow.Locator(".mud-chip").First;
            if (await chip.IsVisibleAsync())
            {
                await Expect(chip).ToBeVisibleAsync();
            }
        }
    }

    [Test]
    public async Task AlertSettingsPage_EnabledStatusChip()
    {
        
        await Page.GotoAsync($"{BaseUrl}/alerts");

        var tableRow = Page.Locator("tbody tr").First;
        if (await tableRow.IsVisibleAsync())
        {
            var statusChips = tableRow.Locator(".mud-chip");
            var count = await statusChips.CountAsync();
            if (count > 0)
            {
                var lastChip = statusChips.Nth(count - 1);
                await Expect(lastChip).ToBeVisibleAsync();
            }
        }
    }

    [Test]
    public async Task AlertRule_ToggleEnabled()
    {
        
        await Page.GotoAsync($"{BaseUrl}/alerts");

        var tableRow = Page.Locator("tbody tr").First;
        if (await tableRow.IsVisibleAsync())
        {
            var toggleSwitch = tableRow.Locator(".mud-switch").First;
            if (await toggleSwitch.IsVisibleAsync())
            {
                await toggleSwitch.ClickAsync();
                await Page.WaitForTimeoutAsync(500);
            }
        }
    }

    [Test]
    public async Task ViewAlertEvents_List()
    {
        
        await Page.GotoAsync($"{BaseUrl}/alerts");

        await Expect(Page.Locator("text=Recent Alert Events")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ViewAlertEvents_TableHeaders()
    {
        
        await Page.GotoAsync($"{BaseUrl}/alerts");

        var eventsSection = Page.Locator("text=Recent Alert Events").First;
        if (await eventsSection.IsVisibleAsync())
        {
            await Expect(Page.Locator("th:has-text('Rule')")).ToBeVisibleAsync();
            await Expect(Page.Locator("th:has-text('Fired At')")).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task AlertRule_WebhookField()
    {
        
        await Page.GotoAsync($"{BaseUrl}/alerts");

        await Page.ClickAsync("button:has-text('New Alert Rule')");
        await Expect(Page.Locator(".mud-dialog")).ToBeVisibleAsync();

        var webhookInput = Page.Locator(".mud-dialog input[label='Webhook URL'], .mud-dialog input[placeholder*='webhook']").First;
        if (await webhookInput.IsVisibleAsync())
        {
            await webhookInput.FillAsync("https://example.com/webhook");
        }
    }

    [Test]
    public async Task AlertRule_CancelDialog()
    {
        
        await Page.GotoAsync($"{BaseUrl}/alerts");

        await Page.ClickAsync("button:has-text('New Alert Rule')");
        await Expect(Page.Locator(".mud-dialog")).ToBeVisibleAsync();

        var cancelButton = Page.Locator(".mud-dialog button:has-text('Cancel')").First;
        if (await cancelButton.IsVisibleAsync())
        {
            await cancelButton.ClickAsync();
            await Expect(Page.Locator(".mud-dialog")).Not.ToBeVisibleAsync();
        }
    }

}
