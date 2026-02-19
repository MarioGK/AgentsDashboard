using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Docker.DotNet;
using Docker.DotNet.Models;
using ICSharpCode.SharpZipLib.Tar;

namespace AgentsDashboard.ControlPlane.Services;


public record AiDockerfileResult(bool Success, string Dockerfile, string? Error);
