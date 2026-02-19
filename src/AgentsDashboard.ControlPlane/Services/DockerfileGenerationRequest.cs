using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Docker.DotNet;
using Docker.DotNet.Models;
using ICSharpCode.SharpZipLib.Tar;

namespace AgentsDashboard.ControlPlane.Services;


public record DockerfileGenerationRequest(
    string Description,
    string BaseImage,
    string[] Runtimes,
    string[] Tools,
    string[] Harnesses,
    bool IncludeGit,
    bool IncludeDockerCli,
    int TargetPlatform);
