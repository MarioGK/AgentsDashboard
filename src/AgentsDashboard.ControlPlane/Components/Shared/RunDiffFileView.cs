using System.Text;
using System.Text.RegularExpressions;

namespace AgentsDashboard.ControlPlane.Components.Shared;



public sealed record RunDiffFileView(
    string Path,
    string OldPath,
    string NewPath,
    int AddedLines,
    int RemovedLines,
    IReadOnlyList<RunDiffHunk> Hunks,
    string Patch,
    string OriginalContent,
    string ModifiedContent);
