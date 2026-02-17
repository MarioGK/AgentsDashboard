using AgentsDashboard.ControlPlane.Services;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public sealed class AgentTeamDiffMergeServiceTests
{
    [Test]
    public void Build_WhenLanesTouchDifferentFiles_MergesAllWithoutConflicts()
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

        merged.MergedFiles.Should().Be(2);
        merged.ConflictCount.Should().Be(0);
        merged.Conflicts.Should().BeEmpty();
        merged.Additions.Should().Be(2);
        merged.Deletions.Should().Be(2);
        merged.MergedPatch.Should().Contain("diff --git a/a.txt b/a.txt");
        merged.MergedPatch.Should().Contain("diff --git a/b.txt b/b.txt");
        merged.LaneDiffs.Should().HaveCount(2);
        merged.LaneDiffs.Select(x => x.Harness).Should().Contain("codex");
    }

    [Test]
    public void Build_WhenSameFileHunksOverlap_ProducesConflict()
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

        merged.MergedFiles.Should().Be(0);
        merged.ConflictCount.Should().Be(1);
        merged.Conflicts.Should().ContainSingle();
        merged.Conflicts[0].FilePath.Should().Be("foo.txt");
        merged.Conflicts[0].Reason.Should().Contain("overlapping");
        merged.Conflicts[0].LaneLabels.Should().Contain("planner");
        merged.Conflicts[0].LaneLabels.Should().Contain("reviewer");
        merged.MergedPatch.Should().BeEmpty();
    }

    [Test]
    public void Build_WhenSameFileHunksDoNotOverlap_MergesIntoSinglePatch()
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
                Harness: "claude-code",
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

        merged.MergedFiles.Should().Be(1);
        merged.ConflictCount.Should().Be(0);
        merged.Conflicts.Should().BeEmpty();
        merged.MergedPatch.Should().Contain("diff --git a/foo.txt b/foo.txt");
        merged.MergedPatch.Should().Contain("@@ -1 +1 @@");
        merged.MergedPatch.Should().Contain("@@ -10 +10 @@");
    }
}
