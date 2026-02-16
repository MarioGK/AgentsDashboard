using System.IO;
using System.Reflection;
using System.Text.Json;
using AgentsDashboard.Contracts.ControlPlane;
using AgentsDashboard.ControlPlane.Data;
using Grpc.Net.Client;
using MagicOnion.Client;
using Microsoft.Extensions.Configuration;

namespace AgentsDashboard.ControlPlane.Services;

public static class ControlPlaneStoreGatewayClientFactory
{
    private static readonly string[] UrlSchemes = ["http://", "https://"];

    public static IOrchestratorStore Create(IConfiguration configuration)
    {
        var address = ResolveControlPlaneGatewayAddress(configuration);
        var channel = GrpcChannel.ForAddress(address);
        var gateway = MagicOnionClient.Create<IControlPlaneStoreGateway>(channel);
        var proxy = DispatchProxy.Create<IOrchestratorStore, ControlPlaneStoreGatewayClient>();
        var client = (ControlPlaneStoreGatewayClient)(object)proxy;
        client.Configure(gateway);
        return proxy;
    }

    public static string ResolveControlPlaneGatewayAddress(IConfiguration configuration)
    {
        var configured = configuration["Orchestrator:StoreGatewayUrl"];
        if (!string.IsNullOrWhiteSpace(configured))
            return NormalizeGatewayAddress(configured);

        var urls = configuration["ASPNETCORE_URLS"] ?? configuration["urls"];
        if (!string.IsNullOrWhiteSpace(urls))
        {
            var selected = urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(url => UrlSchemes.Any(url.StartsWith));

            if (!string.IsNullOrWhiteSpace(selected))
            {
                return NormalizeGatewayAddress(selected);
            }
        }

        return "http://localhost:8080";
    }

    private static string NormalizeGatewayAddress(string rawAddress)
    {
        var trimmed = rawAddress.Trim();
        try
        {
            var uri = new Uri(trimmed);
            var host = uri.Host;
            if (host is "+" or "*" or "0.0.0.0")
            {
                host = "localhost";
            }

            return new UriBuilder(uri) { Host = host }.Uri.ToString().TrimEnd('/');
        }
        catch
        {
            return trimmed;
        }
    }

    private class ControlPlaneStoreGatewayClient : DispatchProxy
    {
        private static readonly MethodInfo GenericResultInvoke = typeof(ControlPlaneStoreGatewayClient)
            .GetMethod("InvokeWithResultAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web);

        private IControlPlaneStoreGateway? _gateway;

        public void Configure(IControlPlaneStoreGateway gateway) => _gateway = gateway;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
                throw new ArgumentNullException(nameof(targetMethod));

            if (targetMethod.DeclaringType == typeof(object))
            {
                return targetMethod.Invoke(this, args);
            }

            var invocationArgs = args ?? [];
            var cancellationToken = ResolveCancellationToken(targetMethod, invocationArgs);

            if (targetMethod.ReturnType == typeof(Task))
            {
                return InvokeWithoutResultAsync(targetMethod, invocationArgs, cancellationToken);
            }

            if (!targetMethod.ReturnType.IsGenericType || targetMethod.ReturnType.GetGenericTypeDefinition() != typeof(Task<>))
            {
                throw new NotSupportedException("Only async Task-based methods are supported by store gateway proxy.");
            }

            var returnType = targetMethod.ReturnType.GenericTypeArguments[0];
            return GenericResultInvoke.MakeGenericMethod(returnType)
                .Invoke(this, [targetMethod, invocationArgs, cancellationToken])!;
        }

        private static CancellationToken ResolveCancellationToken(MethodInfo method, object?[] args)
        {
            var parameters = method.GetParameters();

            for (var i = 0; i < parameters.Length && i < args.Length; i++)
            {
                if (parameters[i].ParameterType == typeof(CancellationToken) && args[i] is CancellationToken token)
                {
                    return token;
                }
            }

            return CancellationToken.None;
        }

        private async Task InvokeWithoutResultAsync(MethodInfo targetMethod, object?[] args, CancellationToken cancellationToken)
        {
            var response = await InvokeCoreAsync(targetMethod, args, cancellationToken);

            if (!response.Success)
            {
                throw new InvalidOperationException($"Store gateway invoke '{targetMethod.Name}' failed: {response.ErrorMessage}");
            }
        }

        private async Task<T> InvokeWithResultAsync<T>(MethodInfo targetMethod, object?[] args, CancellationToken cancellationToken)
        {
            var response = await InvokeCoreAsync(targetMethod, args, cancellationToken);

            if (!response.Success)
            {
                throw new InvalidOperationException($"Store gateway invoke '{targetMethod.Name}' failed: {response.ErrorMessage}");
            }

            if (response.Payload is null)
            {
                return default!;
            }

            var returnType = typeof(T);

            if (returnType == typeof(Stream) || returnType == typeof(FileStream))
            {
                var path = Path.GetTempFileName();
                await File.WriteAllBytesAsync(path, response.Payload, cancellationToken);

                return (T)(object)new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    32 * 1024,
                    FileOptions.DeleteOnClose);
            }

            if (response.IsBinary)
            {
                if (returnType == typeof(byte[]))
                {
                    return (T)(object)response.Payload;
                }

                throw new InvalidOperationException($"Store gateway response is binary but {returnType.FullName} is not supported for binary deserialization.");
            }

            var value = JsonSerializer.Deserialize(response.Payload, returnType, JsonSerializerOptions);
            if (value is null)
            {
                return default!;
            }

            return (T)value;
        }

        private async Task<ControlPlaneInvocationResponse> InvokeCoreAsync(
            MethodInfo targetMethod,
            object?[] args,
            CancellationToken cancellationToken)
        {
            if (_gateway is null)
                throw new InvalidOperationException("Store gateway proxy is not initialized.");

            var request = await BuildRequestAsync(targetMethod, args, cancellationToken);
            return await _gateway.InvokeAsync(request);
        }

        private async Task<ControlPlaneInvocationRequest> BuildRequestAsync(
            MethodInfo targetMethod,
            object?[] args,
            CancellationToken cancellationToken)
        {
            var parameters = targetMethod.GetParameters();
            if (parameters.Length != args.Length)
            {
                throw new InvalidOperationException("Store gateway dispatch argument mismatch.");
            }

            var serializedArguments = new List<ControlPlaneInvocationArgument>(parameters.Length);

            for (var i = 0; i < parameters.Length; i++)
            {
                var parameterType = parameters[i].ParameterType;
                var argument = args[i];
                var payload = await SerializeArgumentAsync(parameterType, argument, cancellationToken);

                serializedArguments.Add(new ControlPlaneInvocationArgument(parameterType.AssemblyQualifiedName!, payload));
            }

            var expectedStream = targetMethod.ReturnType == typeof(Task<FileStream>) ||
                targetMethod.ReturnType == typeof(Task<Stream>) ||
                parameters.Any(p => typeof(Stream).IsAssignableFrom(p.ParameterType));

            return new ControlPlaneInvocationRequest(
                MethodName: targetMethod.Name,
                ParameterTypeNames: parameters.Select(p => p.ParameterType.AssemblyQualifiedName!).ToArray(),
                Arguments: serializedArguments,
                ExpectsFileStream: expectedStream);
        }

        private static async Task<byte[]?> SerializeArgumentAsync(
            Type parameterType,
            object? argument,
            CancellationToken cancellationToken)
        {
            if (argument is null)
            {
                if (parameterType == typeof(CancellationToken))
                    return null;

                return null;
            }

            if (parameterType == typeof(CancellationToken))
            {
                return null;
            }

            if (typeof(Stream).IsAssignableFrom(parameterType))
            {
                var source = (Stream)argument;
                var copy = new MemoryStream();
                await source.CopyToAsync(copy, cancellationToken);
                return copy.ToArray();
            }

            return JsonSerializer.SerializeToUtf8Bytes(argument, parameterType, JsonSerializerOptions);
        }
    }
}
