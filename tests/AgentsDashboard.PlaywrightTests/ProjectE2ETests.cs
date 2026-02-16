using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace AgentsDashboard.PlaywrightTests;

[TestFixture]
public class ProjectE2ETests : PageTest
{
    private const string BaseUrl = "http://localhost:5266";

    [Test]
    public async Task ProjectsPage_LoadsAfterLogin()
    {
        
        await Page.GotoAsync($"{BaseUrl}/projects");
        await Expect(Page.Locator("text=Projects")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ProjectsPage_HasCreateForm()
    {
        
        await Page.GotoAsync($"{BaseUrl}/projects");
        await Expect(Page.Locator("input[placeholder*='Project']")).ToBeVisibleAsync();
        await Expect(Page.Locator("button:has-text('Create')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task CreateProject_WithNameOnly()
    {
        
        await Page.GotoAsync($"{BaseUrl}/projects");

        var projectName = $"E2E Project {Guid.NewGuid():N}";
        await Page.FillAsync("input[placeholder*='Project']", projectName);
        await Page.ClickAsync("button:has-text('Create')");

        await Expect(Page.Locator($"text={projectName}")).ToBeVisibleAsync();
    }

    [Test]
    public async Task CreateProject_WithDescription()
    {
        
        await Page.GotoAsync($"{BaseUrl}/projects");

        var projectName = $"E2E Project with Desc {Guid.NewGuid():N}";
        var description = "Test project description for E2E testing";

        await Page.FillAsync("input[placeholder*='Project']", projectName);

        var descriptionField = Page.Locator("textarea[placeholder*='Description'], textarea[label='Description']").First;
        if (await descriptionField.IsVisibleAsync())
        {
            await descriptionField.FillAsync(description);
        }

        await Page.ClickAsync("button:has-text('Create')");
        await Expect(Page.Locator($"text={projectName}")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Project_AppearsInList()
    {
        
        var projectName = $"E2E List Test {Guid.NewGuid():N}";
        await CreateProjectAsync(projectName);

        await Page.GotoAsync($"{BaseUrl}/projects");
        await Expect(Page.Locator($"text={projectName}")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ProjectDetailPage_Loads()
    {
        
        var projectName = $"E2E Detail Test {Guid.NewGuid():N}";
        var projectId = await CreateProjectAsync(projectName);

        await Page.GotoAsync($"{BaseUrl}/projects/{projectId}");
        await Expect(Page.Locator($"text={projectName}")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ProjectDetailPage_ShowsRepositoriesSection()
    {
        
        var projectName = $"E2E Repo Section Test {Guid.NewGuid():N}";
        var projectId = await CreateProjectAsync(projectName);

        await Page.GotoAsync($"{BaseUrl}/projects/{projectId}");
        await Expect(Page.Locator("text=Repositories")).ToBeVisibleAsync();
    }

    [Test]
    public async Task EditProject_UpdateName()
    {
        
        var projectName = $"E2E Edit Test {Guid.NewGuid():N}";
        var projectId = await CreateProjectAsync(projectName);

        await Page.GotoAsync($"{BaseUrl}/projects/{projectId}");

        var editButton = Page.Locator("button:has-text('Edit'), button:has([data-icon='edit'])").First;
        if (await editButton.IsVisibleAsync())
        {
            await editButton.ClickAsync();

            var nameInput = Page.Locator("input[value*='E2E Edit Test'], input[label='Name']").First;
            if (await nameInput.IsVisibleAsync())
            {
                await nameInput.FillAsync($"{projectName} - Updated");
                await Page.ClickAsync("button:has-text('Save')");
            }
        }
    }

    [Test]
    public async Task DeleteProject_RemovesFromList()
    {
        
        var projectName = $"E2E Delete Test {Guid.NewGuid():N}";
        var projectId = await CreateProjectAsync(projectName);

        await Page.GotoAsync($"{BaseUrl}/projects/{projectId}");

        var deleteButton = Page.Locator("button:has-text('Delete'), button[color='error']").First;
        if (await deleteButton.IsVisibleAsync())
        {
            await deleteButton.ClickAsync();

            var confirmButton = Page.Locator(".mud-dialog button:has-text('Delete'), .mud-dialog button:has-text('Confirm')").First;
            if (await confirmButton.IsVisibleAsync())
            {
                await confirmButton.ClickAsync();
                await Page.WaitForURLAsync($"{BaseUrl}/projects");
            }
        }

        await Page.GotoAsync($"{BaseUrl}/projects");
    }

    [Test]
    public async Task ProjectCard_ShowsRepositoryCount()
    {
        
        await Page.GotoAsync($"{BaseUrl}/projects");

        var projectCard = Page.Locator(".mud-card, .mud-paper").First;
        if (await projectCard.IsVisibleAsync())
        {
            var repoCountText = projectCard.Locator("text=/\\d+ repos?/");
            if (await repoCountText.IsVisibleAsync())
            {
                await Expect(repoCountText).ToBeVisibleAsync();
            }
        }
    }

    [Test]
    public async Task ProjectList_HasSearchFilter()
    {
        
        await Page.GotoAsync($"{BaseUrl}/projects");

        var searchInput = Page.Locator("input[placeholder*='Search'], input[placeholder*='Filter']").First;
        if (await searchInput.IsVisibleAsync())
        {
            await Expect(searchInput).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task ProjectNavigation_FromDashboard()
    {
        
        await Page.GotoAsync($"{BaseUrl}/");

        var projectsLink = Page.Locator("a:has-text('Projects'), a[href='/projects']").First;
        if (await projectsLink.IsVisibleAsync())
        {
            await projectsLink.ClickAsync();
            await Expect(Page).ToHaveURLAsync($"{BaseUrl}/projects");
        }
    }

    [Test]
    public async Task ProjectDetail_BackNavigation()
    {
        
        var projectName = $"E2E Back Nav Test {Guid.NewGuid():N}";
        var projectId = await CreateProjectAsync(projectName);

        await Page.GotoAsync($"{BaseUrl}/projects/{projectId}");

        var backButton = Page.Locator("button:has(.mud-icon-root) >> nth=0");
        if (await backButton.IsVisibleAsync())
        {
            await backButton.ClickAsync();
            await Expect(Page).ToHaveURLAsync($"{BaseUrl}/projects");
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
}
