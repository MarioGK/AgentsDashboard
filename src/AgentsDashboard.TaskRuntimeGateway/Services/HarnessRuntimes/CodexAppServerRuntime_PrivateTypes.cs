using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.TaskRuntime;

namespace AgentsDashboard.TaskRuntimeGateway.Services.HarnessRuntimes;

public sealed partial class CodexAppServerRuntime
    : IHarnessRuntime
{
    private sealed record TurnCompletion(string ThreadId, string TurnId, string Status, string Error);
}
