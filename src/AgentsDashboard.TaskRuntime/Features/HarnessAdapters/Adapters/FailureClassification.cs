namespace AgentsDashboard.TaskRuntime.Features.HarnessAdapters.Adapters;

public enum FailureClass
{
    None = 0,
    AuthenticationError = 1,
    Timeout = 2,
    ResourceExhausted = 3,
    InvalidInput = 4,
    ConfigurationError = 5,
    NetworkError = 6,
    PermissionDenied = 7,
    NotFound = 8,
    InternalError = 9,
    Unknown = 99
}

public sealed class FailureClassification
{
    public FailureClass Class { get; init; } = FailureClass.None;
    public string Reason { get; init; } = string.Empty;
    public bool IsRetryable { get; init; }
    public int? SuggestedBackoffSeconds { get; init; }
    public IReadOnlyList<string> RemediationHints { get; init; } = [];

    public static FailureClassification Success() => new();

    public static FailureClassification FromClass(
        FailureClass failureClass,
        string reason,
        bool isRetryable = false,
        int? backoffSeconds = null,
        params string[] hints) => new()
        {
            Class = failureClass,
            Reason = reason,
            IsRetryable = isRetryable,
            SuggestedBackoffSeconds = backoffSeconds,
            RemediationHints = hints
        };
}
