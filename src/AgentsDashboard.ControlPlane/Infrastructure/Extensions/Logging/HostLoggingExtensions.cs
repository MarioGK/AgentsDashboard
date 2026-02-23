using System.IO;
using Microsoft.Extensions.Logging;
using ZLogger;
using ZLogger.Formatters;
using ZLogger.Providers;

namespace AgentsDashboard.ControlPlane.Infrastructure.Extensions.Logging;

internal static class HostLoggingExtensions
{
    private const int MaxLogFileSizeKb = 4 * 1024;
    private const long MaxWarningErrorLogFileSizeBytes = 4L * 1024 * 1024;

    public static ILoggingBuilder AddStructuredContainerLogging(this ILoggingBuilder logging, string serviceName)
    {
        var useAnsi = ShouldEnableAnsi();
        logging.ClearProviders();

        logging.AddFilter<ZLoggerConsoleLoggerProvider>((_, level) => level >= LogLevel.Information);
        logging.AddFilter<ZLoggerRollingFileLoggerProvider>((_, level) => level >= LogLevel.Trace);
        logging.AddFilter<WarningErrorFileLoggerProvider>((_, level) => level >= LogLevel.Warning);

        logging.AddZLoggerConsole(options =>
        {
            options.ConfigureEnableAnsiEscapeCode = useAnsi;
            options.LogToStandardErrorThreshold = LogLevel.None;
            options.IncludeScopes = true;
            options.UsePlainTextFormatter(formatter =>
            {
                formatter.SetPrefixFormatter(
                    $"{0:utc-longdate} [{1}] [{2}] ",
                    (in MessageTemplate template, in LogInfo info) =>
                    {
                        template.Format(
                            info.Timestamp,
                            FormatStatusToken(info.LogLevel, useAnsi),
                            serviceName);
                    });

                formatter.SetSuffixFormatter(
                    $"({0}:{1}) ",
                    (in MessageTemplate template, in LogInfo info) =>
                    {
                        template.Format(info.Category, info.EventId.Id);
                    });
            });
        });

        logging.AddZLoggerRollingFile(
            (timestamp, sequence) => GetLogFilePath(serviceName, timestamp, sequence),
            RollingInterval.Day,
            MaxLogFileSizeKb);
        logging.AddProvider(new WarningErrorFileLoggerProvider(GetErrorLogFilePath(), MaxWarningErrorLogFileSizeBytes));

        return logging;
    }

    private static string GetLogFilePath(string serviceName, DateTimeOffset timestamp, int sequence)
    {
        var logsDirectory = RepositoryPathResolver.GetDataPath("logs");
        Directory.CreateDirectory(logsDirectory);
        return Path.Combine(logsDirectory, $"{serviceName}-{timestamp:yyyy-MM-dd}.{sequence:D4}.log");
    }

    private static string GetErrorLogFilePath()
    {
        var dataDirectory = RepositoryPathResolver.GetDataPath();
        Directory.CreateDirectory(dataDirectory);
        return Path.Combine(dataDirectory, "errors.log");
    }

    private static bool ShouldEnableAnsi()
    {
        var forceColor = Environment.GetEnvironmentVariable("FORCE_COLOR");
        if (!string.IsNullOrEmpty(forceColor) && !string.Equals(forceColor, "0", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var noColor = Environment.GetEnvironmentVariable("NO_COLOR");
        if (!string.IsNullOrEmpty(noColor))
        {
            return false;
        }

        if (Console.IsOutputRedirected)
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        var term = Environment.GetEnvironmentVariable("TERM");
        return !string.Equals(term, "dumb", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatStatusToken(LogLevel level, bool useAnsi)
    {
        var token = level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRI",
            _ => "NON"
        };

        if (!useAnsi)
        {
            return token;
        }

        var color = level switch
        {
            LogLevel.Trace or LogLevel.Debug => "\u001b[2m",
            LogLevel.Information => "\u001b[36m",
            LogLevel.Warning => "\u001b[33m",
            LogLevel.Error or LogLevel.Critical => "\u001b[31m",
            _ => "\u001b[37m"
        };

        return $"{color}{token}\u001b[0m";
    }
}
