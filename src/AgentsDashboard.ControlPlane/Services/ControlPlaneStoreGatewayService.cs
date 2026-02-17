using System.IO;
using System.Reflection;
using System.Text.Json;
using AgentsDashboard.Contracts.ControlPlane;
using AgentsDashboard.ControlPlane.Data;
using MagicOnion;
using MagicOnion.Server;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class ControlPlaneStoreGatewayService(
    OrchestratorStore store,
    ILogger<ControlPlaneStoreGatewayService> logger)
    : ServiceBase<IControlPlaneStoreGateway>, IControlPlaneStoreGateway
{
    private static readonly IReadOnlyDictionary<string, MethodInfo[]> StoreMethodsByName = typeof(OrchestratorStore)
        .GetMethods(BindingFlags.Instance | BindingFlags.Public)
        .GroupBy(method => method.Name)
        .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async UnaryResult<ControlPlaneInvocationResponse> InvokeAsync(
        ControlPlaneInvocationRequest request)
    {
        var cancellationToken = Context.CallContext.CancellationToken;
        var method = ResolveMethod(request);
        if (method is null)
        {
            return new ControlPlaneInvocationResponse(
                Success: false,
                IsBinary: false,
                Payload: null,
                ErrorType: "MethodNotFound",
                ErrorMessage: $"Store method '{request.MethodName}' was not found.");
        }

        var parameters = method.GetParameters();
        if (parameters.Length != request.Arguments.Count)
        {
            return new ControlPlaneInvocationResponse(
                Success: false,
                IsBinary: false,
                Payload: null,
                ErrorType: "ArgumentMismatch",
                ErrorMessage: $"Store method '{request.MethodName}' expects {parameters.Length} parameters but received {request.Arguments.Count}.");
        }

        try
        {
            var invocationArguments = await DeserializeArgumentsAsync(parameters, request).ConfigureAwait(false);
            var result = method.Invoke(store, invocationArguments);
            var returnValue = await UnwrapTaskAsync(result, cancellationToken).ConfigureAwait(false);
            return await BuildResponseAsync(returnValue, cancellationToken).ConfigureAwait(false);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            logger.ZLogError(ex.InnerException, "Store gateway invocation failed for {Method}", request.MethodName);
            return new ControlPlaneInvocationResponse(
                Success: false,
                IsBinary: false,
                Payload: null,
                ErrorType: ex.InnerException.GetType().FullName,
                ErrorMessage: ex.InnerException.Message);
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, "Store gateway invocation failed for {Method}", request.MethodName);
            return new ControlPlaneInvocationResponse(
                Success: false,
                IsBinary: false,
                Payload: null,
                ErrorType: ex.GetType().FullName,
                ErrorMessage: ex.Message);
        }
    }

    private static MethodInfo? ResolveMethod(ControlPlaneInvocationRequest request)
    {
        if (!StoreMethodsByName.TryGetValue(request.MethodName, out var candidates))
        {
            return null;
        }

        var parameterTypes = request.ParameterTypeNames.Select(ResolveParameterType).ToArray();
        if (parameterTypes.Any(type => type is null))
        {
            return null;
        }

        foreach (var candidate in candidates)
        {
            var methodParameters = candidate.GetParameters();
            if (methodParameters.Length != parameterTypes.Length)
            {
                continue;
            }

            var matches = true;
            for (var i = 0; i < methodParameters.Length; i++)
            {
                if (methodParameters[i].ParameterType != parameterTypes[i])
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return candidate;
            }
        }

        return null;
    }

    private static Type? ResolveParameterType(string typeName)
    {
        var resolved = Type.GetType(typeName);
        if (resolved is not null)
        {
            return resolved;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(typeName);
            if (type is not null)
            {
                return type;
            }
        }

        return null;
    }

    private static Task<object?[]> DeserializeArgumentsAsync(
        ParameterInfo[] parameters,
        ControlPlaneInvocationRequest request)
    {
        var arguments = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            arguments[i] = DeserializeArgument(parameters[i].ParameterType, request.Arguments[i]);
        }

        return Task.FromResult(arguments);
    }

    private static object? DeserializeArgument(
        Type parameterType,
        ControlPlaneInvocationArgument argument)
    {
        if (parameterType == typeof(CancellationToken))
        {
            return CancellationToken.None;
        }

        if (parameterType == typeof(Stream) || parameterType == typeof(FileStream) ||
            typeof(Stream).IsAssignableFrom(parameterType))
        {
            if (argument.Payload is null)
            {
                return Stream.Null;
            }

            return new MemoryStream(argument.Payload);
        }

        if (argument.Payload is null)
        {
            return parameterType.IsValueType
                ? Activator.CreateInstance(parameterType)
                : null;
        }

        return JsonSerializer.Deserialize(argument.Payload, parameterType, SerializerOptions);
    }

    private static async Task<object?> UnwrapTaskAsync(object? invocationResult, CancellationToken cancellationToken)
    {
        if (invocationResult is not Task task)
        {
            return invocationResult;
        }

        await task.WaitAsync(cancellationToken).ConfigureAwait(false);

        if (!task.GetType().IsGenericType || task.GetType().GetGenericTypeDefinition() != typeof(Task<>))
        {
            return null;
        }

        var resultProperty = task.GetType().GetProperty("Result")!;
        return resultProperty.GetValue(task);
    }

    private static async Task<ControlPlaneInvocationResponse> BuildResponseAsync(object? value, CancellationToken cancellationToken)
    {
        if (value is null)
        {
            return new ControlPlaneInvocationResponse(
                Success: true,
                IsBinary: false,
                Payload: null,
                ErrorType: null,
                ErrorMessage: null);
        }

        if (value is byte[] bytes)
        {
            return new ControlPlaneInvocationResponse(
                Success: true,
                IsBinary: true,
                Payload: bytes,
                ErrorType: null,
                ErrorMessage: null);
        }

        if (value is Stream stream)
        {
            await using var copy = new MemoryStream();
            await stream.CopyToAsync(copy, cancellationToken).ConfigureAwait(false);
            return new ControlPlaneInvocationResponse(
                Success: true,
                IsBinary: true,
                Payload: copy.ToArray(),
                ErrorType: null,
                ErrorMessage: null);
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(value, value.GetType(), SerializerOptions);
        return new ControlPlaneInvocationResponse(
            Success: true,
            IsBinary: false,
            Payload: payload,
            ErrorType: null,
            ErrorMessage: null);
    }
}
