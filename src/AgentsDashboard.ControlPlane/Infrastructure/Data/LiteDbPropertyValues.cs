using System.Reflection;

namespace AgentsDashboard.ControlPlane.Infrastructure.Data;

public sealed class LiteDbPropertyValues(object target)
{
    public void SetValues(object source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var sourceType = source.GetType();
        var targetType = target.GetType();

        foreach (var sourceProperty in sourceType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!sourceProperty.CanRead || sourceProperty.GetIndexParameters().Length != 0)
            {
                continue;
            }

            var targetProperty = targetType.GetProperty(sourceProperty.Name, BindingFlags.Public | BindingFlags.Instance);
            if (targetProperty is null || !targetProperty.CanWrite || targetProperty.GetIndexParameters().Length != 0)
            {
                continue;
            }

            var value = sourceProperty.GetValue(source);
            targetProperty.SetValue(target, value);
        }
    }
}
