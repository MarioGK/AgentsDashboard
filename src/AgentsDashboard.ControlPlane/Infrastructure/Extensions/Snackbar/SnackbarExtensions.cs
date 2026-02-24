using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace AgentsDashboard.ControlPlane.Infrastructure.Extensions;

internal static class SnackbarExtensions
{
    public static Snackbar? AddImportant(
        this ISnackbar snackbar,
        string message,
        Severity severity = Severity.Normal,
        Action<SnackbarOptions>? configure = null,
        string? key = null,
        bool importantSuccess = false)
    {
        if (!SnackbarImportancePolicy.ShouldDisplay(severity, importantSuccess))
        {
            return null;
        }

        return snackbar.Add(message, severity, configure, key);
    }

    public static Snackbar? AddImportant(
        this ISnackbar snackbar,
        MarkupString message,
        Severity severity = Severity.Normal,
        Action<SnackbarOptions>? configure = null,
        string? key = null,
        bool importantSuccess = false)
    {
        if (!SnackbarImportancePolicy.ShouldDisplay(severity, importantSuccess))
        {
            return null;
        }

        return snackbar.Add(message, severity, configure, key);
    }

    public static Snackbar? AddImportant(
        this ISnackbar snackbar,
        RenderFragment message,
        Severity severity = Severity.Normal,
        Action<SnackbarOptions>? configure = null,
        string? key = null,
        bool importantSuccess = false)
    {
        if (!SnackbarImportancePolicy.ShouldDisplay(severity, importantSuccess))
        {
            return null;
        }

        return snackbar.Add(message, severity, configure, key);
    }
}
