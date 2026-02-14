using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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

public class ImageBuilderService(ILogger<ImageBuilderService> logger) : IAsyncDisposable
{
    private readonly DockerClient _dockerClient = new DockerClientConfiguration().CreateClient();

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

    public async Task<AiDockerfileResult> GenerateDockerfileWithAiAsync(
        string description,
        string? zaiApiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(zaiApiKey))
        {
            return new AiDockerfileResult(false, string.Empty, "Z.ai API key not configured. Please set up Z.ai credentials in Provider Settings.");
        }

        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(60);
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", zaiApiKey);

            var systemPrompt = @"You are an expert DevOps engineer specializing in creating optimized Dockerfiles. 
Generate a production-ready Dockerfile based on the user's requirements.

Rules:
1. Use best practices for Docker (multi-stage builds when beneficial, minimal layers, proper caching)
2. Always include a non-root user named 'agent' with UID 1000
3. Create /workspace and /artifacts directories owned by the agent user
4. Set proper environment variables (DEBIAN_FRONTEND=noninteractive, HOME=/home/agent)
5. Clean up package manager caches to reduce image size
6. For harness tools (Claude Code, Codex, OpenCode, Zai), set appropriate environment variables
7. Use specific version tags for base images, not 'latest'
8. Include health considerations (no secrets in images, minimal attack surface)
9. The WORKDIR should be /workspace
10. Default CMD should be an interactive shell

Output ONLY the Dockerfile content, no explanations or markdown formatting.";

            var requestBody = new
            {
                model = "glm-4-plus",
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = $"Create a Dockerfile for the following requirements:\n\n{description}" }
                },
                temperature = 0.3,
                max_tokens = 4096
            };

            var response = await httpClient.PostAsJsonAsync(
                "https://open.bigmodel.cn/api/paas/v4/chat/completions",
                requestBody,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning("ZhipuAI API returned {StatusCode}: {Error}", response.StatusCode, errorContent);
                return new AiDockerfileResult(false, string.Empty, $"AI service error: {response.StatusCode}");
            }

            var jsonResponse = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

            if (jsonResponse.TryGetProperty("choices", out var choices) &&
                choices.GetArrayLength() > 0 &&
                choices[0].TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var content))
            {
                var dockerfile = content.GetString() ?? string.Empty;

                dockerfile = System.Text.RegularExpressions.Regex.Replace(
                    dockerfile,
                    @"^```dockerfile?\s*\n|\n```$",
                    "",
                    System.Text.RegularExpressions.RegexOptions.Multiline);
                dockerfile = dockerfile.Trim();

                if (!dockerfile.Contains("FROM", StringComparison.OrdinalIgnoreCase))
                {
                    return new AiDockerfileResult(false, string.Empty, "Generated content does not appear to be a valid Dockerfile");
                }

                logger.LogInformation("Successfully generated Dockerfile via AI for description: {Description}", description);
                return new AiDockerfileResult(true, dockerfile, null);
            }

            return new AiDockerfileResult(false, string.Empty, "Unexpected response format from AI service");
        }
        catch (TaskCanceledException)
        {
            return new AiDockerfileResult(false, string.Empty, "AI request timed out. Please try again.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Network error during AI Dockerfile generation");
            return new AiDockerfileResult(false, string.Empty, $"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating Dockerfile with AI");
            return new AiDockerfileResult(false, string.Empty, $"Error: {ex.Message}");
        }
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
            logger.LogInformation("Building Docker image {Tag}", tag);

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
            logger.LogInformation("Build completed for {Tag}. Success: {Success}, ImageId: {ImageId}", tag, success, imageId);

            return new ImageBuildResult(success, imageId, logs);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception during image build for {Tag}", tag);
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

            logger.LogInformation("Listed {Count} Docker images", result.Count);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception while listing images");
            return [];
        }
    }

    public virtual async Task<bool> DeleteImageAsync(string tag, CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Deleting Docker image {Tag}", tag);

            await _dockerClient.Images.DeleteImageAsync(
                tag,
                new ImageDeleteParameters { Force = true },
                cancellationToken);

            logger.LogInformation("Successfully deleted image {Tag}", tag);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete image {Tag}", tag);
            return false;
        }
    }

    public async Task<bool> TagImageAsync(string sourceTag, string targetTag, CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Tagging image {SourceTag} as {TargetTag}", sourceTag, targetTag);

            var parts = targetTag.Split(':', 2);
            var repo = parts.Length > 0 ? parts[0] : targetTag;
            var tag = parts.Length > 1 ? parts[1] : "latest";

            await _dockerClient.Images.TagImageAsync(
                sourceTag,
                new ImageTagParameters { RepositoryName = repo, Tag = tag },
                cancellationToken);

            logger.LogInformation("Successfully tagged image {SourceTag} as {TargetTag}", sourceTag, targetTag);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to tag image {SourceTag} as {TargetTag}", sourceTag, targetTag);
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
}
