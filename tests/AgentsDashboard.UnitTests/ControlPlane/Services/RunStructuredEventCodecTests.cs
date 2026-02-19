using System.Reflection;
using System.Text.Json;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Services;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public sealed class RunStructuredEventCodecTests
{
    private static readonly Type CodecType = typeof(RunStructuredViewService).Assembly
        .GetType("AgentsDashboard.ControlPlane.Services.RunStructuredEventCodec", throwOnError: true)!;

    [Test]
    public void NormalizePayloadJson_NormalizesValidJsonAndEscapesInvalidPayloads()
    {
        var normalizedJson = InvokeNormalizePayloadJson(" { \"thinking\" : \"step\" } ");
        using (var document = JsonDocument.Parse(normalizedJson))
        {
            Assert.That(document.RootElement.GetProperty("thinking").GetString()).IsEqualTo("step");
        }

        Assert.That(InvokeNormalizePayloadJson("not-json")).IsEqualTo("\"not-json\"");

        Assert.That(InvokeNormalizePayloadJson("   ")).IsEqualTo("{}");
    }

    [Test]
    public void Decode_UsesFallbacksForMissingFields()
    {
        var createdAt = new DateTime(2026, 2, 17, 12, 0, 0, DateTimeKind.Utc);
        var source = new RunStructuredEventDocument
        {
            RunId = "run-1",
            Sequence = 7,
            EventType = "   ",
            Category = "   ",
            PayloadJson = "not-json",
            SchemaVersion = " harness-structured-event-v2 ",
            Summary = " summary ",
            Error = " error ",
            TimestampUtc = default,
            CreatedAtUtc = createdAt,
        };

        var decoded = InvokeDecode(source);

        Assert.That(GetDecodedValue<string>(decoded, "EventType")).IsEqualTo("structured");
        Assert.That(GetDecodedValue<string>(decoded, "Category")).IsEqualTo("structured");
        Assert.That(GetDecodedValue<string>(decoded, "PayloadJson")).IsEqualTo("\"not-json\"");
        Assert.That(GetDecodedValue<string>(decoded, "Schema")).IsEqualTo("harness-structured-event-v2");
        Assert.That(GetDecodedValue<string>(decoded, "Summary")).IsEqualTo("summary");
        Assert.That(GetDecodedValue<string>(decoded, "Error")).IsEqualTo("error");
        Assert.That(GetDecodedValue<DateTime>(decoded, "TimestampUtc")).IsEqualTo(createdAt);
        Assert.That(GetDecodedValue<long>(decoded, "Sequence")).IsEqualTo(7L);
    }

    private static string InvokeNormalizePayloadJson(string? payload)
    {
        var method = CodecType.GetMethod("NormalizePayloadJson", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method).IsNotNull();
        return (string)method!.Invoke(null, [payload])!;
    }

    private static object InvokeDecode(RunStructuredEventDocument source)
    {
        var method = CodecType.GetMethod("Decode", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method).IsNotNull();
        return method!.Invoke(null, [source])!;
    }

    private static T GetDecodedValue<T>(object decoded, string propertyName)
    {
        var property = decoded.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(property).IsNotNull();
        return (T)property!.GetValue(decoded)!;
    }
}
