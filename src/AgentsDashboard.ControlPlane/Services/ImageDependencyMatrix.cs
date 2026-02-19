using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Docker.DotNet;
using Docker.DotNet.Models;
using ICSharpCode.SharpZipLib.Tar;

namespace AgentsDashboard.ControlPlane.Services;


public sealed record ImageDependencyMatrix(
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> PackageManagers,
    IReadOnlyList<string> Harnesses,
    IReadOnlyList<string> SecurityTools);
