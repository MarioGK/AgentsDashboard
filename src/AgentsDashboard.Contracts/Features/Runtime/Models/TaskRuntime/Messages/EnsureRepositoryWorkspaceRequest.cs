using MessagePack;

namespace AgentsDashboard.Contracts.Features.Runtime.Models.TaskRuntime.Messages;

[MessagePackObject]
public sealed record EnsureRepositoryWorkspaceRequest
{
    [Key(0)] public string? RepositoryId { get; init; }
    [Key(1)] public required string GitUrl { get; init; }
    [Key(2)] public string? DefaultBranch { get; init; }
    [Key(3)] public string? GitHubToken { get; init; }
    [Key(4)] public bool FetchRemote { get; init; }
    [Key(5)] public string? RepositoryKeyHint { get; init; }
}
