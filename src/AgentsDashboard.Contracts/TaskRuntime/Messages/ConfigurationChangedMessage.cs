using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;

[MessagePackObject]

[MessagePackObject]

[MessagePackObject]

[MessagePackObject]

[MessagePackObject]

[MessagePackObject]

[MessagePackObject]

[MessagePackObject]
public sealed record ConfigurationChangedMessage
{
    [Key(0)]
    public required string ConfigurationKey { get; init; }

    [Key(1)]
    public string? NewValue { get; init; }

    [Key(2)]
    public long Timestamp { get; init; }
}
