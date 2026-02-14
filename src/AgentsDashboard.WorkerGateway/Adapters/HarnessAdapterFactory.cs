using AgentsDashboard.WorkerGateway.Configuration;
using AgentsDashboard.WorkerGateway.Services;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.WorkerGateway.Adapters;

public sealed class HarnessAdapterFactory(
    IOptions<WorkerOptions> options,
    SecretRedactor secretRedactor,
    IServiceProvider serviceProvider)
{
    private readonly Dictionary<string, Func<IHarnessAdapter>> _adapterFactories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["codex"] = () => new CodexAdapter(options, secretRedactor, serviceProvider.GetRequiredService<ILogger<CodexAdapter>>()),
        ["opencode"] = () => new OpenCodeAdapter(options, secretRedactor, serviceProvider.GetRequiredService<ILogger<OpenCodeAdapter>>()),
        ["claude-code"] = () => new ClaudeCodeAdapter(options, secretRedactor, serviceProvider.GetRequiredService<ILogger<ClaudeCodeAdapter>>()),
        ["claude code"] = () => new ClaudeCodeAdapter(options, secretRedactor, serviceProvider.GetRequiredService<ILogger<ClaudeCodeAdapter>>()),
        ["zai"] = () => new ZaiAdapter(options, secretRedactor, serviceProvider.GetRequiredService<ILogger<ZaiAdapter>>()),
    };

    public IHarnessAdapter Create(string harnessName)
    {
        if (string.IsNullOrWhiteSpace(harnessName))
            throw new ArgumentException("Harness name cannot be empty", nameof(harnessName));

        var normalizedName = harnessName.Trim().ToLowerInvariant();

        if (_adapterFactories.TryGetValue(normalizedName, out var factory))
            return factory();

        throw new NotSupportedException($"Harness '{harnessName}' is not supported. Supported harnesses: {string.Join(", ", _adapterFactories.Keys)}");
    }

    public bool IsSupported(string harnessName)
    {
        if (string.IsNullOrWhiteSpace(harnessName))
            return false;

        return _adapterFactories.ContainsKey(harnessName.Trim().ToLowerInvariant());
    }

    public IReadOnlyList<string> GetSupportedHarnesses() => _adapterFactories.Keys.ToList();

    public void RegisterAdapter(string harnessName, Func<IHarnessAdapter> factory)
    {
        if (string.IsNullOrWhiteSpace(harnessName))
            throw new ArgumentException("Harness name cannot be empty", nameof(harnessName));

        var normalizedName = harnessName.Trim().ToLowerInvariant();
        _adapterFactories[normalizedName] = factory ?? throw new ArgumentNullException(nameof(factory));
    }
}
