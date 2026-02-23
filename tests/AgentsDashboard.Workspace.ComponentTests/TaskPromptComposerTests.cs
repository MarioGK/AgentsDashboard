using AgentsDashboard.ControlPlane.Components.Shared;
using AgentsDashboard.ControlPlane.Features.Workspace.Services;
using AgentsDashboard.Workspace.ComponentTests.Infrastructure;

namespace AgentsDashboard.Workspace.ComponentTests;

public sealed class TaskPromptComposerTests
{
    [Test]
    public async Task ClearButtonClearsComposerValueAndImagesAsync()
    {
        using var context = WorkspaceBunitTestContext.Create();

        var currentValue = "Implement the workspace flow.";
        IReadOnlyList<WorkspaceImageInput> currentImages =
        [
            new WorkspaceImageInput(
                Id: "img-1",
                FileName: "sample.png",
                MimeType: "image/png",
                SizeBytes: 100,
                DataUrl: "data:image/png;base64,AA==")
        ];
        var submitCount = 0;

        var component = context.RenderComponent<TaskPromptComposer>(parameters => parameters
            .Add(p => p.InputId, "workspace-composer-input-fixed")
            .Add(p => p.Value, currentValue)
            .Add(p => p.Images, currentImages)
            .Add(p => p.ValueChanged, (string value) => currentValue = value)
            .Add(p => p.ImagesChanged, (IReadOnlyList<WorkspaceImageInput> images) => currentImages = images)
            .Add(p => p.ShowSubmitButton, true)
            .Add(p => p.OnSubmit, () => submitCount++));

        component.Find("[data-testid='workspace-composer-clear']").Click();

        await Assert.That(currentValue).IsEqualTo(string.Empty);
        await Assert.That(currentImages.Count).IsEqualTo(0);

        component.SetParametersAndRender(parameters => parameters
            .Add(p => p.InputId, "workspace-composer-input-fixed")
            .Add(p => p.Value, currentValue)
            .Add(p => p.Images, currentImages)
            .Add(p => p.ValueChanged, (string value) => currentValue = value)
            .Add(p => p.ImagesChanged, (IReadOnlyList<WorkspaceImageInput> images) => currentImages = images)
            .Add(p => p.ShowSubmitButton, true)
            .Add(p => p.OnSubmit, () => submitCount++));

        var value = component.Find("[data-testid='workspace-composer-input']").GetAttribute("value") ?? string.Empty;
        await Assert.That(value).IsEqualTo(string.Empty);
        await Assert.That(component.FindAll(".workspace-composer-file-chip").Count).IsEqualTo(0);
    }

    [Test]
    public async Task SubmitButtonTracksComposerStateAsync()
    {
        using var context = WorkspaceBunitTestContext.Create();

        var component = context.RenderComponent<TaskPromptComposer>(parameters => parameters
            .Add(p => p.InputId, "workspace-composer-input-fixed")
            .Add(p => p.Value, string.Empty)
            .Add(p => p.Images, Array.Empty<WorkspaceImageInput>())
            .Add(p => p.ShowSubmitButton, true)
            .Add(p => p.OnSubmit, () => { }));

        var submitButton = component.Find("[data-testid='workspace-composer-send']");
        await Assert.That(submitButton.HasAttribute("disabled")).IsTrue();

        component.SetParametersAndRender(parameters => parameters
            .Add(p => p.InputId, "workspace-composer-input-fixed")
            .Add(p => p.Value, "Run a quick check")
            .Add(p => p.Images, Array.Empty<WorkspaceImageInput>())
            .Add(p => p.ShowSubmitButton, true)
            .Add(p => p.OnSubmit, () => { }));

        submitButton = component.Find("[data-testid='workspace-composer-send']");
        await Assert.That(submitButton.HasAttribute("disabled")).IsFalse();
    }
}
