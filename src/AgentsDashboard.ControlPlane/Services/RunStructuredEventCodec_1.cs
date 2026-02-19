using System.Text.Json;
using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.ControlPlane.Services;


internal static class RunStructuredEventCodec
{
    public static string NormalizePayloadJson(string? payload)
    {
        var trimmed = payload?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return "{}";
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            return document.RootElement.GetRawText();
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(trimmed);
        }
    }

    public static DecodedRunStructuredEvent Decode(RunStructuredEventDocument structuredEvent)
    {
        var eventType = structuredEvent.EventType?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(eventType))
        {
            eventType = "structured";
        }

        var category = structuredEvent.Category?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(category))
        {
            category = eventType;
        }

        var payloadJson = NormalizePayloadJson(structuredEvent.PayloadJson);
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            payloadJson = "{}";
        }

        var schema = structuredEvent.SchemaVersion?.Trim() ?? string.Empty;
        var summary = structuredEvent.Summary?.Trim() ?? string.Empty;
        var error = structuredEvent.Error?.Trim() ?? string.Empty;

        var timestampUtc = structuredEvent.TimestampUtc;
        if (timestampUtc == default)
        {
            timestampUtc = structuredEvent.CreatedAtUtc == default
                ? DateTime.UtcNow
                : structuredEvent.CreatedAtUtc;
        }

        return new DecodedRunStructuredEvent(
            eventType,
            category,
            payloadJson,
            schema,
            summary,
            error,
            timestampUtc,
            structuredEvent.Sequence);
    }
}
