using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;
[MessagePackObject]

public sealed record TaskRuntimeRegistrationRequest
{
    [Key(0)]
    public required string TaskRuntimeId { get; init; }

    [Key(1)]
    public string? Endpoint { get; init; }

    [Key(2)]
    public int MaxSlots { get; init; }

    [Key(3)]
    public string? Version { get; init; }

    [Key(4)]
    public Dictionary<string, string>? Capabilities { get; init; }
}
