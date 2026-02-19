using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;
using LlmTornado;
using LlmTornado.Code;
using LlmTornado.Embedding.Models;

namespace AgentsDashboard.ControlPlane.Services;

public interface IWorkspaceAiService
{
    Task<WorkspaceAiTextResult> SuggestPromptContinuationAsync(
        string repositoryId,
        string prompt,
        string? context,
        CancellationToken cancellationToken);

    Task<WorkspaceAiTextResult> ImprovePromptAsync(
        string repositoryId,
        string prompt,
        string? context,
        CancellationToken cancellationToken);

    Task<WorkspaceAiTextResult> GeneratePromptFromContextAsync(
        string repositoryId,
        string context,
        CancellationToken cancellationToken);

    Task<WorkspaceAiTextResult> SummarizeRunOutputAsync(
        string repositoryId,
        string outputJson,
        IReadOnlyList<RunLogEvent> runLogs,
        CancellationToken cancellationToken);

    Task<WorkspaceEmbeddingResult> CreateEmbeddingAsync(
        string repositoryId,
        string text,
        CancellationToken cancellationToken);
}



public sealed record WorkspaceEmbeddingResult(
    bool Success,
    string Payload,
    int Dimensions,
    string Model,
    bool UsedFallback,
    bool KeyConfigured,
    string? Message);
