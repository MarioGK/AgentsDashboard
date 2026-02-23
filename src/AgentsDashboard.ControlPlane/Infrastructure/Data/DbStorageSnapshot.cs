namespace AgentsDashboard.ControlPlane.Infrastructure.Data;






public sealed record DbStorageSnapshot(
    string DatabasePath,
    long MainFileBytes,
    long WalFileBytes,
    long TotalBytes,
    bool Exists,
    DateTime MeasuredAtUtc);
