using AgentsDashboard.TaskRuntimeGateway.Configuration;
using AgentsDashboard.TaskRuntimeGateway.Services;
using AgentsDashboard.TaskRuntimeGateway.Services.HarnessRuntimes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.UnitTests.TaskRuntimeGateway.Services;

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

    private static DefaultHarnessRuntimeFactory CreateFactory()
    {
        var workerOptions = Options.Create(new TaskRuntimeOptions());
        var redactor = new SecretRedactor(workerOptions);
        var commandRuntime = new CommandHarnessRuntime(
            workerOptions,
            Mock.Of<IDockerContainerService>(),
            redactor,
            NullLogger<CommandHarnessRuntime>.Instance);
        var codexRuntime = new CodexAppServerRuntime(redactor, NullLogger<CodexAppServerRuntime>.Instance);
        var openCodeRuntime = new OpenCodeSseRuntime(redactor, NullLogger<OpenCodeSseRuntime>.Instance);

        return new DefaultHarnessRuntimeFactory(
            codexRuntime,
            openCodeRuntime,
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
