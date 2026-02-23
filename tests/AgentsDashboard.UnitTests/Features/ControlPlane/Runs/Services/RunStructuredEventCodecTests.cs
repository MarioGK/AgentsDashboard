using System.Reflection;
using System.Text.Json;



namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public sealed class RunStructuredEventCodecTests
{
    private static readonly Type CodecType = typeof(RunStructuredViewService).Assembly
        .GetType("AgentsDashboard.ControlPlane.Features.Runs.Services.RunStructuredEventCodec", throwOnError: true)!;

    [Test]
    public async Task NormalizePayloadJson_NormalizesValidJsonAndEscapesInvalidPayloads()
    {
        var normalizedJson = await InvokeNormalizePayloadJson(" { \"thinking\" : \"step\" } ");
        using (var document = JsonDocument.Parse(normalizedJson))
        {
            await Assert.That(document.RootElement.GetProperty("thinking").GetString()).IsEqualTo("step");
        }

        await Assert.That(await InvokeNormalizePayloadJson("not-json")).IsEqualTo("\"not-json\"");
        await Assert.That(await InvokeNormalizePayloadJson("   ")).IsEqualTo("{}");
    }

    [Test]
    public async Task Decode_UsesFallbacksForMissingFields()
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

        var decoded = await InvokeDecode(source);

        await Assert.That(await GetDecodedValue<string>(decoded, "EventType")).IsEqualTo("structured");
        await Assert.That(await GetDecodedValue<string>(decoded, "Category")).IsEqualTo("structured");
        await Assert.That(await GetDecodedValue<string>(decoded, "PayloadJson")).IsEqualTo("\"not-json\"");
        await Assert.That(await GetDecodedValue<string>(decoded, "Schema")).IsEqualTo("harness-structured-event-v2");
        await Assert.That(await GetDecodedValue<string>(decoded, "Summary")).IsEqualTo("summary");
        await Assert.That(await GetDecodedValue<string>(decoded, "Error")).IsEqualTo("error");
        await Assert.That(await GetDecodedValue<DateTime>(decoded, "TimestampUtc")).IsEqualTo(createdAt);
        await Assert.That(await GetDecodedValue<long>(decoded, "Sequence")).IsEqualTo(7L);
    }

    private static async Task<string> InvokeNormalizePayloadJson(string? payload)
    {
        var method = CodecType.GetMethod("NormalizePayloadJson", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        await Assert.That(method).IsNotNull();
        return (string)method!.Invoke(null, [payload])!;
    }

    private static async Task<object> InvokeDecode(RunStructuredEventDocument source)
    {
        var method = CodecType.GetMethod("Decode", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        await Assert.That(method).IsNotNull();
        return method!.Invoke(null, [source])!;
    }

    private static async Task<T> GetDecodedValue<T>(object decoded, string propertyName)
    {
        var property = decoded.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        await Assert.That(property).IsNotNull();
        return (T)property!.GetValue(decoded)!;
    }
}
