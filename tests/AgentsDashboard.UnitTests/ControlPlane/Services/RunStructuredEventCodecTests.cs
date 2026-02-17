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
            document.RootElement.GetProperty("thinking").GetString().Should().Be("step");
        }

        InvokeNormalizePayloadJson("not-json")
            .Should()
            .Be("\"not-json\"");

        InvokeNormalizePayloadJson("   ")
            .Should()
            .Be("{}");
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

        GetDecodedValue<string>(decoded, "EventType").Should().Be("structured");
        GetDecodedValue<string>(decoded, "Category").Should().Be("structured");
        GetDecodedValue<string>(decoded, "PayloadJson").Should().Be("\"not-json\"");
        GetDecodedValue<string>(decoded, "Schema").Should().Be("harness-structured-event-v2");
        GetDecodedValue<string>(decoded, "Summary").Should().Be("summary");
        GetDecodedValue<string>(decoded, "Error").Should().Be("error");
        GetDecodedValue<DateTime>(decoded, "TimestampUtc").Should().Be(createdAt);
        GetDecodedValue<long>(decoded, "Sequence").Should().Be(7L);
    }

    private static string InvokeNormalizePayloadJson(string? payload)
    {
        var method = CodecType.GetMethod("NormalizePayloadJson", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return (string)method!.Invoke(null, [payload])!;
    }

    private static object InvokeDecode(RunStructuredEventDocument source)
    {
        var method = CodecType.GetMethod("Decode", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return method!.Invoke(null, [source])!;
    }

    private static T GetDecodedValue<T>(object decoded, string propertyName)
    {
        var property = decoded.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        property.Should().NotBeNull();
        return (T)property!.GetValue(decoded)!;
    }
}
