using Microsoft.Extensions.Logging;

namespace AgentsDashboard.TaskRuntimeGateway;

internal static class LoggerObjectExtensions
{
    public static void LogInformationObject(this ILogger logger, string message, object? payload)
    {
        logger.LogInformation($"{message}: {payload:json}");
    }

    public static void LogDebugObject(this ILogger logger, string message, object? payload)
    {
        logger.LogDebug($"{message}: {payload:json}");
    }

    public static void LogWarningObject(this ILogger logger, string message, object? payload)
    {
        logger.LogWarning($"{message}: {payload:json}");
    }

    public static void LogWarningObject(this ILogger logger, Exception? exception, string message, object? payload)
    {
        if (exception is null)
        {
            logger.LogWarning($"{message}: {payload:json}");
            return;
        }

        logger.LogWarning(exception, $"{message}: {payload:json}");
    }

    public static void LogErrorObject(this ILogger logger, string message, object? payload)
    {
        logger.LogError($"{message}: {payload:json}");
    }

    public static void LogErrorObject(this ILogger logger, Exception? exception, string message, object? payload)
    {
        if (exception is null)
        {
            logger.LogError($"{message}: {payload:json}");
            return;
        }

        logger.LogError(exception, $"{message}: {payload:json}");
    }
}
