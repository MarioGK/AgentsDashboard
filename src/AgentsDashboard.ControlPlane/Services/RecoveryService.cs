using System.Diagnostics;
using System.Diagnostics.Metrics;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.ControlPlane.Services;


public sealed record DeadRunDetectionResult
{
    public int StaleRunsTerminated { get; set; }
    public int ZombieRunsTerminated { get; set; }
    public int OverdueRunsTerminated { get; set; }
}
