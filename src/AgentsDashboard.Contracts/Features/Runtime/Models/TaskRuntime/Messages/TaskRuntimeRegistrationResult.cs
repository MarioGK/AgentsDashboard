using MessagePack;

namespace AgentsDashboard.Contracts.Features.Runtime.Models.TaskRuntime.Messages;

[MessagePackObject]

public sealed record TaskRuntimeRegistrationResult
{
    [Key(0)]
    public required bool Success { get; init; }

    [Key(1)]
    public string? AssignedId { get; init; }

    [Key(2)]
    public string? ErrorMessage { get; init; }

    [Key(3)]
    public long ServerTimestamp { get; init; }
}
