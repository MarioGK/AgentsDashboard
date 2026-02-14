using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.WorkerGateway.Adapters;
using AgentsDashboard.WorkerGateway.Configuration;
using AgentsDashboard.WorkerGateway.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.UnitTests.WorkerGateway.Adapters;

public class HarnessAdapterFactoryTests
{
    private static HarnessAdapterFactory CreateFactory()
    {
        var options = Options.Create(new WorkerOptions());
        var redactor = new SecretRedactor(options);
        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        return new HarnessAdapterFactory(options, redactor, serviceProvider);
    }

    [Fact]
    public void Create_WithCodex_ReturnsCodexAdapter()
    {
        var factory = CreateFactory();
        var adapter = factory.Create("codex");
        adapter.Should().BeOfType<CodexAdapter>();
        adapter.HarnessName.Should().Be("codex");
    }

    [Fact]
    public void Create_WithOpenCode_ReturnsOpenCodeAdapter()
    {
        var factory = CreateFactory();
        var adapter = factory.Create("opencode");
        adapter.Should().BeOfType<OpenCodeAdapter>();
        adapter.HarnessName.Should().Be("opencode");
    }

    [Fact]
    public void Create_WithClaudeCode_ReturnsClaudeCodeAdapter()
    {
        var factory = CreateFactory();
        var adapter = factory.Create("claude-code");
        adapter.Should().BeOfType<ClaudeCodeAdapter>();
        adapter.HarnessName.Should().Be("claude-code");
    }

    [Fact]
    public void Create_WithClaudeCodeAlternateName_ReturnsClaudeCodeAdapter()
    {
        var factory = CreateFactory();
        var adapter = factory.Create("claude code");
        adapter.Should().BeOfType<ClaudeCodeAdapter>();
        adapter.HarnessName.Should().Be("claude-code");
    }

    [Fact]
    public void Create_WithZai_ReturnsZaiAdapter()
    {
        var factory = CreateFactory();
        var adapter = factory.Create("zai");
        adapter.Should().BeOfType<ZaiAdapter>();
        adapter.HarnessName.Should().Be("zai");
    }

    [Theory]
    [InlineData("CODEX")]
    [InlineData("CoDeX")]
    [InlineData("  codex  ")]
    public void Create_WithDifferentCasing_ReturnsCorrectAdapter(string harnessName)
    {
        var factory = CreateFactory();
        var adapter = factory.Create(harnessName);
        adapter.Should().BeOfType<CodexAdapter>();
    }

    [Fact]
    public void Create_WithUnknownHarness_ThrowsNotSupportedException()
    {
        var factory = CreateFactory();
        var act = () => factory.Create("unknown-harness");
        act.Should().Throw<NotSupportedException>()
            .WithMessage("*not supported*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithEmptyOrNullHarness_ThrowsArgumentException(string? harnessName)
    {
        var factory = CreateFactory();
        var act = () => factory.Create(harnessName!);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*cannot be empty*");
    }

    [Fact]
    public void IsSupported_WithKnownHarness_ReturnsTrue()
    {
        var factory = CreateFactory();
        factory.IsSupported("codex").Should().BeTrue();
        factory.IsSupported("opencode").Should().BeTrue();
        factory.IsSupported("claude-code").Should().BeTrue();
        factory.IsSupported("claude code").Should().BeTrue();
        factory.IsSupported("zai").Should().BeTrue();
    }

    [Theory]
    [InlineData("codex", true)]
    [InlineData("opencode", true)]
    [InlineData("claude-code", true)]
    [InlineData("claude code", true)]
    [InlineData("zai", true)]
    [InlineData("CODEX", true)]
    [InlineData("unknown", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSupported_ReturnsExpectedResult(string? harnessName, bool expected)
    {
        var factory = CreateFactory();
        factory.IsSupported(harnessName!).Should().Be(expected);
    }

    [Fact]
    public void GetSupportedHarnesses_ReturnsAllHarnesses()
    {
        var factory = CreateFactory();
        var harnesses = factory.GetSupportedHarnesses();
        harnesses.Should().Contain("codex");
        harnesses.Should().Contain("opencode");
        harnesses.Should().Contain("claude-code");
        harnesses.Should().Contain("claude code");
        harnesses.Should().Contain("zai");
    }

    [Fact]
    public void RegisterAdapter_AddsNewHarness()
    {
        var factory = CreateFactory();
        factory.RegisterAdapter("custom", () => new TestAdapter());
        factory.IsSupported("custom").Should().BeTrue();
        var adapter = factory.Create("custom");
        adapter.Should().BeOfType<TestAdapter>();
    }

    [Fact]
    public void RegisterAdapter_OverwritesExistingHarness()
    {
        var factory = CreateFactory();
        factory.RegisterAdapter("codex", () => new TestAdapter());
        var adapter = factory.Create("codex");
        adapter.Should().BeOfType<TestAdapter>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void RegisterAdapter_WithEmptyName_ThrowsArgumentException(string? harnessName)
    {
        var factory = CreateFactory();
        var act = () => factory.RegisterAdapter(harnessName!, () => new TestAdapter());
        act.Should().Throw<ArgumentException>()
            .WithMessage("*cannot be empty*");
    }

    [Fact]
    public void RegisterAdapter_WithNullFactory_ThrowsArgumentNullException()
    {
        var factory = CreateFactory();
        var act = () => factory.RegisterAdapter("test", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    private class TestAdapter : IHarnessAdapter
    {
        public string HarnessName => "custom";
        public HarnessExecutionContext PrepareContext(DispatchJobRequest request) => new()
        {
            RunId = "test",
            Harness = "test",
            Prompt = "",
            Command = "",
            Image = "test:latest",
            WorkspacePath = "/workspace",
            GitUrl = "",
            Env = new Dictionary<string, string>(),
            ContainerLabels = new Dictionary<string, string>()
        };
        public HarnessCommand BuildCommand(HarnessExecutionContext context) => new() { FileName = "test" };
        public Task<HarnessResultEnvelope> ExecuteAsync(HarnessExecutionContext context, HarnessCommand command, CancellationToken cancellationToken) => Task.FromResult(new HarnessResultEnvelope());
        public HarnessResultEnvelope ParseEnvelope(string stdout, string stderr, int exitCode) => new();
        public IReadOnlyList<HarnessArtifact> MapArtifacts(HarnessResultEnvelope envelope) => [];
        public FailureClassification ClassifyFailure(HarnessResultEnvelope envelope) => FailureClassification.Success();
    }
}
