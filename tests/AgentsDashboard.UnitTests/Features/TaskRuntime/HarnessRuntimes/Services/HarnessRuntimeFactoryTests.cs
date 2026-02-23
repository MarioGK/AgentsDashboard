


using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.UnitTests.TaskRuntime.Services;

public sealed class HarnessRuntimeFactoryTests
{
    [Test]
    public async Task Select_WhenCodexDefaultMode_UsesStdioRuntimeWithoutFallback()
    {
        var factory = CreateFactory();
        var selection = factory.Select(CreateRequest("codex", "default"));

        await Assert.That(selection.RuntimeMode).IsEqualTo("stdio");
        await Assert.That(selection.Primary.Name).IsEqualTo("codex-stdio");
        await Assert.That(selection.Fallback).IsNull();
    }

    [Test]
    public async Task Select_WhenCodexCommandMode_UsesStdioRuntimeWithoutFallback()
    {
        var factory = CreateFactory();
        var selection = factory.Select(CreateRequest("codex", "command"));

        await Assert.That(selection.RuntimeMode).IsEqualTo("stdio");
        await Assert.That(selection.Primary.Name).IsEqualTo("codex-stdio");
        await Assert.That(selection.Fallback).IsNull();
    }

    [Test]
    public async Task Select_WhenOpenCode_UsesSseRuntimeWithoutFallback()
    {
        var factory = CreateFactory();
        var selection = factory.Select(CreateRequest("opencode", "default"));

        await Assert.That(selection.RuntimeMode).IsEqualTo("sse");
        await Assert.That(selection.Primary.Name).IsEqualTo("opencode-sse");
        await Assert.That(selection.Fallback).IsNull();
    }

    [Test]
    public async Task Select_WhenUnsupportedHarness_ThrowsNotSupportedException()
    {
        var factory = CreateFactory();
        var request = CreateRequest("other", "default");

        var action = () => factory.Select(request);

        await Assert.That(action).Throws<NotSupportedException>();
    }

    private static DefaultHarnessRuntimeFactory CreateFactory()
    {
        var redactor = new SecretRedactor(Options.Create(new TaskRuntimeOptions()));
        var codexRuntime = new CodexAppServerRuntime(redactor, NullLogger<CodexAppServerRuntime>.Instance);
        var openCodeRuntime = new OpenCodeSseRuntime(redactor, NullLogger<OpenCodeSseRuntime>.Instance);

        return new DefaultHarnessRuntimeFactory(
            codexRuntime,
            openCodeRuntime);
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
