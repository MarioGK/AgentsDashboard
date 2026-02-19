using System.Text;
using System.Text.RegularExpressions;
using LlmTornado;
using LlmTornado.Chat.Models;
using LlmTornado.Code;

namespace AgentsDashboard.ControlPlane.Services;



public sealed record LlmTornadoTextResult(bool Success, string Text, string? Error);
