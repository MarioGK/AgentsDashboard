using System.Text;
using System.Text.RegularExpressions;
using LlmTornado;
using LlmTornado.Chat.Models;
using LlmTornado.Code;

namespace AgentsDashboard.ControlPlane.Services;



public sealed record TaskPromptGenerationRequest(
    string RepositoryName,
    string TaskName,
    string Harness,
    string Kind,
    string Command,
    string? CronExpression);
