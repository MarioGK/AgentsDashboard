using MudBlazor;

namespace AgentsDashboard.ControlPlane.Infrastructure.Extensions;

internal static class SnackbarImportancePolicy
{
    public static bool ShouldDisplay(Severity severity, bool importantSuccess)
    {
        return severity switch
        {
            Severity.Error => true,
            Severity.Warning => true,
            Severity.Success => importantSuccess,
            _ => false,
        };
    }
}
