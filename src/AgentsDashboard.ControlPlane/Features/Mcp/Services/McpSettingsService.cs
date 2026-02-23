

namespace AgentsDashboard.ControlPlane.Features.Mcp.Services;

public sealed class McpSettingsService(
    ISystemStore store,
    McpConfigJsonService jsonService,
    McpSettingsFileService fileService)
{
    public async Task<(string Json, McpConfigValidationResult Validation)> LoadSystemConfigAsync(CancellationToken cancellationToken)
    {
        var settings = await store.GetSettingsAsync(cancellationToken);
        var validation = jsonService.ValidateAndFormat(settings.Orchestrator.McpConfigJson);
        var json = validation.FormattedJson;
        return (json, validation);
    }

    public async Task<McpConfigValidationResult> SaveSystemConfigAsync(string? rawJson, CancellationToken cancellationToken)
    {
        var validation = jsonService.ValidateAndFormat(rawJson);
        if (!validation.IsValid)
        {
            return validation;
        }

        var settings = await store.GetSettingsAsync(cancellationToken);
        settings.Orchestrator.McpConfigJson = validation.FormattedJson;
        await store.UpdateSettingsAsync(settings, cancellationToken);
        return validation;
    }

    public string GetSystemFilePath()
    {
        return fileService.GetPath();
    }

    public async Task<(string Json, McpConfigValidationResult Validation)> LoadSystemFileAsync(CancellationToken cancellationToken)
    {
        var content = await fileService.ReadAsync(cancellationToken);
        var validation = jsonService.ValidateAndFormat(content);
        return (validation.FormattedJson, validation);
    }

    public async Task<McpConfigValidationResult> SaveSystemFileAsync(string? rawJson, CancellationToken cancellationToken)
    {
        var validation = jsonService.ValidateAndFormat(rawJson);
        if (!validation.IsValid)
        {
            return validation;
        }

        await fileService.WriteAsync(validation.FormattedJson, cancellationToken);
        return validation;
    }

    public async Task<McpConfigValidationResult> SyncSystemFromFileAsync(CancellationToken cancellationToken)
    {
        var (json, validation) = await LoadSystemFileAsync(cancellationToken);
        if (!validation.IsValid)
        {
            return validation;
        }

        var settings = await store.GetSettingsAsync(cancellationToken);
        settings.Orchestrator.McpConfigJson = json;
        await store.UpdateSettingsAsync(settings, cancellationToken);
        return validation;
    }

    public async Task<McpConfigValidationResult> SyncFileFromSystemAsync(CancellationToken cancellationToken)
    {
        var settings = await store.GetSettingsAsync(cancellationToken);
        var validation = jsonService.ValidateAndFormat(settings.Orchestrator.McpConfigJson);
        if (!validation.IsValid)
        {
            return validation;
        }

        await fileService.WriteAsync(validation.FormattedJson, cancellationToken);
        return validation;
    }
}
