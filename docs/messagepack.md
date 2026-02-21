# MessagePack for C#

**Extremely Fast MessagePack Serializer** for C#/.NET, with built-in LZ4 compression and AOT source generator support.

- **GitHub**: https://github.com/MessagePack-CSharp/MessagePack-CSharp
- **NuGet**: `MessagePack`
- **License**: MIT
- **Author**: neuecc (Cysharp)

## Overview

MessagePack for C# is a high-performance binary serializer that is:
- **10x faster** than MsgPack-Cli
- **Outperforms** other C# serializers (JSON.NET, protobuf-net, etc.)
- **Built-in LZ4 compression** for ultra-fast serialization with small binary footprint
- **AOT-safe** with source generators for Unity/Xamarin support

Performance is critical for games, distributed computing, microservices, and data caches.

## Platform Support

- .NET 8+ (fully optimized)
- .NET Standard 2.0 / 2.1
- .NET Framework
- Unity 2022.3.12f1+ (with IL2CPP via Source Generator)
- Xamarin

## Installation

```bash
dotnet add package MessagePack
```

Extension packages:

```bash
dotnet add package MessagePack.ReactiveProperty
dotnet add package MessagePack.UnityShims
dotnet add package MessagePack.AspNetCoreMvcFormatter
```

## Quick Start

Define a class/struct with `[MessagePackObject]` and annotate serializable members with `[Key]`:

```csharp
using MessagePack;

[MessagePackObject]
public class MyClass
{
    [Key(0)]
    public int Age { get; set; }

    [Key(1)]
    public string FirstName { get; set; }

    [Key(2)]
    public string LastName { get; set; }

    [IgnoreMember]
    public string FullName => FirstName + LastName;
}
```

Serialize and deserialize:

```csharp
var mc = new MyClass
{
    Age = 99,
    FirstName = "John",
    LastName = "Doe",
};

// Serialize
byte[] bytes = MessagePackSerializer.Serialize(mc);

// Deserialize
MyClass mc2 = MessagePackSerializer.Deserialize<MyClass>(bytes);

// Convert to JSON for debugging
var json = MessagePackSerializer.ConvertToJson(bytes);
// Output: [99,"John","Doe"]
Console.WriteLine(json);
```

## Key Types

### Indexed Keys vs String Keys

```csharp
// Indexed keys - faster, smaller binary (serialized as array)
[MessagePackObject]
public class Sample1
{
    [Key(0)]
    public int Foo { get; set; }
    [Key(1)]
    public int Bar { get; set; }
}
// Result: [10, 20]

// String keys - better debugging, interoperability (serialized as map)
[MessagePackObject]
public class Sample2
{
    [Key("foo")]
    public int Foo { get; set; }
    [Key("bar")]
    public int Bar { get; set; }
}
// Result: {"foo":10,"bar":20}

// Key as property name (contractless-style)
[MessagePackObject(keyAsPropertyName: true)]
public class Sample3
{
    public int Foo { get; set; }

    [IgnoreMember]
    public int Bar { get; set; }
}
// Result: {"Foo":10}
```

**Recommendation**: Use indexed keys for maximum performance and compact binary size.

## Built-in Supported Types

- Primitives (`int`, `string`, etc.), `Enum`s, `Nullable<>`, `Lazy<>`
- `TimeSpan`, `DateTime`, `DateTimeOffset`
- `Guid`, `Uri`, `Version`, `StringBuilder`
- `BigInteger`, `Complex`, `Half`
- Arrays (`[]`, `[,]`, etc.), `ArraySegment<>`, `BitArray`
- `KeyValuePair<,>`, `Tuple<,...>`, `ValueTuple<,...>`
- Collections: `List<>`, `Dictionary<,>`, `HashSet<>`, `Queue<>`, `Stack<>`
- Concurrent collections: `ConcurrentBag<>`, `ConcurrentQueue<>`, `ConcurrentDictionary<,>`
- Immutable collections (`ImmutableList<>`, etc.)
- Custom `ICollection<>` / `IDictionary<,>` implementations with parameterless constructor

## Object Serialization

### Contractless Serialization

For JSON.NET-like usage without attributes:

```csharp
public class ContractlessSample
{
    public int MyProperty1 { get; set; }
    public int MyProperty2 { get; set; }
}

var data = new ContractlessSample { MyProperty1 = 99, MyProperty2 = 9999 };
var bin = MessagePackSerializer.Serialize(
    data,
    MessagePack.Resolvers.ContractlessStandardResolver.Options);

// {"MyProperty1":99,"MyProperty2":9999}
Console.WriteLine(MessagePackSerializer.ConvertToJson(bin));
```

### Private Member Serialization

```csharp
[MessagePackObject]
public class PrivateSample
{
    [Key(0)]
    int x;

    public void SetX(int v) => x = v;
    public int GetX() => x;
}

var data = new PrivateSample();
data.SetX(9999);

var bin = MessagePackSerializer.Serialize(
    data,
    MessagePack.Resolvers.DynamicObjectResolverAllowPrivate.Options);
```

### Immutable Objects

```csharp
[MessagePackObject]
public struct Point
{
    [Key(0)]
    public readonly int X;
    [Key(1)]
    public readonly int Y;

    public Point(int x, int y)
    {
        X = x;
        Y = y;
    }
}

// Automatically chooses best-matched constructor
var point = MessagePackSerializer.Deserialize<Point>(bin);
```

### C# 9 Record Types

```csharp
// Key as property name
[MessagePackObject(true)]
public record Point(int X, int Y);

// Explicit key indices
[MessagePackObject]
public record Point([property: Key(0)] int X, [property: Key(1)] int Y);
```

### Serialization Callbacks

```csharp
[MessagePackObject]
public class SampleCallback : IMessagePackSerializationCallbackReceiver
{
    [Key(0)]
    public int Key { get; set; }

    public void OnBeforeSerialize()
    {
        // Called before serialization
    }

    public void OnAfterDeserialize()
    {
        // Called after deserialization
    }
}
```

## Union (Polymorphic Serialization)

Serialize interface/abstract class-typed objects:

```csharp
[Union(0, typeof(FooClass))]
[Union(1, typeof(BarClass))]
public interface IUnionSample
{
}

[MessagePackObject]
public class FooClass : IUnionSample
{
    [Key(0)]
    public int XYZ { get; set; }
}

[MessagePackObject]
public class BarClass : IUnionSample
{
    [Key(0)]
    public string OPQ { get; set; }
}

// Usage
IUnionSample data = new FooClass { XYZ = 999 };
var bin = MessagePackSerializer.Serialize(data);
var reData = MessagePackSerializer.Deserialize<IUnionSample>(bin);

// Union serialized as [key, object]: [0, [999]]
```

## LZ4 Compression

Built-in LZ4 compression for compact binaries:

```csharp
var lz4Options = MessagePackSerializerOptions.Standard
    .WithCompression(MessagePackCompression.Lz4BlockArray);

byte[] compressed = MessagePackSerializer.Serialize(obj, lz4Options);
var deserialized = MessagePackSerializer.Deserialize<T>(compressed, lz4Options);
```

| Mode | Description |
|------|-------------|
| `Lz4Block` | Single block, best compression ratio |
| `Lz4BlockArray` | Array of blocks, avoids LOH (recommended) |

Both modes can deserialize each other. `None` cannot decompress compressed data.

## High-Level API

| Method | Description |
|--------|-------------|
| `Serialize<T>` | Serialize object to MessagePack binary |
| `Deserialize<T>` | Deserialize binary to object |
| `SerializeToJson` | Serialize object to JSON (debugging) |
| `ConvertToJson` | Convert MessagePack binary to JSON |
| `ConvertFromJson` | Convert JSON to MessagePack binary |

```csharp
// Basic usage
byte[] bytes = MessagePackSerializer.Serialize(obj);
T obj = MessagePackSerializer.Deserialize<T>(bytes);

// With options
byte[] bytes = MessagePackSerializer.Serialize(obj, options);

// Async Stream API
await MessagePackSerializer.SerializeAsync(stream, obj, cancellationToken);
T obj = await MessagePackSerializer.DeserializeAsync<T>(stream, cancellationToken);

// Multiple structures in one stream
using var reader = new MessagePackStreamReader(stream);
while (await reader.ReadAsync(cancellationToken) is ReadOnlySequence<byte> msgpack)
{
    var item = MessagePackSerializer.Deserialize<T>(msgpack);
}
```

## Low-Level API (Custom Formatters)

Implement `IMessagePackFormatter<T>` for custom serialization:

```csharp
public class FileInfoFormatter : IMessagePackFormatter<FileInfo>
{
    public void Serialize(ref MessagePackWriter writer, FileInfo value, MessagePackSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNil();
            return;
        }
        writer.Write(value.FullName);
    }

    public FileInfo Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil())
        {
            return null;
        }

        options.Security.DepthStep(ref reader);
        var path = reader.ReadString();
        reader.Depth--;

        return new FileInfo(path);
    }
}
```

### Per-Member Custom Formatter

```csharp
[MessagePackObject]
public class MyClass
{
    [Key(0)]
    [MessagePackFormatter(typeof(CustomFormatter))]
    public int MyProperty { get; set; }
}
```

## Resolvers

Resolvers control how types are serialized:

| Resolver | Description |
|----------|-------------|
| `StandardResolver` | Default resolver with attributes |
| `ContractlessStandardResolver` | No attributes required |
| `StandardResolverAllowPrivate` | Standard + private members |
| `ContractlessStandardResolverAllowPrivate` | Contractless + private |
| `NativeDateTimeResolver` | Preserves `DateTime.Kind` |
| `NativeGuidResolver` | Faster binary Guid (not string) |
| `NativeDecimalResolver` | Faster binary Decimal |
| `CompositeResolver` | Combine multiple resolvers |

### Composite Resolver

```csharp
var resolver = CompositeResolver.Create(
    // Custom formatters first
    new IMessagePackFormatter[] { new MyCustomFormatter() },
    // Then standard resolvers
    new IFormatterResolver[] { StandardResolver.Instance }
);

var options = MessagePackSerializerOptions.Standard.WithResolver(resolver);
MessagePackSerializer.DefaultOptions = options;
```

### Custom Resolver

```csharp
public class MyApplicationResolver : IFormatterResolver
{
    public static readonly IFormatterResolver Instance = new MyApplicationResolver();

    public IMessagePackFormatter<T> GetFormatter<T>() => Cache<T>.Formatter;

    private static class Cache<T>
    {
        public static readonly IMessagePackFormatter<T> Formatter;

        static Cache()
        {
            // Custom formatter lookup logic
        }
    }
}
```

## Security

When deserializing untrusted data, use secure mode:

```csharp
var options = MessagePackSerializerOptions.Standard
    .WithSecurity(MessagePackSecurity.UntrustedData);

T obj = MessagePackSerializer.Deserialize<T>(untrustedData, options);
```

**Warning**: Avoid `Typeless` serializer/formatters for untrusted data.

## Performance Tips

1. **Use indexed keys** (`[Key(0)]`) instead of string keys
2. **Use native resolvers** for Guid/Decimal/DateTime if interoperability isn't needed
3. **Avoid buffer copies** - use `IBufferWriter<byte>` or `ReadOnlyMemory<byte>` directly
4. **Create custom composite resolvers** instead of `CompositeResolver.Create` for library code
5. **Enable source generator** for AOT and fastest startup

### String Interning

For data with repeated strings:

```csharp
[MessagePackObject]
public class ClassOfStrings
{
    [Key(0)]
    [MessagePackFormatter(typeof(StringInterningFormatter))]
    public string InternedString { get; set; }
}

// Or globally
var options = MessagePackSerializerOptions.Standard.WithResolver(
    CompositeResolver.Create(
        new IMessagePackFormatter[] { new StringInterningFormatter() },
        new IFormatterResolver[] { StandardResolver.Instance }));
```

## AOT Code Generation

Source generator automatically creates formatters at compile time:

```csharp
// Generated resolver is automatically included in StandardResolver
// Or use explicitly:
[GeneratedMessagePackResolver]
partial class MyResolver
{
}
```

Assembly attributes for custom formatters:

```csharp
[assembly: MessagePackKnownFormatter(typeof(MyCustomFormatter))]
[assembly: MessagePackAssumedFormattable(typeof(MyCustomType))]
```

## Extensions

### ASP.NET Core MVC Formatter

```csharp
services.AddMvc().AddMvcOptions(options =>
{
    options.OutputFormatters.Add(new MessagePackOutputFormatter(ContractlessStandardResolver.Options));
    options.InputFormatters.Add(new MessagePackInputFormatter(ContractlessStandardResolver.Options));
});
```

### MagicOnion Integration

MagicOnion uses MessagePack for C#-to-C# gRPC communication without IDL:

```csharp
// MagicOnion handles MessagePack serialization automatically
```

## Typeless API

Embed type information for BinaryFormatter-like usage:

```csharp
object obj = new MyClass { Age = 10, FirstName = "John" };

// Serialize with type info embedded
var blob = MessagePackSerializer.Typeless.Serialize(obj);

// Deserialize without specifying type
var result = MessagePackSerializer.Typeless.Deserialize(blob) as MyClass;
```

## Reserved Extension Type Codes

| Code | Type | Used By |
|------|------|---------|
| -1 | DateTime | MessagePack timestamp |
| 30-39 | Arrays | Unity UnsafeBlitFormatter |
| 98 | All | Lz4BlockArray compression |
| 99 | All | Lz4Block compression |
| 100 | object | TypelessFormatter |

Available for custom use: `[0, 30)` and `[120, 127]`

## Resources

- [MessagePack-CSharp GitHub Repository](https://github.com/MessagePack-CSharp/MessagePack-CSharp)
- [MessagePack Specification](https://msgpack.org/)
- [MagicOnion - gRPC with MessagePack](https://github.com/Cysharp/MagicOnion)
