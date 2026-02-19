using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.ControlPlane.Services;

public interface IHarnessOutputParserService
{
    ParsedHarnessOutput Parse(string? outputJson, IReadOnlyList<RunLogEvent>? runLogs = null);
}






public sealed record ParsedHarnessOutput(
    bool ParsedEnvelope,
    string Status,
    string Summary,
    string Error,
    string NormalizedOutputJson,
    IReadOnlyList<ParsedOutputSection> Sections,
    IReadOnlyList<ParsedToolCallGroup> ToolCallGroups,
    IReadOnlyList<ParsedRawStreamItem> RawStream);
