using System.Reflection;
using System.Text.Json;
using AgentsDashboard.TaskRuntime.Services;

namespace AgentsDashboard.UnitTests.TaskRuntime.Services;

public sealed class JobProcessorServiceTests
{
    private const string RuntimeMarker = "agentsdashboard.harness-runtime-event.v1";
    private static readonly MethodInfo TryParseRuntimeEventChunkMethod = typeof(JobProcessorService)
        .GetMethod("TryParseRuntimeEventChunk", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo BuildStructuredProjectionMethod = typeof(JobProcessorService)
        .GetMethod("BuildStructuredProjection", BindingFlags.NonPublic | BindingFlags.Static)!;

    [Test]
    public async Task TryParseRuntimeEventChunk_ParsesValidWireEvent()
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

        await Assert.That(parsed).IsTrue();
        await Assert.That(runtimeEvent).IsNotNull();
        await Assert.That(await GetPropertyValue<long>(runtimeEvent!, "Sequence")).IsEqualTo(12);
        await Assert.That(await GetPropertyValue<string>(runtimeEvent, "Type")).IsEqualTo("assistant_delta");
        await Assert.That(await GetPropertyValue<string>(runtimeEvent, "Content")).IsEqualTo("hello");
    }

    [Test]
    public async Task TryParseRuntimeEventChunk_RejectsInvalidMarker()
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

        await Assert.That(parsed).IsFalse();
        await Assert.That(runtimeEvent).IsNull();
    }

    [Test]
    public async Task BuildStructuredProjection_MapsReasoningDeltaPayload()
    {
        var runtimeEvent = await ParseRuntimeEvent("reasoning_delta", "plan step");

        var projection = InvokeBuildStructuredProjection(runtimeEvent, "harness-structured-event-v2");

        await Assert.That(await GetPropertyValue<string>(projection, "Category")).IsEqualTo("reasoning.delta");
        await Assert.That(await GetPropertyValue<string>(projection, "SchemaVersion")).IsEqualTo("harness-structured-event-v2");

        var payload = await GetPropertyValue<string>(projection, "PayloadJson");
        using var document = JsonDocument.Parse(payload);
        await Assert.That(document.RootElement.GetProperty("thinking").GetString()).IsEqualTo("plan step");
        await Assert.That(document.RootElement.GetProperty("reasoning").GetString()).IsEqualTo("plan step");
        await Assert.That(document.RootElement.GetProperty("content").GetString()).IsEqualTo("plan step");
    }

    [Test]
    public async Task BuildStructuredProjection_MapsCompletionToRunCompletedCategory()
    {
        var runtimeEvent = await ParseRuntimeEvent(
            "completion",
            "completed",
            new Dictionary<string, string> { ["status"] = "succeeded" });

        var projection = InvokeBuildStructuredProjection(runtimeEvent, "harness-structured-event-v2");

        await Assert.That(await GetPropertyValue<string>(projection, "Category")).IsEqualTo("run.completed");

        var payload = await GetPropertyValue<string>(projection, "PayloadJson");
        using var document = JsonDocument.Parse(payload);
        await Assert.That(document.RootElement.GetProperty("status").GetString()).IsEqualTo("succeeded");
        await Assert.That(document.RootElement.GetProperty("content").GetString()).IsEqualTo("completed");
    }

    [Test]
    public async Task BuildStructuredProjection_UsesEmbeddedStructuredProjectionWhenPresent()
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
        var runtimeEvent = await ParseRuntimeEvent("log", embeddedPayload);

        var projection = InvokeBuildStructuredProjection(runtimeEvent, "harness-structured-event-v2");

        await Assert.That(await GetPropertyValue<string>(projection, "Category")).IsEqualTo("diff.updated");
        await Assert.That(await GetPropertyValue<string>(projection, "SchemaVersion")).IsEqualTo("custom-v3");

        var payload = await GetPropertyValue<string>(projection, "PayloadJson");
        using var document = JsonDocument.Parse(payload);
        await Assert.That(document.RootElement.GetProperty("diffPatch").GetString()).IsEqualTo("diff --git a/a.txt b/a.txt");
        await Assert.That(document.RootElement.GetProperty("diffStat").GetString()).IsEqualTo("1 file changed");
        await Assert.That(document.RootElement.TryGetProperty("type", out _)).IsFalse();
    }

    [Test]
    public async Task BuildStructuredProjection_CanonicalizesEmbeddedSessionDiffCategory()
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
        var runtimeEvent = await ParseRuntimeEvent("log", embeddedPayload);

        var projection = InvokeBuildStructuredProjection(runtimeEvent, "harness-structured-event-v2");

        await Assert.That(await GetPropertyValue<string>(projection, "Category")).IsEqualTo("diff.updated");
        await Assert.That(await GetPropertyValue<string>(projection, "SchemaVersion")).IsEqualTo("opencode.sse.v1");
    }

    private static (bool Parsed, object? RuntimeEvent) InvokeTryParseRuntimeEventChunk(string chunk)
    {
        var args = new object?[] { chunk, null };
        var parsed = (bool)TryParseRuntimeEventChunkMethod.Invoke(null, args)!;
        return (parsed, args[1]);
    }

    private static async Task<object> ParseRuntimeEvent(
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
        await Assert.That(parsed).IsTrue();
        await Assert.That(runtimeEvent).IsNotNull();
        return runtimeEvent!;
    }

    private static object InvokeBuildStructuredProjection(object runtimeEvent, string schemaVersion)
    {
        return BuildStructuredProjectionMethod.Invoke(null, [runtimeEvent, schemaVersion])!;
    }

    private static async Task<T> GetPropertyValue<T>(object source, string propertyName)
    {
        var property = source.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        await Assert.That(property).IsNotNull();
        return (T)property!.GetValue(source)!;
    }
}
