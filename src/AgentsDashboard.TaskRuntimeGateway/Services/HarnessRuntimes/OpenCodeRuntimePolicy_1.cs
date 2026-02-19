using System.Text.RegularExpressions;
using AgentsDashboard.Contracts.TaskRuntime;

namespace AgentsDashboard.TaskRuntimeGateway.Services.HarnessRuntimes;

internal enum OpenCodeRuntimeMode
{
    Default = 0,
    Plan = 1,
    Review = 2
}



internal sealed record OpenCodeRuntimePolicy(
    OpenCodeRuntimeMode Mode,
    string Agent,
    string? SystemPrompt,
    IReadOnlyList<OpenCodePermissionRule>? SessionPermissionRules);
