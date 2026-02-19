using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.ControlPlane.Services;

public interface IHarnessOutputParserService
{
    ParsedHarnessOutput Parse(string? outputJson, IReadOnlyList<RunLogEvent>? runLogs = null);
}






public sealed record ParsedOutputSection(
    string Key,
    string Title,
    string Content,
    IReadOnlyList<ParsedOutputField> Fields);
