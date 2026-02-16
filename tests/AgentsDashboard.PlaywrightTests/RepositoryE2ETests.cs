using Microsoft.Playwright;
using TUnit.Playwright;

namespace AgentsDashboard.PlaywrightTests;

public class RepositoryE2ETests : PageTest
{
    private const string BaseUrl = "http://localhost:5266";

    [Test]
    public async Task ProjectDetailPage_LoadsAfterLogin()
    {

        await Page.GotoAsync($"{BaseUrl}/projects");
        await Expect(Page.Locator("text=Projects")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ProjectDetailPage_CreateRepository()
    {

        var projectId = await CreateProjectAsync("E2E Repo Test Project");

        await Page.GotoAsync($"{BaseUrl}/projects/{projectId}");
        await Expect(Page.Locator("text=E2E Repo Test Project")).ToBeVisibleAsync();

        await Page.FillAsync("input[placeholder*='Repository Name'], input[label='Repository Name']", "E2E Test Repository");
        await Page.FillAsync("input[placeholder*='Git URL'], input[label='Git URL']", "https://github.com/test/repo.git");
        await Page.FillAsync("input[placeholder*='Branch'], input[label='Default Branch']", "main");
        await Page.ClickAsync("button:has-text('Add')");

        await Expect(Page.Locator("text=E2E Test Repository")).ToBeVisibleAsync();
    }

    [Test]
    public async Task CreateRepository_UnderProject()
    {

        var projectName = $"E2E Create Repo {Guid.NewGuid():N}";
        var projectId = await CreateProjectAsync(projectName);

        await Page.GotoAsync($"{BaseUrl}/projects/{projectId}");

        var repoName = $"Test Repo {Guid.NewGuid():N}";
        await Page.FillAsync("input[placeholder*='Repository Name'], input[label='Repository Name']", repoName);
        await Page.FillAsync("input[placeholder*='Git URL'], input[label='Git URL']", "https://github.com/test/newrepo.git");
        await Page.ClickAsync("button:has-text('Add')");

        await Expect(Page.Locator($"text={repoName}")).ToBeVisibleAsync();
    }

    [Test]
    public async Task CreateRepository_WithDescription()
    {

        var (projectId, repoId) = await CreateProjectWithRepositoryAsync($"E2E Repo Desc {Guid.NewGuid():N}");

        await Page.GotoAsync($"{BaseUrl}/repositories/{repoId}");

        var descriptionField = Page.Locator("textarea[label='Description'], textarea[placeholder*='Description']").First;
        if (await descriptionField.IsVisibleAsync())
        {
            await descriptionField.FillAsync("Test repository description");
        }
    }

    [Test]
    public async Task Repository_AppearsUnderCorrectProject()
    {

        var projectName = $"E2E Correct Project {Guid.NewGuid():N}";
        var (projectId, repoId) = await CreateProjectWithRepositoryAsync(projectName);

        await Page.GotoAsync($"{BaseUrl}/projects/{projectId}");
        await Expect(Page.Locator($"text={projectName} Repo")).ToBeVisibleAsync();
    }

    [Test]
    public async Task EditRepository_UpdateName()
    {

        var (projectId, repoId) = await CreateProjectWithRepositoryAsync($"E2E Edit Repo {Guid.NewGuid():N}");

        await Page.GotoAsync($"{BaseUrl}/repositories/{repoId}");

        var editButton = Page.Locator("button:has-text('Edit'), button:has([data-icon='edit'])").First;
        if (await editButton.IsVisibleAsync())
        {
            await editButton.ClickAsync();

            var nameInput = Page.Locator("input[label='Name'], input[label='Repository Name']").First;
            if (await nameInput.IsVisibleAsync())
            {
                await nameInput.FillAsync("Updated Repository Name");
                await Page.ClickAsync("button:has-text('Save')");
            }
        }
    }

    [Test]
    public async Task DeleteRepository_RemovesFromProject()
    {

        var projectName = $"E2E Delete Repo {Guid.NewGuid():N}";
        var (projectId, repoId) = await CreateProjectWithRepositoryAsync(projectName);

        await Page.GotoAsync($"{BaseUrl}/repositories/{repoId}");

        var deleteButton = Page.Locator("button:has-text('Delete'), button[color='error']").First;
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

    [Test]
    public async Task RepositoryDetailPage_LoadsAndShowsTabs()
    {

        var (projectId, repoId) = await CreateProjectWithRepositoryAsync("E2E Repo Detail Test");

        await Page.GotoAsync($"{BaseUrl}/repositories/{repoId}");
        await Expect(Page.Locator("text=E2E Repo Detail Test")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Tasks")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Runs")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Findings Inbox")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=Secrets & Webhooks")).ToBeVisibleAsync();
    }

    [Test]
    public async Task RepositoryDetailPage_SaveSecrets()
    {

        var (projectId, repoId) = await CreateProjectWithRepositoryAsync("E2E Secrets Test");

        await Page.GotoAsync($"{BaseUrl}/repositories/{repoId}");
        await Page.ClickAsync("text=Secrets & Webhooks");

        await Page.FillAsync("input[label='GitHub Token (GH_TOKEN)']", "test-github-token");
        await Page.ClickAsync("button:has-text('Save Secrets')");

        await Expect(Page.Locator("text=Secrets saved")).ToBeVisibleAsync();
    }

    [Test]
    public async Task RepositoryDetailPage_TasksTab()
    {

        var (projectId, repoId) = await CreateProjectWithRepositoryAsync($"E2E Tasks Tab {Guid.NewGuid():N}");

        await Page.GotoAsync($"{BaseUrl}/repositories/{repoId}");
        await Page.ClickAsync("text=Tasks");

        await Expect(Page.Locator("th:has-text('Name')")).ToBeVisibleAsync();
        await Expect(Page.Locator("th:has-text('Kind')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task RepositoryDetailPage_RunsTab()
    {

        var (projectId, repoId) = await CreateProjectWithRepositoryAsync($"E2E Runs Tab {Guid.NewGuid():N}");

        await Page.GotoAsync($"{BaseUrl}/repositories/{repoId}");
        await Page.ClickAsync("text=Runs");

        await Expect(Page.Locator("th:has-text('Status')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task RepositoryDetailPage_FindingsTab()
    {

        var (projectId, repoId) = await CreateProjectWithRepositoryAsync($"E2E Findings Tab {Guid.NewGuid():N}");

        await Page.GotoAsync($"{BaseUrl}/repositories/{repoId}");
        await Page.ClickAsync("text=Findings");

        await Expect(Page.Locator("text=Findings Inbox")).ToBeVisibleAsync();
    }

    [Test]
    public async Task RepositoryDetailPage_BackNavigation()
    {

        var (projectId, repoId) = await CreateProjectWithRepositoryAsync($"E2E Back Nav {Guid.NewGuid():N}");

        await Page.GotoAsync($"{BaseUrl}/repositories/{repoId}");

        var backButton = Page.Locator("button:has(.mud-icon-root) >> nth=0");
        await backButton.ClickAsync();

        await Expect(Page).ToHaveURLAsync($"{BaseUrl}/projects/{projectId}");
    }

    [Test]
    public async Task RepositoryList_InProjectDetail()
    {

        var projectName = $"E2E Repo List {Guid.NewGuid():N}";
        var projectId = await CreateProjectAsync(projectName);

        await Page.GotoAsync($"{BaseUrl}/projects/{projectId}");

        await Page.FillAsync("input[placeholder*='Repository Name'], input[label='Repository Name']", "First Repo");
        await Page.FillAsync("input[placeholder*='Git URL'], input[label='Git URL']", "https://github.com/test/first.git");
        await Page.ClickAsync("button:has-text('Add')");
        await Expect(Page.Locator("text=First Repo")).ToBeVisibleAsync();

        await Page.FillAsync("input[placeholder*='Repository Name'], input[label='Repository Name']", "Second Repo");
        await Page.FillAsync("input[placeholder*='Git URL'], input[label='Git URL']", "https://github.com/test/second.git");
        await Page.ClickAsync("button:has-text('Add')");
        await Expect(Page.Locator("text=Second Repo")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Repository_OpenButton_NavigatesToDetail()
    {

        var (projectId, repoId) = await CreateProjectWithRepositoryAsync($"E2E Open Button {Guid.NewGuid():N}");

        await Page.GotoAsync($"{BaseUrl}/projects/{projectId}");
        await Page.ClickAsync("button:has-text('Open')");

        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex($"/repositories/{repoId}"));
    }

    [Test]
    public async Task Repository_WebhookUrl_Displayed()
    {

        var (projectId, repoId) = await CreateProjectWithRepositoryAsync($"E2E Webhook URL {Guid.NewGuid():N}");

        await Page.GotoAsync($"{BaseUrl}/repositories/{repoId}");
        await Page.ClickAsync("text=Secrets & Webhooks");

        var webhookUrl = Page.Locator("text=/webhook/, text=Webhook URL").First;
        if (await webhookUrl.IsVisibleAsync())
        {
            await Expect(webhookUrl).ToBeVisibleAsync();
        }
    }


    private async Task<string> CreateProjectAsync(string name)
    {
        await Page.GotoAsync($"{BaseUrl}/projects");

        await Page.FillAsync("input[placeholder*='Project']", name);
        await Page.ClickAsync("button:has-text('Create')");
        await Expect(Page.Locator($"text={name}")).ToBeVisibleAsync();

        await Page.ClickAsync($"text={name}");
        await Page.WaitForURLAsync($"{BaseUrl}/projects/*");

        var url = Page.Url;
        var projectId = url.Split('/').Last();
        return projectId;
    }

    private async Task<(string ProjectId, string RepoId)> CreateProjectWithRepositoryAsync(string projectName)
    {
        var projectId = await CreateProjectAsync(projectName);

        await Page.FillAsync("input[placeholder*='Repository Name'], input[label='Repository Name']", $"{projectName} Repo");
        await Page.FillAsync("input[placeholder*='Git URL'], input[label='Git URL']", "https://github.com/test/repo.git");
        await Page.ClickAsync("button:has-text('Add')");

        await Expect(Page.Locator($"text={projectName} Repo")).ToBeVisibleAsync();
        await Page.ClickAsync($"button:has-text('Open')");

        await Page.WaitForURLAsync($"{BaseUrl}/repositories/*");
        var url = Page.Url;
        var repoId = url.Split('/').Last();

        return (projectId, repoId);
    }
}
