using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Docker.DotNet;
using Docker.DotNet.Models;
using ICSharpCode.SharpZipLib.Tar;

namespace AgentsDashboard.ControlPlane.Services;

public record ImageBuildResult(bool Success, string ImageId, List<string> Logs);
public record ImageInfo(string Tag, string Id, long Size, DateTime Created);
public record DockerfileGenerationRequest(
    string Description,
    string BaseImage,
    string[] Runtimes,
    string[] Tools,
    string[] Harnesses,
    bool IncludeGit,
    bool IncludeDockerCli,
    int TargetPlatform);
public record AiDockerfileResult(bool Success, string Dockerfile, string? Error);
public sealed record ImageDependencyMatrix(
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> PackageManagers,
    IReadOnlyList<string> Harnesses,
    IReadOnlyList<string> SecurityTools);

public class ImageBuilderService(
    LlmTornadoGatewayService llmTornadoGatewayService,
    ILogger<ImageBuilderService> logger) : IAsyncDisposable
{
    private readonly DockerClient _dockerClient = new DockerClientConfiguration().CreateClient();
    private static readonly Dictionary<string, string> PinnedImageVersions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ubuntu"] = "24.04",
        ["debian"] = "bookworm-slim",
        ["alpine"] = "3.19",
        ["node"] = "20-slim",
        ["python"] = "3.12-slim",
        ["golang"] = "1.23-alpine",
        ["rust"] = "1.75-slim",
        ["mcr.microsoft.com/dotnet/sdk"] = "10.0",
        ["mcr.microsoft.com/dotnet/aspnet"] = "10.0"
    };

    public string GenerateDockerfile(DockerfileGenerationRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# AI-Generated Dockerfile");
        sb.AppendLine($"# Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"# Description: {request.Description}");
        sb.AppendLine();

        var baseImage = request.BaseImage switch
        {
            "ubuntu" => "ubuntu:24.04",
            "alpine" => "alpine:3.19",
            "debian" => "debian:bookworm-slim",
            "node" => "node:20-slim",
            "python" => "python:3.12-slim",
            "dotnet" => "mcr.microsoft.com/dotnet/sdk:10.0",
            "golang" => "golang:1.23-alpine",
            "rust" => "rust:1.75-slim",
            _ => request.BaseImage
        };

        sb.AppendLine($"FROM {baseImage}");
        sb.AppendLine();

        var isAlpine = baseImage.Contains("alpine", StringComparison.OrdinalIgnoreCase);
        var pkgMgr = isAlpine ? "apk" : "apt-get";

        sb.AppendLine("ENV DEBIAN_FRONTEND=noninteractive \\");
        sb.AppendLine("    DOTNET_CLI_TELEMETRY_OPTOUT=1 \\");
        sb.AppendLine("    HOME=/home/agent");
        sb.AppendLine();

        var packages = new List<string> { "curl", "git", "bash" };

        if (request.IncludeGit)
            packages.Add("git");

        if (request.IncludeDockerCli && !isAlpine)
        {
            packages.AddRange(new[] { "ca-certificates", "gnupg", "lsb-release" });
        }

        if (request.Runtimes.Contains("python") && !baseImage.Contains("python", StringComparison.OrdinalIgnoreCase))
        {
            packages.Add("python3");
            packages.Add("python3-pip");
        }

        if (request.Runtimes.Contains("node") && !baseImage.Contains("node", StringComparison.OrdinalIgnoreCase))
        {
            packages.Add("nodejs");
            packages.Add("npm");
        }

        if (request.Tools.Contains("ripgrep"))
            packages.Add(isAlpine ? "ripgrep" : "ripgrep");

        if (request.Tools.Contains("fd-find"))
            packages.Add(isAlpine ? "fd" : "fd-find");

        if (request.Tools.Contains("jq"))
            packages.Add("jq");

        if (request.Tools.Contains("build-essential") && !isAlpine)
            packages.Add("build-essential");

        if (packages.Count > 0)
        {
            if (isAlpine)
            {
                sb.AppendLine($"RUN apk add --no-cache \\");
                sb.AppendLine($"    {string.Join(" \\\n    ", packages.Distinct())}");
            }
            else
            {
                sb.AppendLine($"RUN {pkgMgr} update && {pkgMgr} install -y \\");
                sb.AppendLine($"    {string.Join(" \\\n    ", packages.Distinct())} \\");
                sb.AppendLine("    && rm -rf /var/lib/apt/lists/*");
            }
            sb.AppendLine();
        }

        if (request.Runtimes.Contains("dotnet") && !baseImage.Contains("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine("RUN wget https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh \\");
            sb.AppendLine("    && chmod +x /tmp/dotnet-install.sh \\");
            sb.AppendLine("    && /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet \\");
            sb.AppendLine("    && ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet \\");
            sb.AppendLine("    && rm /tmp/dotnet-install.sh");
            sb.AppendLine();
        }

        if (request.Runtimes.Contains("bun"))
        {
            sb.AppendLine("RUN curl -fsSL https://bun.sh/install | bash \\");
            sb.AppendLine("    && mv /root/.bun/bin/bun /usr/local/bin/bun \\");
            sb.AppendLine("    && chmod +x /usr/local/bin/bun");
            sb.AppendLine();
        }

        if (request.Runtimes.Contains("go") && !baseImage.Contains("golang", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine("RUN wget https://go.dev/dl/go1.23.6.linux-amd64.tar.gz \\");
            sb.AppendLine("    && tar -C /usr/local -xzf go1.23.6.linux-amd64.tar.gz \\");
            sb.AppendLine("    && rm go1.23.6.linux-amd64.tar.gz");
            sb.AppendLine("ENV PATH=\"/usr/local/go/bin:/root/go/bin:${PATH}\"");
            sb.AppendLine();
        }

        if (request.Harnesses.Contains("claude-code"))
        {
            sb.AppendLine("RUN npm install -g @anthropic-ai/claude-code 2>/dev/null || echo 'Claude Code installation attempted'");
            sb.AppendLine("ENV ANTHROPIC_API_KEY=\"\" CLAUDE_OUTPUT_ENVELOPE=true");
            sb.AppendLine();
        }

        if (request.Harnesses.Contains("codex"))
        {
            sb.AppendLine("RUN npm install -g openai 2>/dev/null || echo 'OpenAI installation attempted'");
            sb.AppendLine("ENV OPENAI_API_KEY=\"\" OPENAI_OUTPUT_ENVELOPE=true");
            sb.AppendLine();
        }

        if (request.Harnesses.Contains("opencode"))
        {
            sb.AppendLine("RUN go install github.com/opencode-ai/opencode@latest 2>/dev/null || echo 'OpenCode installation attempted'");
            sb.AppendLine("ENV OPENCODE_OUTPUT_ENVELOPE=true");
            sb.AppendLine();
        }

        if (request.Harnesses.Contains("zai"))
        {
            sb.AppendLine("RUN npm install -g cc-mirror 2>/dev/null || echo 'cc-mirror installation attempted'");
            sb.AppendLine("ENV Z_AI_API_KEY=\"\" ZAI_OUTPUT_ENVELOPE=true ZAI_MODEL=glm-5");
            sb.AppendLine();
        }

        if (request.IncludeDockerCli && !isAlpine)
        {
            sb.AppendLine("RUN install -m 0755 -d /etc/apt/keyrings \\");
            sb.AppendLine("    && curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc \\");
            sb.AppendLine("    && chmod a+r /etc/apt/keyrings/docker.asc \\");
            sb.AppendLine("    && echo \"deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo \\\"$VERSION_CODENAME\\\") stable\" | tee /etc/apt/sources.list.d/docker.list > /dev/null \\");
            sb.AppendLine("    && apt-get update \\");
            sb.AppendLine("    && apt-get install -y docker-ce-cli docker-compose-plugin \\");
            sb.AppendLine("    && rm -rf /var/lib/apt/lists/*");
            sb.AppendLine();
        }

        if (request.Tools.Contains("playwright"))
        {
            sb.AppendLine("RUN npx playwright@latest install --with-deps chromium || echo 'Playwright installation attempted'");
            sb.AppendLine();
        }

        sb.AppendLine("RUN useradd -m -s /bin/bash -u 1000 agent \\");
        sb.AppendLine("    && echo \"agent ALL=(ALL) NOPASSWD:ALL\" >> /etc/sudoers");
        sb.AppendLine();

        sb.AppendLine("RUN mkdir -p /workspace /artifacts \\");
        sb.AppendLine("    && chown -R agent:agent /workspace /artifacts");
        sb.AppendLine();

        sb.AppendLine("USER agent");
        sb.AppendLine("WORKDIR /workspace");
        sb.AppendLine();

        sb.AppendLine("CMD [\"/bin/bash\"]");

        return sb.ToString();
    }

    public ImageDependencyMatrix AnalyzeDependencyMatrix(string dockerfileContent)
    {
        if (string.IsNullOrWhiteSpace(dockerfileContent))
        {
            return new ImageDependencyMatrix([], [], [], []);
        }

        var languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var packageManagers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var harnesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var securityTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in dockerfileContent.Split('\n'))
        {
            var line = StripInlineComment(rawLine).Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith("FROM", StringComparison.OrdinalIgnoreCase))
            {
                var image = ExtractFromImage(line);
                AddLanguageFromImage(image, languages);
            }

            if (line.Contains("apt-get", StringComparison.OrdinalIgnoreCase))
            {
                packageManagers.Add("apt-get");
            }

            if (line.Contains("apk", StringComparison.OrdinalIgnoreCase))
            {
                packageManagers.Add("apk");
            }

            if (line.Contains("yum", StringComparison.OrdinalIgnoreCase) || line.Contains("dnf", StringComparison.OrdinalIgnoreCase))
            {
                packageManagers.Add("yum/dnf");
            }

            if (line.Contains("npm install", StringComparison.OrdinalIgnoreCase) || line.Contains("npm install -g", StringComparison.OrdinalIgnoreCase))
            {
                packageManagers.Add("npm");
            }

            if (line.Contains("pip install", StringComparison.OrdinalIgnoreCase))
            {
                packageManagers.Add("pip");
                languages.Add("python");
            }

            if (line.Contains("go install", StringComparison.OrdinalIgnoreCase))
            {
                packageManagers.Add("go");
                languages.Add("go");
            }

            if (line.Contains("cargo", StringComparison.OrdinalIgnoreCase))
            {
                languages.Add("rust");
            }

            if (line.Contains("openai", StringComparison.OrdinalIgnoreCase) && line.Contains("npm install -g", StringComparison.OrdinalIgnoreCase))
            {
                harnesses.Add("codex");
            }

            if (line.Contains("claude-code", StringComparison.OrdinalIgnoreCase) || line.Contains("claude-wrapper", StringComparison.OrdinalIgnoreCase))
            {
                harnesses.Add("claude-code");
            }

            if (line.Contains("cc-mirror", StringComparison.OrdinalIgnoreCase))
            {
                harnesses.Add("zai");
            }

            if (line.Contains("opencode", StringComparison.OrdinalIgnoreCase))
            {
                harnesses.Add("opencode");
            }

            if (line.Contains("playwright", StringComparison.OrdinalIgnoreCase))
            {
                harnesses.Add("playwright");
            }

            if (line.Contains("trivy", StringComparison.OrdinalIgnoreCase)
                || line.Contains("snyk", StringComparison.OrdinalIgnoreCase)
                || line.Contains("grype", StringComparison.OrdinalIgnoreCase)
                || line.Contains("checkov", StringComparison.OrdinalIgnoreCase)
                || line.Contains("semgrep", StringComparison.OrdinalIgnoreCase)
                || line.Contains("gitleaks", StringComparison.OrdinalIgnoreCase)
                || line.Contains("nancy", StringComparison.OrdinalIgnoreCase)
                || line.Contains("safety", StringComparison.OrdinalIgnoreCase)
                || line.Contains("pip-audit", StringComparison.OrdinalIgnoreCase))
            {
                if (line.Contains("trivy", StringComparison.OrdinalIgnoreCase))
                {
                    securityTools.Add("Trivy");
                }

                if (line.Contains("snyk", StringComparison.OrdinalIgnoreCase))
                {
                    securityTools.Add("Snyk");
                }

                if (line.Contains("grype", StringComparison.OrdinalIgnoreCase))
                {
                    securityTools.Add("Grype");
                }

                if (line.Contains("checkov", StringComparison.OrdinalIgnoreCase))
                {
                    securityTools.Add("Checkov");
                }

                if (line.Contains("semgrep", StringComparison.OrdinalIgnoreCase))
                {
                    securityTools.Add("Semgrep");
                }

                if (line.Contains("gitleaks", StringComparison.OrdinalIgnoreCase))
                {
                    securityTools.Add("Gitleaks");
                }

                if (line.Contains("nancy", StringComparison.OrdinalIgnoreCase))
                {
                    securityTools.Add("Nancy");
                }

                if (line.Contains("safety", StringComparison.OrdinalIgnoreCase))
                {
                    securityTools.Add("Safety");
                }

                if (line.Contains("pip-audit", StringComparison.OrdinalIgnoreCase))
                {
                    securityTools.Add("pip-audit");
                }
            }
        }

        return new ImageDependencyMatrix(
            [.. languages.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)],
            [.. packageManagers.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)],
            [.. harnesses.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)],
            [.. securityTools.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)]);
    }

    public string HardenDockerfile(string dockerfileContent)
    {
        if (string.IsNullOrWhiteSpace(dockerfileContent))
        {
            return dockerfileContent;
        }

        var lines = dockerfileContent.Split('\n');
        var hardened = new List<string>(lines.Length + 12);

        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("FROM ", StringComparison.OrdinalIgnoreCase))
            {
                var pinned = ApplyPinnedFromLine(line);
                hardened.Add(pinned.line);
                continue;
            }

            hardened.Add(line);
        }

        if (!HasSafeUser(hardened))
        {
            InsertBeforeRuntimeInstruction(
                hardened,
                [
                    "RUN if id agent >/dev/null 2>&1; then :; elif command -v useradd >/dev/null 2>&1; then useradd -m -s /bin/bash -u 1000 agent; elif command -v adduser >/dev/null 2>&1; then adduser -D -u 1000 -s /bin/sh agent; fi",
                    "RUN mkdir -p /workspace /artifacts && chown -R 1000:1000 /workspace /artifacts /home/agent",
                    "USER agent",
                    "WORKDIR /workspace"
                ]);
        }

        if (!HasInstruction(hardened, "HEALTHCHECK"))
        {
            InsertBeforeRuntimeInstruction(
                hardened,
                [
                    "HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \\",
                    "  CMD [\"/bin/sh\", \"-c\", \"test -d /workspace && test -d /artifacts\"]"
                ]);
        }

        return string.Join('\n', hardened)
            .TrimEnd('\n', '\r');
    }

    public async Task<AiDockerfileResult> GenerateDockerfileWithAiAsync(
        string description,
        string? zaiApiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(zaiApiKey))
        {
            return new AiDockerfileResult(false, string.Empty, "Z.ai API key not configured. Please set up Z.ai credentials in Provider Settings.");
        }

        return await llmTornadoGatewayService.GenerateDockerfileAsync(description, zaiApiKey, cancellationToken);
    }

    public async Task<ImageBuildResult> BuildImageAsync(
        string dockerfileContent,
        string tag,
        Action<string>? onLogLine,
        CancellationToken cancellationToken)
    {
        var logs = new List<string>();
        var imageId = string.Empty;

        try
        {
            logger.ZLogInformation("Building Docker image {Tag}", tag);

            var tarStream = CreateTarArchive(dockerfileContent);
            tarStream.Position = 0;

            var buildParameters = new ImageBuildParameters
            {
                Tags = [tag],
                Dockerfile = "Dockerfile",
                Remove = true,
                ForceRemove = true
            };

            var progress = new Progress<JSONMessage>(message =>
            {
                var logLine = message.Stream ?? message.Status ?? message.ErrorMessage ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(logLine))
                {
                    logLine = logLine.TrimEnd('\n', '\r');
                    logs.Add(logLine);
                    onLogLine?.Invoke(logLine);

                    if (message.Aux is not null)
                    {
                        try
                        {
                            var auxJson = JsonSerializer.Serialize(message.Aux);
                            var auxDict = JsonSerializer.Deserialize<Dictionary<string, object>>(auxJson);
                            if (auxDict?.TryGetValue("ID", out var idObj) == true && idObj is JsonElement elem)
                            {
                                imageId = elem.GetString() ?? string.Empty;
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            });

            await _dockerClient.Images.BuildImageFromDockerfileAsync(
                buildParameters,
                tarStream,
                [],
                new Dictionary<string, string>(),
                progress,
                cancellationToken);

            if (string.IsNullOrEmpty(imageId))
            {
                imageId = tag;
            }

            var success = !logs.Any(l => l.Contains("error", StringComparison.OrdinalIgnoreCase));
            logger.ZLogInformation("Build completed for {Tag}. Success: {Success}, ImageId: {ImageId}", tag, success, imageId);

            return new ImageBuildResult(success, imageId, logs);
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, "Exception during image build for {Tag}", tag);
            logs.Add($"Error: {ex.Message}");
            return new ImageBuildResult(false, string.Empty, logs);
        }
    }

    public virtual async Task<List<ImageInfo>> ListImagesAsync(string? filter, CancellationToken cancellationToken)
    {
        try
        {
            var parameters = new ImagesListParameters
            {
                All = false
            };

            var images = await _dockerClient.Images.ListImagesAsync(parameters, cancellationToken);

            var result = images
                .SelectMany(img => img.RepoTags ?? [],
                    (img, tag) => new ImageInfo(
                        Tag: tag,
                        Id: img.ID,
                        Size: img.Size,
                        Created: img.Created))
                .Where(img => !img.Tag.EndsWith("<none>:<none>"))
                .Where(img => string.IsNullOrEmpty(filter) || img.Tag.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(img => img.Created)
                .ToList();

            logger.ZLogInformation("Listed {Count} Docker images", result.Count);
            return result;
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, "Exception while listing images");
            return [];
        }
    }

    public virtual async Task<bool> DeleteImageAsync(string tag, CancellationToken cancellationToken)
    {
        try
        {
            logger.ZLogInformation("Deleting Docker image {Tag}", tag);

            await _dockerClient.Images.DeleteImageAsync(
                tag,
                new ImageDeleteParameters { Force = true },
                cancellationToken);

            logger.ZLogInformation("Successfully deleted image {Tag}", tag);
            return true;
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, "Failed to delete image {Tag}", tag);
            return false;
        }
    }

    public async Task<bool> TagImageAsync(string sourceTag, string targetTag, CancellationToken cancellationToken)
    {
        try
        {
            logger.ZLogInformation("Tagging image {SourceTag} as {TargetTag}", sourceTag, targetTag);

            var parts = targetTag.Split(':', 2);
            var repo = parts.Length > 0 ? parts[0] : targetTag;
            var tag = parts.Length > 1 ? parts[1] : "latest";

            await _dockerClient.Images.TagImageAsync(
                sourceTag,
                new ImageTagParameters { RepositoryName = repo, Tag = tag },
                cancellationToken);

            logger.ZLogInformation("Successfully tagged image {SourceTag} as {TargetTag}", sourceTag, targetTag);
            return true;
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, "Failed to tag image {SourceTag} as {TargetTag}", sourceTag, targetTag);
            return false;
        }
    }

    private static MemoryStream CreateTarArchive(string dockerfileContent)
    {
        var tarStream = new MemoryStream();
        using (var archive = new TarOutputStream(tarStream, Encoding.UTF8) { IsStreamOwner = false })
        {
            var dockerfileBytes = Encoding.UTF8.GetBytes(dockerfileContent);
            var entry = TarEntry.CreateTarEntry("Dockerfile");
            entry.Size = dockerfileBytes.Length;
            entry.ModTime = DateTime.UtcNow;

            archive.PutNextEntry(entry);
            archive.Write(dockerfileBytes, 0, dockerfileBytes.Length);
            archive.CloseEntry();
        }

        return tarStream;
    }

    public ValueTask DisposeAsync()
    {
        _dockerClient.Dispose();
        return ValueTask.CompletedTask;
    }

    private static bool HasInstruction(IReadOnlyList<string> lines, string instruction)
    {
        return lines.Any(line =>
        {
            var trimmed = line.TrimStart();
            return trimmed.StartsWith($"{instruction} ", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals(instruction, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static bool HasSafeUser(List<string> lines)
    {
        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith("USER ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var user = trimmed["USER ".Length..].Trim();
            if (!user.Equals("root", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string ExtractFromImage(string line)
    {
        var tokens = Regex.Matches(line, @"[^\s]+")
            .Select(match => match.Value)
            .ToArray();
        if (tokens.Length < 2)
        {
            return string.Empty;
        }

        var imageIndex = 1;
        while (imageIndex < tokens.Length && tokens[imageIndex].StartsWith("--", StringComparison.OrdinalIgnoreCase))
        {
            imageIndex++;
        }

        return imageIndex < tokens.Length ? tokens[imageIndex] : string.Empty;
    }

    private static (string line, bool changed) ApplyPinnedFromLine(string line)
    {
        var tokens = Regex.Matches(line, @"[^\s]+").Select(match => match.Value).ToList();
        if (tokens.Count < 2)
        {
            return (line, false);
        }

        var imageIndex = 1;
        while (imageIndex < tokens.Count && tokens[imageIndex].StartsWith("--", StringComparison.OrdinalIgnoreCase))
        {
            imageIndex++;
        }

        if (imageIndex >= tokens.Count)
        {
            return (line, false);
        }

        var image = tokens[imageIndex];
        if (!TryPinImage(image, out var pinnedImage))
        {
            return (line, false);
        }

        var beforeImage = string.Join(" ", tokens.Take(imageIndex));
        var afterImage = string.Join(" ", tokens.Skip(imageIndex + 1));
        var rebuilt = $"{beforeImage} {pinnedImage}{(afterImage.Length > 0 ? $" {afterImage}" : string.Empty)}";
        return (rebuilt, true);
    }

    private static bool TryPinImage(string image, out string pinnedImage)
    {
        pinnedImage = image;

        if (string.IsNullOrWhiteSpace(image) || image.Contains("${", StringComparison.OrdinalIgnoreCase) || image.Contains("$(", StringComparison.OrdinalIgnoreCase) || image.Contains('@'))
        {
            return false;
        }

        var tagSeparator = image.LastIndexOf(':');
        var slashSeparator = image.LastIndexOf('/');
        if (tagSeparator <= slashSeparator)
        {
            if (!PinnedImageVersions.TryGetValue(image, out var fallbackTag) && !PinnedImageVersions.TryGetValue(image.ToLowerInvariant(), out fallbackTag))
            {
                return false;
            }

            pinnedImage = $"{image}:{fallbackTag}";
            return true;
        }

        var tag = image[(tagSeparator + 1)..];
        if (!tag.Equals("latest", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var imageName = image[..tagSeparator];
        if (!PinnedImageVersions.TryGetValue(imageName, out var pinnedTag) && !PinnedImageVersions.TryGetValue(imageName.ToLowerInvariant(), out pinnedTag))
        {
            if (!PinnedImageVersions.TryGetValue(imageName[(slashSeparator + 1)..], out pinnedTag))
            {
                return false;
            }
        }

        pinnedImage = $"{imageName}:{pinnedTag}";
        return true;
    }

    private static void AddLanguageFromImage(string image, HashSet<string> languages)
    {
        if (string.IsNullOrWhiteSpace(image))
        {
            return;
        }

        var lower = image.ToLowerInvariant();
        if (lower.Contains("python"))
        {
            languages.Add("python");
        }
        if (lower.Contains("node"))
        {
            languages.Add("node");
        }
        if (lower.Contains("dotnet"))
        {
            languages.Add("dotnet");
        }
        if (lower.Contains("golang"))
        {
            languages.Add("go");
        }
        if (lower.Contains("go"))
        {
            languages.Add("go");
        }
        if (lower.Contains("rust"))
        {
            languages.Add("rust");
        }
        if (lower.Contains("alpine") || lower.Contains("debian") || lower.Contains("ubuntu"))
        {
            languages.Add("linux");
        }
    }

    private static void InsertBeforeRuntimeInstruction(List<string> lines, IReadOnlyList<string> insertLines)
    {
        var insertAt = lines.FindIndex(line =>
        {
            var trimmed = line.TrimStart();
            return trimmed.StartsWith("CMD ", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("ENTRYPOINT ", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("ONBUILD ", StringComparison.OrdinalIgnoreCase);
        });

        if (insertAt < 0)
        {
            insertAt = lines.Count;
        }

        lines.InsertRange(insertAt, insertLines);
    }

    private static string StripInlineComment(string line)
    {
        var index = line.IndexOf('#');
        if (index < 0)
        {
            return line;
        }

        return line[..index];
    }
}
