using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentsDashboard.Contracts.Validation;


public sealed class HarnessEnvelopeValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public static HarnessEnvelopeValidationResult Success() => new() { IsValid = true };

    public static HarnessEnvelopeValidationResult Failure(params string[] errors) =>
        new() { IsValid = false, Errors = errors.ToList() };

    public static HarnessEnvelopeValidationResult Failure(IEnumerable<string> errors) =>
        new() { IsValid = false, Errors = errors.ToList() };
}
