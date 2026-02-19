using System.Text;
using System.Text.RegularExpressions;
using LlmTornado;
using LlmTornado.Chat.Models;
using LlmTornado.Code;

namespace AgentsDashboard.ControlPlane.Services;



public sealed record TaskPromptGenerationResult(bool Success, string Prompt, string? Error);
