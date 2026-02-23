namespace AgentsDashboard.Contracts.Features.Providers.Models.Domain;

































































public sealed class ProviderSecretDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RepositoryId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string EncryptedValue { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
