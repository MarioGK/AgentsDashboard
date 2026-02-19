using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.ControlPlane.Services;

public interface IHarnessOutputParserService
{
    ParsedHarnessOutput Parse(string? outputJson, IReadOnlyList<RunLogEvent>? runLogs = null);
}






public sealed class HarnessOutputParserService : IHarnessOutputParserService
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly Regex s_markdownFenceRegex = new(
        @"^```(?:json|javascript|text|txt)?\s*|\s*```$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex s_toolNameRegex = new(
        @"(?:tool(?:_call| call|_use| use)?|function(?:_call| call)?)\s*(?:name)?\s*[:=]\s*([a-zA-Z0-9_.:-]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex s_toolIdRegex = new(
        @"(?:tool[_-]?call[_-]?id|call[_-]?id|id)\s*[:=]\s*([a-zA-Z0-9_-]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const int MaxRawStreamEntries = 4000;

    public ParsedHarnessOutput Parse(string? outputJson, IReadOnlyList<RunLogEvent>? runLogs = null)
    {
        var normalizedOutputJson = NormalizeJsonCandidate(outputJson);
        var rawStream = BuildRawStream(runLogs ?? []);

        var parsedEnvelope = TryParseEnvelope(normalizedOutputJson, out var envelope);

        var status = parsedEnvelope
            ? NormalizeStatus(envelope.Status)
            : InferStatus(normalizedOutputJson, rawStream);

        var summary = parsedEnvelope
            ? NormalizeText(envelope.Summary)
            : InferSummary(normalizedOutputJson, rawStream);

        var error = parsedEnvelope
            ? NormalizeText(envelope.Error)
            : InferError(normalizedOutputJson, rawStream);

        var sections = parsedEnvelope
            ? BuildEnvelopeSections(envelope, normalizedOutputJson)
            : BuildJsonSections(normalizedOutputJson, summary, error);

        var toolCallGroups = BuildToolCallGroups(rawStream, parsedEnvelope ? envelope : null);

        return new ParsedHarnessOutput(
            parsedEnvelope,
            status,
            summary,
            error,
            normalizedOutputJson,
            sections,
            toolCallGroups,
            rawStream);
    }

    private static IReadOnlyList<ParsedOutputSection> BuildEnvelopeSections(HarnessResultEnvelope envelope, string normalizedOutputJson)
    {
        var sections = new List<ParsedOutputSection>();

        if (!string.IsNullOrWhiteSpace(envelope.Summary))
        {
            sections.Add(new ParsedOutputSection(
                "summary",
                "Summary",
                NormalizeText(envelope.Summary),
                []));
        }

        if (!string.IsNullOrWhiteSpace(envelope.Error))
        {
            sections.Add(new ParsedOutputSection(
                "error",
                "Error",
                NormalizeText(envelope.Error),
                []));
        }

        if (envelope.Actions.Count > 0)
        {
            var actionLines = new List<string>(envelope.Actions.Count);
            var actionFields = new List<ParsedOutputField>(envelope.Actions.Count * 3);

            for (var index = 0; index < envelope.Actions.Count; index++)
            {
                var action = envelope.Actions[index];
                var line = $"{action.Type}: {action.Description}".Trim();
                if (!string.IsNullOrWhiteSpace(action.Target))
                {
                    line = $"{line} -> {action.Target}";
                }

                actionLines.Add(line);
                actionFields.Add(new ParsedOutputField($"{index + 1}.type", action.Type));
                actionFields.Add(new ParsedOutputField($"{index + 1}.description", action.Description));
                actionFields.Add(new ParsedOutputField($"{index + 1}.target", action.Target));
            }

            sections.Add(new ParsedOutputSection(
                "actions",
                "Actions",
                string.Join(Environment.NewLine, actionLines),
                actionFields));
        }

        if (envelope.Artifacts.Count > 0)
        {
            var artifactFields = envelope.Artifacts
                .Select((artifact, index) => new ParsedOutputField((index + 1).ToString(), artifact))
                .ToList();

            sections.Add(new ParsedOutputSection(
                "artifacts",
                "Artifacts",
                string.Join(Environment.NewLine, envelope.Artifacts),
                artifactFields));
        }

        if (envelope.Metrics.Count > 0)
        {
            var metricFields = envelope.Metrics
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Select(x => new ParsedOutputField(x.Key, x.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)))
                .ToList();

            sections.Add(new ParsedOutputSection(
                "metrics",
                "Metrics",
                string.Join(Environment.NewLine, metricFields.Select(x => $"{x.Key}: {x.Value}")),
                metricFields));
        }

        if (envelope.Metadata.Count > 0)
        {
            var metadataFields = envelope.Metadata
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Select(x => new ParsedOutputField(x.Key, x.Value))
                .ToList();

            sections.Add(new ParsedOutputSection(
                "metadata",
                "Metadata",
                string.Join(Environment.NewLine, metadataFields.Select(x => $"{x.Key}: {x.Value}")),
                metadataFields));
        }

        if (!string.IsNullOrWhiteSpace(normalizedOutputJson))
        {
            sections.Add(new ParsedOutputSection(
                "raw_json",
                "Raw JSON",
                normalizedOutputJson,
                []));
        }

        return sections;
    }

    private static IReadOnlyList<ParsedOutputSection> BuildJsonSections(string normalizedOutputJson, string summary, string error)
    {
        var sections = new List<ParsedOutputSection>();

        if (!string.IsNullOrWhiteSpace(summary))
        {
            sections.Add(new ParsedOutputSection(
                "summary",
                "Summary",
                summary,
                []));
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            sections.Add(new ParsedOutputSection(
                "error",
                "Error",
                error,
                []));
        }

        if (string.IsNullOrWhiteSpace(normalizedOutputJson))
        {
            return sections;
        }

        try
        {
            using var document = JsonDocument.Parse(normalizedOutputJson);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in root.EnumerateObject())
                {
                    if (string.Equals(property.Name, "summary", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(property.Name, "error", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var fields = FlattenJsonObject(property.Value)
                        .Take(40)
                        .ToList();

                    sections.Add(new ParsedOutputSection(
                        property.Name,
                        ToSectionTitle(property.Name),
                        FormatJsonElement(property.Value),
                        fields));
                }
            }
            else
            {
                sections.Add(new ParsedOutputSection(
                    "raw_json",
                    "Raw JSON",
                    normalizedOutputJson,
                    []));
            }
        }
        catch
        {
            sections.Add(new ParsedOutputSection(
                "raw_json",
                "Raw JSON",
                normalizedOutputJson,
                []));
        }

        return sections;
    }

    private static IReadOnlyList<ParsedRawStreamItem> BuildRawStream(IReadOnlyList<RunLogEvent> runLogs)
    {
        if (runLogs.Count == 0)
        {
            return [];
        }

        var orderedLogs = runLogs
            .OrderBy(x => x.TimestampUtc)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToList();

        var raw = new List<ParsedRawStreamItem>();
        var sequence = 0;

        foreach (var log in orderedLogs)
        {
            var lines = SplitLines(log.Message);
            foreach (var line in lines)
            {
                var normalizedLine = NormalizeText(line);
                if (string.IsNullOrWhiteSpace(normalizedLine))
                {
                    continue;
                }

                sequence++;
                var isToolRelated = IsToolRelated(normalizedLine);
                var item = new ParsedRawStreamItem(
                    sequence,
                    log.TimestampUtc,
                    string.IsNullOrWhiteSpace(log.Level) ? "info" : log.Level,
                    ResolveChannel(log.Level, normalizedLine),
                    normalizedLine,
                    isToolRelated);

                if (raw.Count == MaxRawStreamEntries)
                {
                    raw.RemoveAt(0);
                }

                raw.Add(item);
            }
        }

        return raw;
    }

    private static IReadOnlyList<ParsedToolCallGroup> BuildToolCallGroups(
        IReadOnlyList<ParsedRawStreamItem> rawStream,
        HarnessResultEnvelope? envelope)
    {
        var groups = new Dictionary<string, ToolCallAccumulator>(StringComparer.OrdinalIgnoreCase);
        ToolCallAccumulator? lastToolAccumulator = null;

        foreach (var item in rawStream)
        {
            if (TryExtractToolMarker(item.Message, out var toolName, out var toolCallId))
            {
                var key = BuildToolGroupKey(toolName, toolCallId, item.Sequence);
                if (!groups.TryGetValue(key, out var accumulator))
                {
                    accumulator = new ToolCallAccumulator(key, toolName, toolCallId);
                    groups[key] = accumulator;
                }

                accumulator.Entries.Add(item);
                accumulator.LastSequence = item.Sequence;
                lastToolAccumulator = accumulator;
                continue;
            }

            if (item.IsToolRelated &&
                lastToolAccumulator is not null &&
                item.Sequence - lastToolAccumulator.LastSequence <= 3)
            {
                lastToolAccumulator.Entries.Add(item);
                lastToolAccumulator.LastSequence = item.Sequence;
                continue;
            }

            lastToolAccumulator = null;
        }

        if (envelope is not null)
        {
            AppendEnvelopeToolGroups(envelope, groups);
        }

        return groups.Values
            .Where(x => x.Entries.Count > 0)
            .OrderBy(x => x.Entries[0].Sequence)
            .Select(x => new ParsedToolCallGroup(
                x.GroupId,
                x.ToolName,
                x.ToolCallId,
                x.Entries.OrderBy(e => e.Sequence).ToList()))
            .ToList();
    }

    private static void AppendEnvelopeToolGroups(HarnessResultEnvelope envelope, IDictionary<string, ToolCallAccumulator> groups)
    {
        var syntheticSequence = groups.Values
            .SelectMany(x => x.Entries)
            .Select(x => x.Sequence)
            .DefaultIfEmpty(0)
            .Max();

        foreach (var action in envelope.Actions)
        {
            if (!IsToolRelated(action.Type) && !IsToolRelated(action.Description))
            {
                continue;
            }

            syntheticSequence++;
            var toolName = !string.IsNullOrWhiteSpace(action.Type)
                ? action.Type
                : "tool";
            var groupKey = BuildToolGroupKey(toolName, null, syntheticSequence);

            if (!groups.TryGetValue(groupKey, out var accumulator))
            {
                accumulator = new ToolCallAccumulator(groupKey, toolName, null);
                groups[groupKey] = accumulator;
            }

            var content = string.IsNullOrWhiteSpace(action.Target)
                ? action.Description
                : $"{action.Description} -> {action.Target}";

            accumulator.Entries.Add(new ParsedRawStreamItem(
                syntheticSequence,
                DateTime.UtcNow,
                "action",
                "metadata",
                content,
                true));
            accumulator.LastSequence = syntheticSequence;
        }

        foreach (var metadataEntry in envelope.Metadata)
        {
            if (!metadataEntry.Key.Contains("tool", StringComparison.OrdinalIgnoreCase) &&
                !metadataEntry.Value.Contains("tool", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            syntheticSequence++;
            var toolName = metadataEntry.Key;
            var toolCallId = TryExtractToolId(metadataEntry.Value);
            var groupKey = BuildToolGroupKey(toolName, toolCallId, syntheticSequence);

            if (!groups.TryGetValue(groupKey, out var accumulator))
            {
                accumulator = new ToolCallAccumulator(groupKey, toolName, toolCallId);
                groups[groupKey] = accumulator;
            }

            accumulator.Entries.Add(new ParsedRawStreamItem(
                syntheticSequence,
                DateTime.UtcNow,
                "metadata",
                "metadata",
                $"{metadataEntry.Key}: {metadataEntry.Value}",
                true));
            accumulator.LastSequence = syntheticSequence;
        }
    }

    private static string BuildToolGroupKey(string toolName, string? toolCallId, int sequence)
    {
        if (!string.IsNullOrWhiteSpace(toolCallId))
        {
            return $"call:{toolCallId}";
        }

        if (!string.IsNullOrWhiteSpace(toolName))
        {
            return $"tool:{toolName}:{sequence}";
        }

        return $"tool:unknown:{sequence}";
    }

    private static bool TryExtractToolMarker(string message, out string toolName, out string? toolCallId)
    {
        toolName = string.Empty;
        toolCallId = null;

        var trimmed = message.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            try
            {
                using var document = JsonDocument.Parse(trimmed);
                var root = document.RootElement;

                var name =
                    TryGetJsonPropertyIgnoreCase(root, "tool") ??
                    TryGetJsonPropertyIgnoreCase(root, "tool_name") ??
                    TryGetJsonPropertyIgnoreCase(root, "function") ??
                    TryGetJsonPropertyIgnoreCase(root, "function_name") ??
                    TryGetJsonPropertyIgnoreCase(root, "name");

                var id =
                    TryGetJsonPropertyIgnoreCase(root, "tool_call_id") ??
                    TryGetJsonPropertyIgnoreCase(root, "call_id") ??
                    TryGetJsonPropertyIgnoreCase(root, "id");

                if (!string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(id))
                {
                    toolName = name ?? "tool";
                    toolCallId = string.IsNullOrWhiteSpace(id) ? null : id;
                    return true;
                }
            }
            catch
            {
            }
        }

        var nameMatch = s_toolNameRegex.Match(trimmed);
        if (nameMatch.Success)
        {
            toolName = nameMatch.Groups[1].Value;
            toolCallId = TryExtractToolId(trimmed);
            return true;
        }

        if (IsToolRelated(trimmed))
        {
            toolName = "tool";
            toolCallId = TryExtractToolId(trimmed);
            return true;
        }

        return false;
    }

    private static string? TryExtractToolId(string message)
    {
        var idMatch = s_toolIdRegex.Match(message);
        return idMatch.Success ? idMatch.Groups[1].Value : null;
    }

    private static bool TryParseEnvelope(string normalizedOutputJson, out HarnessResultEnvelope envelope)
    {
        envelope = new HarnessResultEnvelope();

        if (string.IsNullOrWhiteSpace(normalizedOutputJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(normalizedOutputJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var hasEnvelopeShape =
                TryGetJsonPropertyIgnoreCase(root, "status") is not null ||
                TryGetJsonPropertyIgnoreCase(root, "summary") is not null ||
                TryGetJsonPropertyIgnoreCase(root, "error") is not null ||
                TryGetJsonPropertyIgnoreCase(root, "actions") is not null ||
                TryGetJsonPropertyIgnoreCase(root, "artifacts") is not null ||
                TryGetJsonPropertyIgnoreCase(root, "metrics") is not null ||
                TryGetJsonPropertyIgnoreCase(root, "metadata") is not null;

            if (!hasEnvelopeShape)
            {
                return false;
            }

            envelope = JsonSerializer.Deserialize<HarnessResultEnvelope>(normalizedOutputJson, s_jsonOptions) ?? new HarnessResultEnvelope();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string InferStatus(string normalizedOutputJson, IReadOnlyList<ParsedRawStreamItem> rawStream)
    {
        var jsonStatus = TryReadScalarProperty(normalizedOutputJson, "status");
        if (!string.IsNullOrWhiteSpace(jsonStatus))
        {
            return NormalizeStatus(jsonStatus);
        }

        if (rawStream.Any(x => x.Level.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                               x.Channel == "stderr"))
        {
            return "failed";
        }

        var completedMessage = rawStream.LastOrDefault(x =>
            x.Level.Contains("completed", StringComparison.OrdinalIgnoreCase) ||
            x.Message.Contains("completed", StringComparison.OrdinalIgnoreCase));

        if (completedMessage is not null)
        {
            if (completedMessage.Message.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                completedMessage.Message.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                return "failed";
            }

            return "succeeded";
        }

        return "unknown";
    }

    private static string InferSummary(string normalizedOutputJson, IReadOnlyList<ParsedRawStreamItem> rawStream)
    {
        var jsonSummary = TryReadScalarProperty(normalizedOutputJson, "summary");
        if (!string.IsNullOrWhiteSpace(jsonSummary))
        {
            return NormalizeText(jsonSummary);
        }

        var completed = rawStream.LastOrDefault(x =>
            x.Level.Contains("completed", StringComparison.OrdinalIgnoreCase) ||
            x.Message.Contains("completed", StringComparison.OrdinalIgnoreCase));

        if (completed is not null)
        {
            return completed.Message;
        }

        var lastMessage = rawStream.LastOrDefault();
        return lastMessage?.Message ?? string.Empty;
    }

    private static string InferError(string normalizedOutputJson, IReadOnlyList<ParsedRawStreamItem> rawStream)
    {
        var jsonError = TryReadScalarProperty(normalizedOutputJson, "error");
        if (!string.IsNullOrWhiteSpace(jsonError))
        {
            return NormalizeText(jsonError);
        }

        var errorMessage = rawStream.LastOrDefault(x =>
            x.Level.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            x.Channel == "stderr");

        return errorMessage?.Message ?? string.Empty;
    }

    private static string? TryReadScalarProperty(string normalizedOutputJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(normalizedOutputJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(normalizedOutputJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return TryGetJsonPropertyIgnoreCase(root, propertyName);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetJsonPropertyIgnoreCase(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => property.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => null,
                _ => property.Value.GetRawText(),
            };
        }

        return null;
    }

    private static IReadOnlyList<ParsedOutputField> FlattenJsonObject(JsonElement element)
    {
        var fields = new List<ParsedOutputField>();

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                fields.Add(new ParsedOutputField(property.Name, FormatJsonElement(property.Value)));
            }

            return fields;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                index++;
                fields.Add(new ParsedOutputField(index.ToString(), FormatJsonElement(item)));
            }

            return fields;
        }

        fields.Add(new ParsedOutputField("value", FormatJsonElement(element)));
        return fields;
    }

    private static string FormatJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => string.Empty,
            JsonValueKind.Object => JsonSerializer.Serialize(element, s_jsonOptions),
            JsonValueKind.Array => JsonSerializer.Serialize(element, s_jsonOptions),
            _ => element.GetRawText(),
        };
    }

    private static string ResolveChannel(string level, string message)
    {
        var normalizedLevel = level?.Trim() ?? string.Empty;

        if (normalizedLevel.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            normalizedLevel.Contains("stderr", StringComparison.OrdinalIgnoreCase) ||
            message.StartsWith("stderr", StringComparison.OrdinalIgnoreCase))
        {
            return "stderr";
        }

        if (normalizedLevel.Contains("log", StringComparison.OrdinalIgnoreCase) ||
            normalizedLevel.Contains("chunk", StringComparison.OrdinalIgnoreCase) ||
            normalizedLevel.Contains("stdout", StringComparison.OrdinalIgnoreCase) ||
            message.StartsWith("stdout", StringComparison.OrdinalIgnoreCase))
        {
            return "stdout";
        }

        return "event";
    }

    private static bool IsToolRelated(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("tool", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("function_call", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("function call", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("tool_use", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("tool use", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("mcp", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> SplitLines(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            yield break;
        }

        using var reader = new StringReader(value);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }

    private static string NormalizeJsonCandidate(string? outputJson)
    {
        if (string.IsNullOrWhiteSpace(outputJson))
        {
            return string.Empty;
        }

        var trimmed = outputJson.Trim();
        trimmed = s_markdownFenceRegex.Replace(trimmed, string.Empty).Trim();
        return trimmed;
    }

    private static string NormalizeText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }

    private static string NormalizeStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "ok" => "succeeded",
            "success" => "succeeded",
            "succeed" => "succeeded",
            "done" => "succeeded",
            "fail" => "failed",
            "failure" => "failed",
            "errored" => "failed",
            _ => normalized,
        };
    }

    private static string ToSectionTitle(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "Section";
        }

        var builder = new StringBuilder(key.Length + 8);
        var previousWasSeparator = true;

        foreach (var ch in key)
        {
            if (ch is '_' or '-' or '.')
            {
                if (!previousWasSeparator)
                {
                    builder.Append(' ');
                    previousWasSeparator = true;
                }

                continue;
            }

            if (previousWasSeparator)
            {
                builder.Append(char.ToUpperInvariant(ch));
            }
            else
            {
                builder.Append(ch);
            }

            previousWasSeparator = false;
        }

        return builder.ToString().Trim();
    }

    private sealed class ToolCallAccumulator(string groupId, string toolName, string? toolCallId)
    {
        public string GroupId { get; } = groupId;
        public string ToolName { get; } = toolName;
        public string? ToolCallId { get; } = toolCallId;
        public List<ParsedRawStreamItem> Entries { get; } = [];
        public int LastSequence { get; set; }
    }
}
