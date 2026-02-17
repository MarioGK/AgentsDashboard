using System.Reflection;
using AgentsDashboard.ControlPlane.Components.Shared;
using BlazorMonaco.Editor;

namespace AgentsDashboard.UnitTests.ControlPlane.Components;

public sealed class RunDiffViewerOptionsTests
{
    [Test]
    public async Task DiffEditorOptions_UsesPlanRequiredConstructionDefaults()
    {
        var method = typeof(RunDiffViewer).GetMethod(
            "DiffEditorOptions",
            BindingFlags.Static | BindingFlags.NonPublic);

        await Assert.That(method).IsNotNull();

        var options = (StandaloneDiffEditorConstructionOptions?)method!.Invoke(null, [null!, true]);

        await Assert.That(options).IsNotNull();
        await Assert.That(options!.RenderSideBySide).IsTrue();
        await Assert.That(options.LineNumbers).IsEqualTo("on");
        await Assert.That(options.GlyphMargin).IsTrue();
        await Assert.That(options.ReadOnly).IsTrue();
        await Assert.That(options.RenderIndicators).IsTrue();
        await Assert.That(options.IgnoreTrimWhitespace).IsFalse();

        var inlineOptions = (StandaloneDiffEditorConstructionOptions?)method!.Invoke(null, [null!, false]);
        await Assert.That(inlineOptions).IsNotNull();
        await Assert.That(inlineOptions!.RenderSideBySide).IsFalse();
    }
}
