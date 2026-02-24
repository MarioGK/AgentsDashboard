using MessagePack;

namespace AgentsDashboard.Contracts.Features.Runtime.Models.TaskRuntime.Messages;

[MessagePackObject]
public sealed record RuntimeReadinessResult
{
    [Key(0)]
    public bool Success { get; init; }

    [Key(1)]
    public string? ErrorMessage { get; init; }

    [Key(2)]
    public bool AcceptingStreamingConnections { get; init; }

    [Key(3)]
    public DateTimeOffset CheckedAt { get; init; }
}
