using System.Text;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Components.Shared;

namespace AgentsDashboard.ControlPlane.Services;


public sealed record AgentTeamLaneDiffInput(
    string LaneLabel,
    string Harness,
    string RunId,
    bool Succeeded,
    string Summary,
    string DiffStat,
    string DiffPatch);
