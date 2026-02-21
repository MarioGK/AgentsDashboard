# ZLogger

**Zero Allocation Text/Structured Logger** for .NET and Unity, with StringInterpolation and Source Generator, built on top of `Microsoft.Extensions.Logging`.

- **GitHub**: https://github.com/Cysharp/ZLogger
- **NuGet**: `ZLogger`
- **License**: MIT
- **Author**: Cysharp

## Overview

ZLogger is a high-performance logging library that minimizes memory allocations through:

- **UTF8 Native**: Directly outputs UTF8 from input to output, avoiding encoding overhead
- **String Interpolation**: Full adoption of C# 10 interpolated strings
- **Source Generator**: `ZLoggerMessage` for compile-time log method generation
- **Structured Logging**: Supports JSON and MessagePack formats
- **Async Buffering**: High-performance async processing for I/O operations

Built directly on `Microsoft.Extensions.Logging`, eliminating the bridge overhead required by other loggers.

## Platform Support

- .NET 8+ (fully optimized)
- .NET Standard 2.0 / 2.1
- .NET 6 / 7
- Unity 2022.2+

## Installation

```bash
dotnet add package ZLogger
```

For MessagePack structured logging:

```bash
dotnet add package ZLogger.MessagePack
```

## Getting Started

### ASP.NET Core

```csharp
using ZLogger;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddZLoggerConsole();
```

### Generic Host

```csharp
using ZLogger;

using var factory = LoggerFactory.Create(logging =>
{
    logging.SetMinimumLevel(LogLevel.Trace);
    logging.AddZLoggerConsole();
});

var logger = factory.CreateLogger("Program");
var name = "John";
var age = 33;

logger.ZLogInformation($"Hello my name is {name}, {age} years old.");
```

## Logging Methods

Use `ZLog*` methods with string interpolation for high-performance logging:

```csharp
public class MyClass(ILogger<MyClass> logger)
{
    public void Foo(string name, string city, int age)
    {
        // Plain text: Hello, Bill lives in Kumamoto 21 years old.
        // JSON: {"Timestamp":"...","LogLevel":"Information","Category":"MyClass","Message":"Hello, Bill lives in Kumamoto 21 years old.","name":"Bill","city":"Kumamoto","age":21}
        logger.ZLogInformation($"Hello, {name} lives in {city} {age} years old.");

        // Explicit property name with @ prefix
        logger.ZLogInformation($"Hello, {name:@user-name} id:{100:@id} {age} years old.");

        // Serialize object as JSON with :json format
        var user = new User(1, "Alice");
        logger.ZLogInformation($"user: {user:json}");

        // Combine @ name with format string
        // {"date":"2023-12-19T11:25:34.3642389+09:00"}
        logger.ZLogDebug($"Today is {DateTime.Now:@date:yyyy-MM-dd}.");
    }
}
```

All logging methods mirror `Microsoft.Extensions.Logging.LoggerExtensions` with a `Z` prefix:
- `ZLogTrace`
- `ZLogDebug`
- `ZLogInformation`
- `ZLogWarning`
- `ZLogError`
- `ZLogCritical`

## Providers

| Provider | Extension | Description |
|----------|-----------|-------------|
| Console | `AddZLoggerConsole()` | Standard output |
| File | `AddZLoggerFile(path)` | Single file append |
| RollingFile | `AddZLoggerRollingFile()` | Rotating files by date/size |
| Stream | `AddZLoggerStream(stream)` | Any `System.IO.Stream` |
| InMemory | `AddZLoggerInMemory()` | In-memory string events |
| LogProcessor | `AddZLoggerLogProcessor()` | Custom `IAsyncLogProcessor` |

### Provider Showcase

```csharp
builder.Logging
    .ClearProviders()
    .SetMinimumLevel(LogLevel.Trace)
    .AddZLoggerConsole()
    .AddZLoggerFile("/path/to/file.log")
    .AddZLoggerRollingFile(options =>
    {
        options.FilePathSelector = (timestamp, sequenceNumber) =>
            $"logs/{timestamp.ToLocalTime():yyyy-MM-dd}_{sequenceNumber:000}.log";
        options.RollingInterval = RollingInterval.Day;
        options.RollingSizeKB = 1024;
    })
    .AddZLoggerInMemory(processor =>
    {
        processor.MessageReceived += msg => Console.WriteLine(msg);
    })
    .AddZLoggerStream(stream);
```

### Console Options

| Option | Description | Default |
|--------|-------------|---------|
| `OutputEncodingToUtf8` | Set `Console.OutputEncoding = UTF8Encoding(false)` | `true` |
| `ConfigureEnableAnsiEscapeCode` | Enable ANSI escape codes | `false` |
| `LogToStandardErrorThreshold` | Route logs >= level to stderr | `LogLevel.None` |

### File Options

| Option | Description | Default |
|--------|-------------|---------|
| `fileShared` | Exclusive control for multi-process writes | `false` |

### RollingFile Options

| Option | Description |
|--------|-------------|
| `FilePathSelector` | Func to construct file path (timestamp, sequence) |
| `RollingInterval` | Auto-rotate interval (Day, Hour, Minute, etc.) |
| `RollingSizeKB` | Max file size before rotation |
| `fileShared` | Multi-process exclusive control |

## Formatters

### PlainText (Default)

```csharp
logging.AddZLoggerConsole(options =>
{
    options.UsePlainTextFormatter(formatter =>
    {
        // Format: "2023-12-01 16:41:55.775|Information|This is log message. (MyNamespace.MyApp)"
        formatter.SetPrefixFormatter($"{0}|{1}|",
            (in MessageTemplate template, in LogInfo info) =>
                template.Format(info.Timestamp, info.LogLevel));
        formatter.SetSuffixFormatter($" ({0})",
            (in MessageTemplate template, in LogInfo info) =>
                template.Format(info.Category));
        formatter.SetExceptionFormatter((writer, ex) =>
            Utf8StringInterpolation.Utf8String.Format(writer, $"{ex.Message}"));
    });
});
```

LogLevel format specifiers:
- `:short` - 3-character notation (`TRC`, `DBG`, `INF`, `WRN`, `ERR`, `CRI`, `NON`)

Timestamp format specifiers:
- `local`, `local-longdate`, `longdate` - Local time (default)
- `utc`, `utc-longdate` - UTC time
- `datetime`, `local-datetime`, `utc-datetime` - Full datetime
- `dateonly`, `local-dateonly`, `utc-dateonly` - Date only
- `timeonly`, `local-timeonly`, `utc-timeonly` - Time only

### JSON

```csharp
logging.AddZLoggerConsole(options =>
{
    options.UseJsonFormatter(formatter =>
    {
        formatter.IncludeProperties = IncludeProperties.ParameterKeyValues;
    });
});
```

| Property | Description |
|----------|-------------|
| `JsonPropertyNames` | Customize key names in output JSON |
| `IncludeProperties` | Flags for properties to output |
| `JsonSerializerOptions` | System.Text.Json options |
| `AdditionalFormatter` | Custom additional properties |
| `PropertyKeyValuesObjectName` | Nest key/values under specified key |
| `KeyNameMutator` | Auto-convert key names |
| `UseUtcTimestamp` | Output UTC timestamp |

#### KeyNameMutator Options

| Mutator | Description |
|---------|-------------|
| `LastMemberName` | Last member name only |
| `LowerFirstCharacter` | First char to lowercase |
| `UpperFirstCharacter` | First char to uppercase |
| `LastMemberNameLowerFirstCharacter` | Last member + lowercase first |
| `LastMemberNameUpperFirstCharacter` | Last member + uppercase first |

### MessagePack

```csharp
logging.AddZLoggerFile("log.bin", options =>
{
    options.UseMessagePackFormatter();
});
```

## LogInfo Structure

| Property | Description |
|----------|-------------|
| `Category` | Category name with JsonEncodedText/UTF8 |
| `Timestamp` | Log timestamp |
| `LogLevel` | Microsoft.Extensions.Logging.LogLevel |
| `EventId` | Event ID |
| `Exception` | Exception if provided |
| `ScopeState` | Scope properties (if `IncludeScopes = true`) |
| `ThreadInfo` | Thread info (if `CaptureThreadInfo = true`) |
| `Context` | Additional context |
| `MemberName` | Caller member name |
| `FilePath` | Caller file path |
| `LineNumber` | Caller line number |

## ZLoggerOptions

| Option | Description | Default |
|--------|-------------|---------|
| `IncludeScopes` | Enable `ILogger.BeginScope` | `false` |
| `IsFormatLogImmediatelyInStandardLog` | Immediate stringify for standard Log | `true` |
| `CaptureThreadInfo` | Capture thread information | `false` |
| `TimeProvider` | Custom time provider | `null` (system) |
| `InternalErrorLogger` | Handler for logging errors | `null` (ignore) |

## Source Generator

Achieve maximum performance with compile-time log method generation:

```csharp
public static partial class MyLogger
{
    [ZLoggerMessage(LogLevel.Information, "Bar: {x} {y}")]
    public static partial void Bar(this ILogger<Foo> logger, int x, int y);
}
```

Supports special format specifiers like `:json`.

## Custom LogProcessor

For custom output destinations, implement `IAsyncLogProcessor`:

```csharp
public class SimpleInMemoryLogProcessor : IAsyncLogProcessor
{
    public event Action<string>? OnMessageReceived;

    public void Post(IZLoggerEntry log)
    {
        var msg = log.ToString();
        log.Return(); // Always call Return() - entries are pooled
        OnMessageReceived?.Invoke(msg);
    }

    public ValueTask DisposeAsync() => default;
}
```

For batching scenarios (e.g., HTTP log shipping), extend `BatchingAsyncLogProcessor`.

## Global LoggerFactory

For static logger access without DI:

```csharp
using var host = Host.CreateDefaultBuilder()
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddZLoggerConsole();
    })
    .Build();

var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
LogManager.SetLoggerFactory(loggerFactory, "Global");
await host.RunAsync();

public static class LogManager
{
    static ILogger globalLogger = default!;
    static ILoggerFactory loggerFactory = default!;

    public static void SetLoggerFactory(ILoggerFactory factory, string categoryName)
    {
        loggerFactory = factory;
        globalLogger = factory.CreateLogger(categoryName);
    }

    public static ILogger Logger => globalLogger;
    public static ILogger<T> GetLogger<T>() where T : class => loggerFactory.CreateLogger<T>();
}
```

## Configuration via appsettings.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    },
    "ZLoggerConsoleLoggerProvider": {
      "LogLevel": {
        "Default": "Debug"
      }
    }
  }
}
```

## Multiple Formatters

Add multiple providers for different output formats:

```csharp
// Console: plain text, File: JSON
logging.AddZLoggerConsole(options => options.UsePlainTextFormatter());
logging.AddZLoggerFile("json.log", options => options.UseJsonFormatter());
```

## Resources

- [ZLogger GitHub Repository](https://github.com/Cysharp/ZLogger)
- [ZLogger v1 Introduction](https://neuecc.medium.com/zlogger-zero-allocation-logger-for-net-core-and-unity-d51e675fca76)
- [ZLogger v2 Architecture](https://neuecc.medium.com/zlogger-v2-architecture-leveraging-net-8-to-maximize-performance-2d9733b43789)
