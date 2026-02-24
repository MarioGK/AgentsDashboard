using Bunit;
using MudBlazor;
using MudBlazor.Services;

namespace AgentsDashboard.Workspace.ComponentTests.Infrastructure;

public static class WorkspaceBunitTestContext
{
    public static BunitContext Create()
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddMudServices(configuration =>
        {
            configuration.PopoverOptions.CheckForPopoverProvider = false;
        });
        context.Services.AddMudMarkdownServices();
        return context;
    }
}
