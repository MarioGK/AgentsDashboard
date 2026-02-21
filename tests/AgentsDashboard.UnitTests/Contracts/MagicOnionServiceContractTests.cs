using System.Reflection;
using AgentsDashboard.Contracts.TaskRuntime;
using MagicOnion;

namespace AgentsDashboard.UnitTests.Contracts;

public class MagicOnionServiceContractTests
{
    [Test]
    public async Task MagicOnionServiceContracts_MustNotUseCancellationTokenParameters()
    {
        var magicOnionContractInterfaceType = typeof(IService<>);
        var badEntries = typeof(ITaskRuntimeService)
            .Assembly
            .GetTypes()
            .Where(type => type.IsInterface &&
                           type.GetInterfaces().Any(
                               serviceInterface => serviceInterface is { IsGenericType: true } &&
                                                  serviceInterface.GetGenericTypeDefinition() == magicOnionContractInterfaceType))
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(CancellationToken)))
                .Select(method => $"{type.FullName}.{method.Name}({string.Join(", ", method.GetParameters().Select(GetParameterDescription))}"))
            .ToList();

        await Assert.That(badEntries.Count).IsEqualTo(0);
    }

    private static string GetParameterDescription(ParameterInfo parameter)
    {
        return $"{parameter.ParameterType.Name} {parameter.Name}";
    }
}
