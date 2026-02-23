using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace AgentsDashboard.TaskRuntime.Features.HarnessRuntimes.Services;


internal sealed record OpenCodeSseEvent(
    string Type,
    JsonElement Properties);
