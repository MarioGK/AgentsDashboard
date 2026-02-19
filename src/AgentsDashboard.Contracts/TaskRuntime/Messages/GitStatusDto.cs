using System.Collections.Generic;
using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;

[MessagePackObject]
public record GitStatusDto
{
    [Key(0)] public string Branch { get; set; } = string.Empty;
    [Key(1)] public bool IsClean { get; init; }
    [Key(2)] public int AheadBy { get; init; }
    [Key(3)] public int BehindBy { get; init; }
    [Key(4)] public List<GitStatusEntryDto> Entries { get; set; } = [];
}
