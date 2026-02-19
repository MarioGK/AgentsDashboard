using System.Text;
using System.Text.RegularExpressions;

namespace AgentsDashboard.ControlPlane.Components.Shared;



public sealed record RunDiffHunk(
    int Index,
    int OldStart,
    int OldCount,
    int NewStart,
    int NewCount,
    string Header,
    string Preview);
