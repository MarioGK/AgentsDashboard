using Microsoft.Extensions.Logging;

namespace AgentsDashboard.TaskRuntimeGateway;

internal static class LoggerObjectExtensions
{
    public static void ZLogInformationObject(this ILogger logger, string message, object? payload)
    {
        logger.ZLogInformation($"{message}: {payload:json}");
    }

    public static void ZLogDebugObject(this ILogger logger, string message, object? payload)
    {
        logger.ZLogDebug($"{message}: {payload:json}");
    }

    public static void ZLogWarningObject(this ILogger logger, string message, object? payload)
    {
        logger.ZLogWarning($"{message}: {payload:json}");
    }

    public static void ZLogWarningObject(this ILogger logger, Exception? exception, string message, object? payload)
    {
        if (exception is null)
        {
            logger.ZLogWarning($"{message}: {payload:json}");
            return;
        }

        logger.ZLogWarning(exception, $"{message}: {payload:json}");
    }

    public static void ZLogErrorObject(this ILogger logger, string message, object? payload)
    {
        logger.ZLogError($"{message}: {payload:json}");
    }

    public static void ZLogErrorObject(this ILogger logger, Exception? exception, string message, object? payload)
    {
        if (exception is null)
        {
            logger.ZLogError($"{message}: {payload:json}");
            return;
        }

        logger.ZLogError(exception, $"{message}: {payload:json}");
    }
}
