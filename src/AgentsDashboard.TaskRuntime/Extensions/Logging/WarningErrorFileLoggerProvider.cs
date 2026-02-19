using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;

namespace AgentsDashboard.TaskRuntime;

internal sealed class WarningErrorFileLoggerProvider(string logFilePath) : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentDictionary<string, WarningErrorFileLogger> _loggers = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly StreamWriter _writer = CreateWriter(logFilePath);
    private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();
    private bool _disposed;

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, CreateLoggerCore);
    }

    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        _scopeProvider = scopeProvider;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _writer.Dispose();
        }
    }

    private void WriteLogLine(string message)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _writer.WriteLine(message);
        }
    }

    private WarningErrorFileLogger CreateLoggerCore(string categoryName)
    {
        return new WarningErrorFileLogger(categoryName, WriteLogLine, GetScopeProvider);
    }

    private IExternalScopeProvider GetScopeProvider()
    {
        return _scopeProvider;
    }

    private static StreamWriter CreateWriter(string logFilePath)
    {
        var fileStream = new FileStream(
            logFilePath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite);

        return new StreamWriter(fileStream, new UTF8Encoding(false))
        {
            AutoFlush = true
        };
    }
}
