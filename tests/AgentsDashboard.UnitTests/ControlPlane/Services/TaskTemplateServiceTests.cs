using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Services;
using FluentAssertions;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public class TaskTemplateServiceTests
{
    [Fact]
    public void GetTemplates_ReturnsFourTemplates()
    {
        var templates = TaskTemplateService.GetTemplates();
        templates.Should().HaveCount(4);
    }

    [Fact]
    public void GetTemplates_ContainsQaBrowserSweep()
    {
        var templates = TaskTemplateService.GetTemplates();
        var template = templates.FirstOrDefault(t => t.Id == "qa-browser-sweep");

        template.Should().NotBeNull();
        template!.Name.Should().Be("QA Browser Sweep");
        template.Description.Should().Contain("Playwright");
        template.Harness.Should().Be("claude-code");
        template.Kind.Should().Be(TaskKind.OneShot);
        template.Command.Should().Contain("playwright test");
    }

    [Fact]
    public void GetTemplates_ContainsUnitTestGuard()
    {
        var templates = TaskTemplateService.GetTemplates();
        var template = templates.FirstOrDefault(t => t.Id == "unit-test-guard");

        template.Should().NotBeNull();
        template!.Name.Should().Be("Unit Test Guard");
        template.Description.Should().Contain("auto-fix");
        template.Harness.Should().Be("codex");
        template.Kind.Should().Be(TaskKind.OneShot);
        template.Command.Should().Contain("dotnet test");
    }

    [Fact]
    public void GetTemplates_ContainsDependencyHealthCheck()
    {
        var templates = TaskTemplateService.GetTemplates();
        var template = templates.FirstOrDefault(t => t.Id == "dep-health-check");

        template.Should().NotBeNull();
        template!.Name.Should().Be("Dependency Health Check");
        template.Description.Should().Contain("security vulnerabilities");
        template.Harness.Should().Be("opencode");
        template.Kind.Should().Be(TaskKind.Cron);
        template.Command.Should().Contain("npm audit");
    }

    [Fact]
    public void GetTemplates_ContainsRegressionReplay()
    {
        var templates = TaskTemplateService.GetTemplates();
        var template = templates.FirstOrDefault(t => t.Id == "regression-replay");

        template.Should().NotBeNull();
        template!.Name.Should().Be("Regression Replay");
        template.Description.Should().Contain("verify fixes");
        template.Harness.Should().Be("claude-code");
        template.Kind.Should().Be(TaskKind.OneShot);
        template.Command.Should().Contain("failures.json");
    }

    [Fact]
    public void AllTemplates_HaveNonEmptyPrompts()
    {
        var templates = TaskTemplateService.GetTemplates();
        foreach (var template in templates)
        {
            template.Prompt.Should().NotBeNullOrEmpty();
            template.Prompt.Length.Should().BeGreaterThanOrEqualTo(20);
        }
    }

    [Fact]
    public void AllTemplates_HaveValidHarnesses()
    {
        var validHarnesses = new[] { "codex", "opencode", "claude-code", "zai" };
        var templates = TaskTemplateService.GetTemplates();

        foreach (var template in templates)
        {
            validHarnesses.Should().Contain(template.Harness);
        }
    }

    [Fact]
    public void AllTemplates_HaveUniqueIds()
    {
        var templates = TaskTemplateService.GetTemplates();
        var ids = templates.Select(t => t.Id).ToList();
        ids.Distinct().Count().Should().Be(ids.Count);
    }
}
