using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace AgentsDashboard.IntegrationTests.Api;

public sealed class AuthorizationTestFixture : IAsyncLifetime
{
    public string ConnectionString { get; private set; } = string.Empty;
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"agentsdashboard-auth-{Guid.NewGuid():N}.db");

    public HttpClient CreateClientWithRole(string role)
    {
        var factory = new WebApplicationFactory<AgentsDashboard.ControlPlane.Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                var hostedServices = services.Where(d => d.ServiceType == typeof(IHostedService)).ToList();
                foreach (var service in hostedServices)
                    services.Remove(service);

                var dispatcherDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(RunDispatcher));
                if (dispatcherDescriptor != null)
                    services.Remove(dispatcherDescriptor);

                services.AddSingleton<RunDispatcher>(_ => new RunDispatcher(
                    new MockWorkerClient(),
                    null!,
                    new MockWorkerLifecycleManager(),
                    null!,
                    null!,
                    null!,
                    Options.Create(new OrchestratorOptions()),
                    null!));

                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, RoleTestAuthHandler>("Test", _ => { });

                services.AddAuthorization(options =>
                {
                    options.AddPolicy("viewer", policy => policy.RequireAuthenticatedUser());
                    options.AddPolicy("operator", policy => policy.RequireRole("operator", "admin"));
                });
            });

            builder.UseEnvironment("Testing");
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test", role);
        return client;
    }

    public async Task InitializeAsync()
    {
        ConnectionString = $"Data Source={_databasePath}";
        Environment.SetEnvironmentVariable("Orchestrator__SqliteConnectionString", ConnectionString);
        var dbOptions = new DbContextOptionsBuilder<OrchestratorDbContext>()
            .UseSqlite(ConnectionString)
            .Options;
        await using var dbContext = new OrchestratorDbContext(dbOptions);
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await Task.CompletedTask;
        Environment.SetEnvironmentVariable("Orchestrator__SqliteConnectionString", null);
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }
}

public class RoleTestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly string _role;

    public RoleTestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
        var authHeader = Request.Headers.Authorization.ToString();
        _role = authHeader.StartsWith("Test ") ? authHeader["Test ".Length..].Trim() : "viewer";
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "test-user"),
            new Claim(ClaimTypes.NameIdentifier, "test-user-id"),
            new Claim(ClaimTypes.Role, _role),
        };

        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

[CollectionDefinition("Authorization")]
public class AuthorizationCollection : ICollectionFixture<AuthorizationTestFixture>;

[Collection("Authorization")]
public class AuthorizationApiTests(AuthorizationTestFixture fixture) : IClassFixture<AuthorizationTestFixture>
{
    [Fact]
    public async Task Viewer_CanReadProjects()
    {
        var client = fixture.CreateClientWithRole("viewer");
        var response = await client.GetAsync("/api/projects");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Viewer_CannotCreateProject()
    {
        var client = fixture.CreateClientWithRole("viewer");
        var request = new CreateProjectRequest("Test", "Description");
        var response = await client.PostAsJsonAsync("/api/projects", request);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Viewer_CannotUpdateProject()
    {
        var client = fixture.CreateClientWithRole("viewer");
        var request = new UpdateProjectRequest("Updated", "Description");
        var response = await client.PutAsJsonAsync("/api/projects/test-id", request);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Viewer_CannotDeleteProject()
    {
        var client = fixture.CreateClientWithRole("viewer");
        var response = await client.DeleteAsync("/api/projects/test-id");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Operator_CanReadProjects()
    {
        var client = fixture.CreateClientWithRole("operator");
        var response = await client.GetAsync("/api/projects");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Operator_CanCreateProject()
    {
        var client = fixture.CreateClientWithRole("operator");
        var request = new CreateProjectRequest("Test", "Description");
        var response = await client.PostAsJsonAsync("/api/projects", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Admin_CanCreateProject()
    {
        var client = fixture.CreateClientWithRole("admin");
        var request = new CreateProjectRequest("Test", "Description");
        var response = await client.PostAsJsonAsync("/api/projects", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Viewer_CannotCreateRepository()
    {
        var client = fixture.CreateClientWithRole("viewer");
        var request = new CreateRepositoryRequest("proj", "Repo", "https://github.com/test.git", "main");
        var response = await client.PostAsJsonAsync("/api/repositories", request);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Viewer_CannotCreateTask()
    {
        var client = fixture.CreateClientWithRole("viewer");
        var request = new CreateTaskRequest("repo", "Task", TaskKind.OneShot, "codex", "prompt", "cmd", false, "", true);
        var response = await client.PostAsJsonAsync("/api/tasks", request);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Viewer_CannotCreateRun()
    {
        var client = fixture.CreateClientWithRole("viewer");
        var request = new CreateRunRequest("task-id");
        var response = await client.PostAsJsonAsync("/api/runs", request);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Viewer_CannotCancelRun()
    {
        var client = fixture.CreateClientWithRole("viewer");
        var response = await client.PostAsync("/api/runs/test-id/cancel", null);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Viewer_CanReadRuns()
    {
        var client = fixture.CreateClientWithRole("viewer");
        var response = await client.GetAsync("/api/runs");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Viewer_CanReadFindings()
    {
        var client = fixture.CreateClientWithRole("viewer");
        var response = await client.GetAsync("/api/findings");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Viewer_CannotUpdateFindingState()
    {
        var client = fixture.CreateClientWithRole("viewer");
        var request = new UpdateFindingStateRequest(FindingState.Acknowledged);
        var response = await client.PatchAsJsonAsync("/api/findings/test-id", request);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Operator_CanUpdateFindingState()
    {
        var operatorClient = fixture.CreateClientWithRole("operator");
        var adminClient = fixture.CreateClientWithRole("admin");

        var projectResponse = await adminClient.PostAsJsonAsync("/api/projects", new CreateProjectRequest("P", "d"));
        var project = await projectResponse.Content.ReadFromJsonAsync<ProjectDocument>();

        var repoResponse = await adminClient.PostAsJsonAsync("/api/repositories", new CreateRepositoryRequest(project!.Id, "R", "https://github.com/test.git", "main"));
        var repo = await repoResponse.Content.ReadFromJsonAsync<RepositoryDocument>();

        var taskResponse = await adminClient.PostAsJsonAsync("/api/tasks", new CreateTaskRequest(repo!.Id, "T", TaskKind.OneShot, "codex", "p", "c", false, "", true));
        var task = await taskResponse.Content.ReadFromJsonAsync<TaskDocument>();

        var runResponse = await adminClient.PostAsJsonAsync("/api/runs", new CreateRunRequest(task!.Id));
        var run = await runResponse.Content.ReadFromJsonAsync<RunDocument>();

        await adminClient.PostAsync($"/api/runs/{run!.Id}/cancel", null);

        var request = new UpdateFindingStateRequest(FindingState.Acknowledged);
        var response = await operatorClient.PatchAsJsonAsync("/api/findings/nonexistent", request);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UnauthenticatedUser_CannotAccessProtectedEndpoints()
    {
        var factory = new WebApplicationFactory<AgentsDashboard.ControlPlane.Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                var hostedServices = services.Where(d => d.ServiceType == typeof(IHostedService)).ToList();
                foreach (var service in hostedServices)
                    services.Remove(service);
            });
            builder.UseEnvironment("Testing");
        });

        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/projects");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
