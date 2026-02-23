using Bunit;
using MudBlazor.Services;

namespace AgentsDashboard.Workspace.ComponentTests.Infrastructure;

public static class WorkspaceBunitTestContext
{
    public static TestContext Create()
    {
        var context = new TestContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddMudServices();
        return context;
    }
}
