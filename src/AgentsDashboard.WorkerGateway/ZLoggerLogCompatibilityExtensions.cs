using System;
using Microsoft.Extensions.Logging;

namespace ZLogger;

internal static class ZLoggerLogCompatibilityExtensions
{
    public static void ZLogTrace(this ILogger logger, string message, params object?[] args) =>
        logger.LogTrace(message, args);

    public static void ZLogTrace(this ILogger logger, Exception? exception, string message, params object?[] args) =>
        logger.LogTrace(exception, message, args);

    public static void ZLogDebug(this ILogger logger, string message, params object?[] args) =>
        logger.LogDebug(message, args);

    public static void ZLogDebug(this ILogger logger, Exception? exception, string message, params object?[] args) =>
        logger.LogDebug(exception, message, args);

    public static void ZLogInformation(this ILogger logger, string message, params object?[] args) =>
        logger.LogInformation(message, args);

    public static void ZLogInformation(this ILogger logger, Exception? exception, string message, params object?[] args) =>
        logger.LogInformation(exception, message, args);

    public static void ZLogWarning(this ILogger logger, string message, params object?[] args) =>
        logger.LogWarning(message, args);

    public static void ZLogWarning(this ILogger logger, Exception? exception, string message, params object?[] args) =>
        logger.LogWarning(exception, message, args);

    public static void ZLogError(this ILogger logger, string message, params object?[] args) =>
        logger.LogError(message, args);

    public static void ZLogError(this ILogger logger, Exception? exception, string message, params object?[] args) =>
        logger.LogError(exception, message, args);

    public static void ZLogCritical(this ILogger logger, string message, params object?[] args) =>
        logger.LogCritical(message, args);

    public static void ZLogCritical(this ILogger logger, Exception? exception, string message, params object?[] args) =>
        logger.LogCritical(exception, message, args);
}
