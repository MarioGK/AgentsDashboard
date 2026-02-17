using AgentsDashboard.WorkerGateway.Configuration;
using AgentsDashboard.WorkerGateway.Services;
using AgentsDashboard.WorkerGateway.Services.HarnessRuntimes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.UnitTests.WorkerGateway.Services;

public sealed class HarnessRuntimeFactoryTests
{
    [Test]
    public void Select_WhenCodexDefaultMode_UsesAppServerWithCommandFallback()
    {
        var factory = CreateFactory();
        var selection = factory.Select(CreateRequest("codex", "default"));

        selection.RuntimeMode.Should().Be("app-server");
        selection.Primary.Name.Should().Be("codex-app-server");
        selection.Fallback.Should().NotBeNull();
        selection.Fallback!.Name.Should().Be("command");
    }

    [Test]
    public void Select_WhenCodexCommandMode_UsesCommandRuntimeWithoutFallback()
    {
        var factory = CreateFactory();
        var selection = factory.Select(CreateRequest("codex", "command"));

        selection.RuntimeMode.Should().Be("command");
        selection.Primary.Name.Should().Be("command");
        selection.Fallback.Should().BeNull();
    }

    [Test]
    public void Select_WhenOpenCode_UsesSseRuntimeWithoutFallback()
    {
        var factory = CreateFactory();
        var selection = factory.Select(CreateRequest("opencode", "default"));

        selection.RuntimeMode.Should().Be("sse");
        selection.Primary.Name.Should().Be("opencode-sse");
        selection.Fallback.Should().BeNull();
    }

    [Test]
    public void Select_WhenClaudeAndZai_UseStreamJsonRuntimes()
    {
        var factory = CreateFactory();
        var claudeSelection = factory.Select(CreateRequest("claude-code", "review"));
        var zaiSelection = factory.Select(CreateRequest("zai", "review"));

        claudeSelection.RuntimeMode.Should().Be("stream-json");
        claudeSelection.Primary.Name.Should().Be("claude-stream-json");
        claudeSelection.Fallback.Should().NotBeNull();

        zaiSelection.RuntimeMode.Should().Be("stream-json");
        zaiSelection.Primary.Name.Should().Be("zai-claude-compatible-stream-json");
        zaiSelection.Fallback.Should().NotBeNull();
    }

    private static DefaultHarnessRuntimeFactory CreateFactory()
    {
        var workerOptions = Options.Create(new WorkerOptions());
        var redactor = new SecretRedactor(workerOptions);
        var commandRuntime = new CommandHarnessRuntime(
            workerOptions,
            Mock.Of<IDockerContainerService>(),
            redactor,
            NullLogger<CommandHarnessRuntime>.Instance);
        var codexRuntime = new CodexAppServerRuntime(redactor, NullLogger<CodexAppServerRuntime>.Instance);
        var openCodeRuntime = new OpenCodeSseRuntime(redactor, NullLogger<OpenCodeSseRuntime>.Instance);
        var claudeRuntime = new ClaudeStreamRuntime(redactor, NullLogger<ClaudeStreamRuntime>.Instance);
        var zaiRuntime = new ZaiClaudeCompatibleRuntime(redactor, NullLogger<ZaiClaudeCompatibleRuntime>.Instance);

        return new DefaultHarnessRuntimeFactory(
            codexRuntime,
            openCodeRuntime,
            claudeRuntime,
            zaiRuntime,
            commandRuntime);
    }

    private static HarnessRunRequest CreateRequest(string harness, string mode)
    {
        return new HarnessRunRequest
        {
            RunId = "run-1",
            TaskId = "task-1",
            Harness = harness,
            Mode = mode,
            Prompt = "test",
            WorkspacePath = "/tmp",
            Environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Timeout = TimeSpan.FromMinutes(5),
        };
    }
}
