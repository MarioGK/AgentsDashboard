using AgentsDashboard.ControlPlane.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.ControlPlane.Services;

public interface ISqliteVecBootstrapService
{
    bool IsAvailable { get; }
    SqliteVecAvailability Status { get; }
}

public sealed record SqliteVecAvailability(
    bool IsAvailable,
    string? ExtensionPath,
    string? Detail,
    DateTime CheckedAtUtc);

public sealed class SqliteVecBootstrapService(
    IOptions<OrchestratorOptions> orchestratorOptions,
    IBackgroundWorkCoordinator backgroundWorkCoordinator,
    ILogger<SqliteVecBootstrapService> logger) : IHostedService, ISqliteVecBootstrapService
{
    private const string StartupOperationKey = "startup:sqlite-vec-bootstrap";

    private volatile SqliteVecAvailability _status = new(
        IsAvailable: false,
        ExtensionPath: null,
        Detail: "sqlite-vec probe has not run",
        CheckedAtUtc: DateTime.MinValue);

    public bool IsAvailable => _status.IsAvailable;

    public SqliteVecAvailability Status => _status;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var workId = backgroundWorkCoordinator.Enqueue(
            BackgroundWorkKind.SqliteVecBootstrap,
            StartupOperationKey,
            async (workCancellationToken, progress) =>
            {
                progress.Report(new BackgroundWorkSnapshot(
                    WorkId: string.Empty,
                    OperationKey: string.Empty,
                    Kind: BackgroundWorkKind.SqliteVecBootstrap,
                    State: BackgroundWorkState.Running,
                    PercentComplete: 5,
                    Message: "Probing sqlite-vec availability.",
                    StartedAt: null,
                    UpdatedAt: DateTimeOffset.UtcNow,
                    ErrorCode: null,
                    ErrorMessage: null));

                var status = await ProbeAsync(workCancellationToken);
                _status = status;

                if (status.IsAvailable)
                {
                    logger.ZLogInformation(
                        "sqlite-vec available (path: {Path}, detail: {Detail})",
                        status.ExtensionPath ?? "built-in",
                        status.Detail ?? string.Empty);

                    progress.Report(new BackgroundWorkSnapshot(
                        WorkId: string.Empty,
                        OperationKey: string.Empty,
                        Kind: BackgroundWorkKind.SqliteVecBootstrap,
                        State: BackgroundWorkState.Succeeded,
                        PercentComplete: 100,
                        Message: "sqlite-vec probe completed and is available.",
                        StartedAt: null,
                        UpdatedAt: DateTimeOffset.UtcNow,
                        ErrorCode: null,
                        ErrorMessage: null));
                }
                else
                {
                    logger.ZLogInformation(
                        "sqlite-vec unavailable; fallback mode enabled ({Detail})",
                        status.Detail ?? "unknown reason");

                    progress.Report(new BackgroundWorkSnapshot(
                        WorkId: string.Empty,
                        OperationKey: string.Empty,
                        Kind: BackgroundWorkKind.SqliteVecBootstrap,
                        State: BackgroundWorkState.Succeeded,
                        PercentComplete: 100,
                        Message: $"sqlite-vec unavailable; fallback mode enabled ({status.Detail ?? "unknown reason"}).",
                        StartedAt: null,
                        UpdatedAt: DateTimeOffset.UtcNow,
                        ErrorCode: null,
                        ErrorMessage: null));
                }
            },
            dedupeByOperationKey: true,
            isCritical: false);

        logger.ZLogInformation("Queued sqlite-vec bootstrap background work {WorkId}", workId);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private async Task<SqliteVecAvailability> ProbeAsync(CancellationToken cancellationToken)
    {
        var checkedAtUtc = DateTime.UtcNow;

        try
        {
            await using var connection = new SqliteConnection(orchestratorOptions.Value.SqliteConnectionString);
            await connection.OpenAsync(cancellationToken);

            connection.EnableExtensions(true);

            var baselineProbe = await ProbeAvailabilityAsync(connection, cancellationToken);
            if (baselineProbe.IsAvailable)
            {
                return new SqliteVecAvailability(true, null, baselineProbe.Detail, checkedAtUtc);
            }

            var loadErrors = new List<string>();
            foreach (var candidate in BuildExtensionCandidates())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!IsLoadCandidateUsable(candidate))
                {
                    continue;
                }

                if (!TryLoadExtension(connection, candidate, out var loadError))
                {
                    if (!string.IsNullOrWhiteSpace(loadError))
                    {
                        loadErrors.Add($"{candidate}: {loadError}");
                    }

                    continue;
                }

                var postLoadProbe = await ProbeAvailabilityAsync(connection, cancellationToken);
                if (postLoadProbe.IsAvailable)
                {
                    return new SqliteVecAvailability(true, candidate, postLoadProbe.Detail, checkedAtUtc);
                }

                loadErrors.Add($"{candidate}: loaded but vec probe failed");
            }

            var detail = loadErrors.Count == 0
                ? "vec module/function not detected"
                : string.Join(" | ", loadErrors.Take(5));

            return new SqliteVecAvailability(false, null, detail, checkedAtUtc);
        }
        catch (Exception ex)
        {
            return new SqliteVecAvailability(false, null, ex.Message, checkedAtUtc);
        }
    }

    private static async Task<(bool IsAvailable, string? Detail)> ProbeAvailabilityAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        if (await ModuleExistsAsync(connection, "vec0", cancellationToken))
        {
            return (true, "vec0 module");
        }

        if (await ModuleExistsAsync(connection, "vss0", cancellationToken))
        {
            return (true, "vss0 module");
        }

        if (await TryExecuteScalarAsync(connection, "SELECT vec_version();", cancellationToken) is { } version &&
            !string.IsNullOrWhiteSpace(version))
        {
            return (true, $"vec_version={version}");
        }

        return (false, null);
    }

    private static async Task<bool> ModuleExistsAsync(
        SqliteConnection connection,
        string moduleName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM pragma_module_list WHERE name = $name;";
        command.Parameters.AddWithValue("$name", moduleName);

        var scalar = await command.ExecuteScalarAsync(cancellationToken);
        return scalar is long count && count > 0;
    }

    private static async Task<string?> TryExecuteScalarAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return value?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static bool TryLoadExtension(SqliteConnection connection, string candidate, out string? error)
    {
        try
        {
            connection.LoadExtension(candidate);
            error = null;
            return true;
        }
        catch (Exception ex1)
        {
            try
            {
                connection.LoadExtension(candidate, "sqlite3_vec_init");
                error = null;
                return true;
            }
            catch (Exception ex2)
            {
                error = $"{ex1.Message} | {ex2.Message}";
                return false;
            }
        }
    }

    private static bool IsLoadCandidateUsable(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var hasPathSeparator = candidate.Contains(Path.DirectorySeparatorChar) ||
                               candidate.Contains(Path.AltDirectorySeparatorChar);

        if (!hasPathSeparator)
        {
            return true;
        }

        return File.Exists(candidate);
    }

    private static IReadOnlyList<string> BuildExtensionCandidates()
    {
        var candidates = new List<string>();

        var explicitPath = Environment.GetEnvironmentVariable("SQLITE_VEC_EXTENSION_PATH");
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            candidates.Add(explicitPath.Trim());
        }

        var fileNames = OperatingSystem.IsWindows()
            ? new[] { "sqlite_vec.dll", "vec0.dll", "sqlite-vec.dll" }
            : OperatingSystem.IsMacOS()
                ? new[] { "sqlite_vec.dylib", "vec0.dylib", "sqlite-vec.dylib" }
                : new[] { "sqlite_vec.so", "vec0.so", "sqlite-vec.so" };

        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory(),
        };

        foreach (var root in roots)
        {
            foreach (var fileName in fileNames)
            {
                candidates.Add(Path.Combine(root, fileName));
            }
        }

        candidates.Add("sqlite_vec");
        candidates.Add("vec0");
        candidates.Add("sqlite-vec");

        return candidates
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
