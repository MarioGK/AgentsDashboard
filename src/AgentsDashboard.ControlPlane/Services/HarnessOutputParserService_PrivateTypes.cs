using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.ControlPlane.Services;

public sealed partial class HarnessOutputParserService : IHarnessOutputParserService
{
    private sealed class ToolCallAccumulator(string groupId, string toolName, string? toolCallId)
    {
        public string GroupId { get; } = groupId;
        public string ToolName { get; } = toolName;
        public string? ToolCallId { get; } = toolCallId;
        public List<ParsedRawStreamItem> Entries { get; } = [];
        public int LastSequence { get; set; }
    }
}
