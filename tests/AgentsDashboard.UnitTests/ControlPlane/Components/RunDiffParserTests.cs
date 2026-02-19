using AgentsDashboard.ControlPlane.Components.Shared;

namespace AgentsDashboard.UnitTests.ControlPlane.Components;

public sealed partial class RunDiffParserTests
{
    [Test]
    public async Task Parse_WhenPatchContainsMultipleFiles_ReturnsFileAndHunkMetadata()
    {
        const string patch = """
diff --git a/src/Foo.cs b/src/Foo.cs
index 1111111..2222222 100644
--- a/src/Foo.cs
+++ b/src/Foo.cs
@@ -1,2 +1,3 @@
diff --git a/src/Bar.cs b/src/Bar.cs
index 3333333..4444444 100644
--- a/src/Bar.cs
+++ b/src/Bar.cs
@@ -4,3 +4,2 @@ public class Bar
-    public int Count { get; set; }
     public bool Enabled { get; set; }
 }
""";

        var files = RunDiffParser.Parse(patch);

        await Assert.That(files.Count).IsEqualTo(2);
        await Assert.That(files[0].Path).IsEqualTo("src/Foo.cs");
        await Assert.That(files[0].AddedLines).IsEqualTo(1);
        await Assert.That(files[0].RemovedLines).IsEqualTo(0);
        await Assert.That(files[0].Hunks.Count).IsEqualTo(1);
        await Assert.That(files[0].Hunks[0].NewStart).IsEqualTo(1);
        await Assert.That(files[0].ModifiedContent.Contains("Name", StringComparison.Ordinal)).IsTrue();

        await Assert.That(files[1].Path).IsEqualTo("src/Bar.cs");
        await Assert.That(files[1].AddedLines).IsEqualTo(0);
        await Assert.That(files[1].RemovedLines).IsEqualTo(1);
        await Assert.That(files[1].Hunks.Count).IsEqualTo(1);
        await Assert.That(files[1].OriginalContent.Contains("Count", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task Parse_WhenPatchCreatesNewFile_UsesNewPathAsDisplayPath()
    {
        const string patch = """
diff --git a/dev/null b/new/file.txt
new file mode 100644
--- /dev/null
+++ b/new/file.txt
@@ -0,0 +1,2 @@
+line one
+line two
""";

        var files = RunDiffParser.Parse(patch);

        await Assert.That(files.Count).IsEqualTo(1);
        await Assert.That(files[0].Path).IsEqualTo("new/file.txt");
        await Assert.That(files[0].OldPath).IsEqualTo("/dev/null");
        await Assert.That(files[0].NewPath).IsEqualTo("new/file.txt");
        await Assert.That(files[0].AddedLines).IsEqualTo(2);
        await Assert.That(files[0].RemovedLines).IsEqualTo(0);
    }
}
