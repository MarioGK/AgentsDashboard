using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace AgentsDashboard.TaskRuntime.Infrastructure.Extensions.Logging;

internal sealed class WarningErrorFileLogger(
    string categoryName,
    Action<string> writeLogLine,
    Func<IExternalScopeProvider> getScopeProvider) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return getScopeProvider().Push(state);
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel >= LogLevel.Warning;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var lineBuilder = new StringBuilder();
        lineBuilder.Append(DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
        lineBuilder.Append(" [");
        lineBuilder.Append(GetStatusToken(logLevel));
        lineBuilder.Append("] [");
        lineBuilder.Append(categoryName);
        lineBuilder.Append("] (");
        lineBuilder.Append(eventId.Id.ToString(CultureInfo.InvariantCulture));
        lineBuilder.Append(") ");
        lineBuilder.Append(formatter(state, exception));

        var scopes = GetScopes();
        if (!string.IsNullOrWhiteSpace(scopes))
        {
            lineBuilder.Append(" | Scope=");
            lineBuilder.Append(scopes);
        }

        if (exception is not null)
        {
            lineBuilder.AppendLine();
            lineBuilder.Append(exception);
        }

        writeLogLine(lineBuilder.ToString());
    }

    private string GetScopes()
    {
        var scopeProvider = getScopeProvider();
        List<string> values = [];

        scopeProvider.ForEachScope(
            static (scope, state) =>
            {
                var text = scope?.ToString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return;
                }

                state.Add(text);
            },
            values);

        if (values.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(" => ", values);
    }

    private static string GetStatusToken(LogLevel level)
    {
        return level switch
        {
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRI",
            _ => "NON"
        };
    }
}
