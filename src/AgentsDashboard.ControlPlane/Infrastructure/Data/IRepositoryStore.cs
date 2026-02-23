namespace AgentsDashboard.ControlPlane.Infrastructure.Data;

public interface IRepositoryStore
{
    Task<RepositoryDocument> CreateRepositoryAsync(CreateRepositoryRequest request, CancellationToken cancellationToken);
    Task<List<RepositoryDocument>> ListRepositoriesAsync(CancellationToken cancellationToken);
    Task<RepositoryDocument?> GetRepositoryAsync(string repositoryId, CancellationToken cancellationToken);
    Task<RepositoryDocument?> UpdateRepositoryAsync(string repositoryId, UpdateRepositoryRequest request, CancellationToken cancellationToken);
    Task<RepositoryDocument?> UpdateRepositoryTaskDefaultsAsync(string repositoryId, UpdateRepositoryTaskDefaultsRequest request, CancellationToken cancellationToken);
    Task<RepositoryDocument?> UpdateRepositoryGitStateAsync(string repositoryId, RepositoryGitStatus gitStatus, CancellationToken cancellationToken);
    Task<RepositoryDocument?> TouchRepositoryAsync(string repositoryId, CancellationToken cancellationToken);
    Task<bool> DeleteRepositoryAsync(string repositoryId, CancellationToken cancellationToken);

    Task<List<InstructionFile>> GetRepositoryInstructionFilesAsync(string repositoryId, CancellationToken cancellationToken);
    Task<RepositoryDocument?> UpdateRepositoryInstructionFilesAsync(string repositoryId, List<InstructionFile> instructionFiles, CancellationToken cancellationToken);
    Task<List<RepositoryInstructionDocument>> GetInstructionsAsync(string repositoryId, CancellationToken cancellationToken);
    Task<RepositoryInstructionDocument?> GetInstructionAsync(string instructionId, CancellationToken cancellationToken);
    Task<RepositoryInstructionDocument> UpsertInstructionAsync(string repositoryId, string? instructionId, CreateRepositoryInstructionRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteInstructionAsync(string instructionId, CancellationToken cancellationToken);

    Task<HarnessProviderSettingsDocument?> GetHarnessProviderSettingsAsync(string repositoryId, string harness, CancellationToken cancellationToken);
    Task<HarnessProviderSettingsDocument> UpsertHarnessProviderSettingsAsync(string repositoryId, string harness, string model, double temperature, int maxTokens, Dictionary<string, string>? additionalSettings, CancellationToken cancellationToken);

    Task<PromptSkillDocument> CreatePromptSkillAsync(CreatePromptSkillRequest request, CancellationToken cancellationToken);
    Task<List<PromptSkillDocument>> ListPromptSkillsAsync(string repositoryId, bool includeGlobal, CancellationToken cancellationToken);
    Task<PromptSkillDocument?> GetPromptSkillAsync(string skillId, CancellationToken cancellationToken);
    Task<PromptSkillDocument?> UpdatePromptSkillAsync(string skillId, UpdatePromptSkillRequest request, CancellationToken cancellationToken);
    Task<bool> DeletePromptSkillAsync(string skillId, CancellationToken cancellationToken);

    Task<RunSessionProfileDocument> CreateRunSessionProfileAsync(CreateRunSessionProfileRequest request, CancellationToken cancellationToken);
    Task<List<RunSessionProfileDocument>> ListRunSessionProfilesAsync(string repositoryId, bool includeGlobal, CancellationToken cancellationToken);
    Task<RunSessionProfileDocument?> GetRunSessionProfileAsync(string sessionProfileId, CancellationToken cancellationToken);
    Task<RunSessionProfileDocument?> UpdateRunSessionProfileAsync(string sessionProfileId, UpdateRunSessionProfileRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteRunSessionProfileAsync(string sessionProfileId, CancellationToken cancellationToken);

    Task UpsertProviderSecretAsync(string repositoryId, string provider, string encryptedValue, CancellationToken cancellationToken);
    Task<List<ProviderSecretDocument>> ListProviderSecretsAsync(string repositoryId, CancellationToken cancellationToken);
    Task<ProviderSecretDocument?> GetProviderSecretAsync(string repositoryId, string provider, CancellationToken cancellationToken);
    Task<bool> DeleteProviderSecretAsync(string repositoryId, string provider, CancellationToken cancellationToken);

    Task<WebhookRegistration> CreateWebhookAsync(CreateWebhookRequest request, CancellationToken cancellationToken);
    Task<WebhookRegistration?> GetWebhookAsync(string webhookId, CancellationToken cancellationToken);
    Task<List<WebhookRegistration>> ListWebhooksAsync(string repositoryId, CancellationToken cancellationToken);
    Task<WebhookRegistration?> UpdateWebhookAsync(string webhookId, UpdateWebhookRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteWebhookAsync(string webhookId, CancellationToken cancellationToken);
}
