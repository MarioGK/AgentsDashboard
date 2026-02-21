using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;

namespace AgentsDashboard.ControlPlane;

internal sealed class WarningErrorFileLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private static readonly int NewLineByteCount = Utf8WithoutBom.GetByteCount(Environment.NewLine);

    private readonly ConcurrentDictionary<string, WarningErrorFileLogger> _loggers = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly string _baseLogFilePath;
    private readonly long _maxFileSizeBytes;
    private StreamWriter _writer;
    private int _sequence;
    private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();
    private bool _disposed;

    public WarningErrorFileLoggerProvider(string logFilePath, long maxFileSizeBytes)
    {
        if (string.IsNullOrWhiteSpace(logFilePath))
        {
            throw new ArgumentException("Log file path must be provided.", nameof(logFilePath));
        }

        if (maxFileSizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFileSizeBytes), "Maximum file size must be greater than zero.");
        }

        _baseLogFilePath = logFilePath;
        _maxFileSizeBytes = maxFileSizeBytes;

        var initialTarget = ResolveWritableLogFile(_baseLogFilePath, _maxFileSizeBytes);
        _sequence = initialTarget.Sequence;
        _writer = CreateWriter(initialTarget.Path);
    }

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

            RotateFileIfNeeded(message);
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

    private void RotateFileIfNeeded(string message)
    {
        var currentLength = _writer.BaseStream.Length;
        var messageBytes = Utf8WithoutBom.GetByteCount(message) + NewLineByteCount;
        if (currentLength + messageBytes <= _maxFileSizeBytes)
        {
            return;
        }

        _writer.Dispose();
        var nextTarget = ResolveWritableLogFile(_baseLogFilePath, _maxFileSizeBytes, _sequence + 1);
        _sequence = nextTarget.Sequence;
        _writer = CreateWriter(nextTarget.Path);
    }

    private static (string Path, int Sequence) ResolveWritableLogFile(string baseLogFilePath, long maxFileSizeBytes, int startingSequence = 0)
    {
        var sequence = Math.Max(0, startingSequence);
        while (true)
        {
            var candidatePath = BuildLogFilePath(baseLogFilePath, sequence);
            if (!File.Exists(candidatePath) || new FileInfo(candidatePath).Length < maxFileSizeBytes)
            {
                return (candidatePath, sequence);
            }

            sequence++;
        }
    }

    private static string BuildLogFilePath(string baseLogFilePath, int sequence)
    {
        if (sequence <= 0)
        {
            return baseLogFilePath;
        }

        var directoryPath = Path.GetDirectoryName(baseLogFilePath);
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(baseLogFilePath);
        var extension = Path.GetExtension(baseLogFilePath);
        var rolledFileName = string.IsNullOrEmpty(extension)
            ? $"{fileNameWithoutExtension}.{sequence:D4}"
            : $"{fileNameWithoutExtension}.{sequence:D4}{extension}";

        return string.IsNullOrEmpty(directoryPath)
            ? rolledFileName
            : Path.Combine(directoryPath, rolledFileName);
    }

    private static StreamWriter CreateWriter(string logFilePath)
    {
        var directoryPath = Path.GetDirectoryName(logFilePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var fileStream = new FileStream(
            logFilePath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite);

        return new StreamWriter(fileStream, Utf8WithoutBom)
        {
            AutoFlush = true
        };
    }
}
