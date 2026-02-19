using AgentsDashboard.ControlPlane.Services;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public sealed class AgentTeamDiffMergeServiceTests
{
    [Test]
    public async Task Build_WhenLanesTouchDifferentFiles_MergesAllWithoutConflicts()
    {
        var laneInputs = new[]
        {
            new AgentTeamLaneDiffInput(
                LaneLabel: "planner",
                Harness: "codex",
                RunId: "run-1",
                Succeeded: true,
                Summary: "lane 1",
                DiffStat: "1 file changed",
                DiffPatch: """
                           diff --git a/a.txt b/a.txt
                           --- a/a.txt
                           +++ b/a.txt
                           @@ -1 +1 @@
                           -old
                           +new
                           """),
            new AgentTeamLaneDiffInput(
                LaneLabel: "implementer",
                Harness: "codex",
                RunId: "run-2",
                Succeeded: true,
                Summary: "lane 2",
                DiffStat: "1 file changed",
                DiffPatch: """
                           diff --git a/b.txt b/b.txt
                           --- a/b.txt
                           +++ b/b.txt
                           @@ -2 +2 @@
                           -foo
                           +bar
                           """),
        };

        var merged = AgentTeamDiffMergeService.Build(laneInputs);

        await Assert.That(merged.MergedFiles).IsEqualTo(2);
        await Assert.That(merged.ConflictCount).IsEqualTo(0);
        await Assert.That(merged.Conflicts).IsEmpty();
        await Assert.That(merged.Additions).IsEqualTo(2);
        await Assert.That(merged.Deletions).IsEqualTo(2);
        await Assert.That(merged.MergedPatch).Contains("diff --git a/a.txt b/a.txt");
        await Assert.That(merged.MergedPatch).Contains("diff --git a/b.txt b/b.txt");
        await Assert.That(merged.LaneDiffs.Count()).IsEqualTo(2);
        await Assert.That(merged.LaneDiffs.Select(x => x.Harness)).Contains("codex");
    }

    [Test]
    public async Task Build_WhenSameFileHunksOverlap_ProducesConflict()
    {
        var laneInputs = new[]
        {
            new AgentTeamLaneDiffInput(
                LaneLabel: "planner",
                Harness: "codex",
                RunId: "run-1",
                Succeeded: true,
                Summary: "lane 1",
                DiffStat: "1 file changed",
                DiffPatch: """
                           diff --git a/foo.txt b/foo.txt
                           --- a/foo.txt
                           +++ b/foo.txt
                           @@ -1 +1 @@
                           -a
                           +b
                           """),
            new AgentTeamLaneDiffInput(
                LaneLabel: "reviewer",
                Harness: "codex",
                RunId: "run-2",
                Succeeded: true,
                Summary: "lane 2",
                DiffStat: "1 file changed",
                DiffPatch: """
                           diff --git a/foo.txt b/foo.txt
                           --- a/foo.txt
                           +++ b/foo.txt
                           @@ -1 +1 @@
                           -a
                           +c
                           """),
        };

        var merged = AgentTeamDiffMergeService.Build(laneInputs);

        await Assert.That(merged.MergedFiles).IsEqualTo(0);
        await Assert.That(merged.ConflictCount).IsEqualTo(1);
        await Assert.That(merged.Conflicts.Count()).IsEqualTo(1);
        await Assert.That(merged.Conflicts[0].FilePath).IsEqualTo("foo.txt");
        await Assert.That(merged.Conflicts[0].Reason).Contains("overlapping");
        await Assert.That(merged.Conflicts[0].LaneLabels).Contains("planner");
        await Assert.That(merged.Conflicts[0].LaneLabels).Contains("reviewer");
        await Assert.That(merged.MergedPatch).IsEmpty();
    }

    [Test]
    public async Task Build_WhenSameFileHunksDoNotOverlap_MergesIntoSinglePatch()
    {
        var laneInputs = new[]
        {
            new AgentTeamLaneDiffInput(
                LaneLabel: "lane-a",
                Harness: "codex",
                RunId: "run-1",
                Succeeded: true,
                Summary: "lane 1",
                DiffStat: "1 file changed",
                DiffPatch: """
                           diff --git a/foo.txt b/foo.txt
                           --- a/foo.txt
                           +++ b/foo.txt
                           @@ -1 +1 @@
                           -a
                           +b
                           """),
            new AgentTeamLaneDiffInput(
                LaneLabel: "lane-b",
                Harness: "opencode",
                RunId: "run-2",
                Succeeded: true,
                Summary: "lane 2",
                DiffStat: "1 file changed",
                DiffPatch: """
                           diff --git a/foo.txt b/foo.txt
                           --- a/foo.txt
                           +++ b/foo.txt
                           @@ -10 +10 @@
                           -x
                           +y
                           """),
        };

        var merged = AgentTeamDiffMergeService.Build(laneInputs);

        await Assert.That(merged.MergedFiles).IsEqualTo(1);
        await Assert.That(merged.ConflictCount).IsEqualTo(0);
        await Assert.That(merged.Conflicts).IsEmpty();
        await Assert.That(merged.MergedPatch).Contains("diff --git a/foo.txt b/foo.txt");
        await Assert.That(merged.MergedPatch).Contains("@@ -1 +1 @@");
        await Assert.That(merged.MergedPatch).Contains("@@ -10 +10 @@");
    }
}
