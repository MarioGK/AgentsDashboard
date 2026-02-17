using System.Reflection;
using System.Text.Json;
using AgentsDashboard.WorkerGateway.Services;

namespace AgentsDashboard.UnitTests.WorkerGateway.Services;

public sealed class JobProcessorServiceTests
{
    private const string RuntimeMarker = "agentsdashboard.harness-runtime-event.v1";
    private static readonly MethodInfo TryParseRuntimeEventChunkMethod = typeof(JobProcessorService)
        .GetMethod("TryParseRuntimeEventChunk", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo BuildStructuredProjectionMethod = typeof(JobProcessorService)
        .GetMethod("BuildStructuredProjection", BindingFlags.NonPublic | BindingFlags.Static)!;

    [Test]
    public void TryParseRuntimeEventChunk_ParsesValidWireEvent()
    {
        var chunk = JsonSerializer.Serialize(new
        {
            Marker = RuntimeMarker,
            Sequence = 12,
            Type = "assistant_delta",
            Content = "hello",
            Metadata = new Dictionary<string, string> { ["channel"] = "assistant" }
        });

        var (parsed, runtimeEvent) = InvokeTryParseRuntimeEventChunk(chunk);

        parsed.Should().BeTrue();
        runtimeEvent.Should().NotBeNull();
        GetPropertyValue<long>(runtimeEvent!, "Sequence").Should().Be(12);
        GetPropertyValue<string>(runtimeEvent, "Type").Should().Be("assistant_delta");
        GetPropertyValue<string>(runtimeEvent, "Content").Should().Be("hello");
    }

    [Test]
    public void TryParseRuntimeEventChunk_RejectsInvalidMarker()
    {
        var chunk = JsonSerializer.Serialize(new
        {
            Marker = "not-supported",
            Sequence = 12,
            Type = "assistant_delta",
            Content = "hello",
            Metadata = new Dictionary<string, string>()
        });

        var (parsed, runtimeEvent) = InvokeTryParseRuntimeEventChunk(chunk);

        parsed.Should().BeFalse();
        runtimeEvent.Should().BeNull();
    }

    [Test]
    public void BuildStructuredProjection_MapsReasoningDeltaPayload()
    {
        var runtimeEvent = ParseRuntimeEvent("reasoning_delta", "plan step");

        var projection = InvokeBuildStructuredProjection(runtimeEvent, "harness-structured-event-v2");

        GetPropertyValue<string>(projection, "Category").Should().Be("reasoning.delta");
        GetPropertyValue<string>(projection, "SchemaVersion").Should().Be("harness-structured-event-v2");

        var payload = GetPropertyValue<string>(projection, "PayloadJson");
        using var document = JsonDocument.Parse(payload);
        document.RootElement.GetProperty("thinking").GetString().Should().Be("plan step");
        document.RootElement.GetProperty("reasoning").GetString().Should().Be("plan step");
        document.RootElement.GetProperty("content").GetString().Should().Be("plan step");
    }

    [Test]
    public void BuildStructuredProjection_MapsCompletionToRunCompletedCategory()
    {
        var runtimeEvent = ParseRuntimeEvent(
            "completion",
            "completed",
            new Dictionary<string, string> { ["status"] = "succeeded" });

        var projection = InvokeBuildStructuredProjection(runtimeEvent, "harness-structured-event-v2");

        GetPropertyValue<string>(projection, "Category").Should().Be("run.completed");

        var payload = GetPropertyValue<string>(projection, "PayloadJson");
        using var document = JsonDocument.Parse(payload);
        document.RootElement.GetProperty("status").GetString().Should().Be("succeeded");
        document.RootElement.GetProperty("content").GetString().Should().Be("completed");
    }

    [Test]
    public void BuildStructuredProjection_UsesEmbeddedStructuredProjectionWhenPresent()
    {
        var embeddedPayload = JsonSerializer.Serialize(new
        {
            type = "diff.updated",
            schemaVersion = "custom-v3",
            properties = new
            {
                diffPatch = "diff --git a/a.txt b/a.txt",
                diffStat = "1 file changed"
            }
        });
        var runtimeEvent = ParseRuntimeEvent("log", embeddedPayload);

        var projection = InvokeBuildStructuredProjection(runtimeEvent, "harness-structured-event-v2");

        GetPropertyValue<string>(projection, "Category").Should().Be("diff.updated");
        GetPropertyValue<string>(projection, "SchemaVersion").Should().Be("custom-v3");

        var payload = GetPropertyValue<string>(projection, "PayloadJson");
        using var document = JsonDocument.Parse(payload);
        document.RootElement.GetProperty("diffPatch").GetString().Should().Be("diff --git a/a.txt b/a.txt");
        document.RootElement.GetProperty("diffStat").GetString().Should().Be("1 file changed");
        document.RootElement.TryGetProperty("type", out _).Should().BeFalse();
    }

    [Test]
    public void BuildStructuredProjection_CanonicalizesEmbeddedSessionDiffCategory()
    {
        var embeddedPayload = JsonSerializer.Serialize(new
        {
            type = "session.diff",
            schemaVersion = "opencode.sse.v1",
            properties = new
            {
                diffPatch = "diff --git a/a.txt b/a.txt",
                diffStat = "1 file changed"
            }
        });
        var runtimeEvent = ParseRuntimeEvent("log", embeddedPayload);

        var projection = InvokeBuildStructuredProjection(runtimeEvent, "harness-structured-event-v2");

        GetPropertyValue<string>(projection, "Category").Should().Be("diff.updated");
        GetPropertyValue<string>(projection, "SchemaVersion").Should().Be("opencode.sse.v1");
    }

    private static (bool Parsed, object? RuntimeEvent) InvokeTryParseRuntimeEventChunk(string chunk)
    {
        var args = new object?[] { chunk, null };
        var parsed = (bool)TryParseRuntimeEventChunkMethod.Invoke(null, args)!;
        return (parsed, args[1]);
    }

    private static object ParseRuntimeEvent(
        string type,
        string content,
        Dictionary<string, string>? metadata = null)
    {
        var chunk = JsonSerializer.Serialize(new
        {
            Marker = RuntimeMarker,
            Sequence = 42,
            Type = type,
            Content = content,
            Metadata = metadata ?? new Dictionary<string, string>()
        });

        var (parsed, runtimeEvent) = InvokeTryParseRuntimeEventChunk(chunk);
        parsed.Should().BeTrue();
        runtimeEvent.Should().NotBeNull();
        return runtimeEvent!;
    }

    private static object InvokeBuildStructuredProjection(object runtimeEvent, string schemaVersion)
    {
        return BuildStructuredProjectionMethod.Invoke(null, [runtimeEvent, schemaVersion])!;
    }

    private static T GetPropertyValue<T>(object source, string propertyName)
    {
        var property = source.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        property.Should().NotBeNull();
        return (T)property!.GetValue(source)!;
    }
}
