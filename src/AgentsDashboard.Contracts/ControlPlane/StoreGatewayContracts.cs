using System;
using System.Collections.Generic;
using MagicOnion;
using MessagePack;

namespace AgentsDashboard.Contracts.ControlPlane;

[MessagePackObject]
public sealed record ControlPlaneInvocationArgument(
    [property: Key(0)] string TypeName,
    [property: Key(1)] byte[]? Payload);

[MessagePackObject]
public sealed record ControlPlaneInvocationRequest(
    [property: Key(0)] string MethodName,
    [property: Key(1)] string[] ParameterTypeNames,
    [property: Key(2)] List<ControlPlaneInvocationArgument> Arguments,
    [property: Key(3)] bool ExpectsFileStream);

[MessagePackObject]
public sealed record ControlPlaneInvocationResponse(
    [property: Key(0)] bool Success,
    [property: Key(1)] bool IsBinary,
    [property: Key(2)] byte[]? Payload,
    [property: Key(3)] string? ErrorType,
    [property: Key(4)] string? ErrorMessage);

public interface IControlPlaneStoreGateway : IService<IControlPlaneStoreGateway>
{
    UnaryResult<ControlPlaneInvocationResponse> InvokeAsync(ControlPlaneInvocationRequest request);
}
