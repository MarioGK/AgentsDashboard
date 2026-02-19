namespace AgentsDashboard.ControlPlane.Services;

public interface ILiteDbVectorSearchStatusService
{
    bool IsAvailable { get; }
    LiteDbVectorSearchAvailability Status { get; }
}


public sealed record LiteDbVectorSearchAvailability(
    bool IsAvailable,
    string? ExtensionPath,
    string? Detail,
    DateTime CheckedAtUtc);
