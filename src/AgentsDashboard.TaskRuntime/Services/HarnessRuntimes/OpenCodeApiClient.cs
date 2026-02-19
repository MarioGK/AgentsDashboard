using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace AgentsDashboard.TaskRuntime.Services.HarnessRuntimes;


internal sealed class OpenCodeApiClient : IDisposable
{
    private static readonly JsonElement s_emptyObject = JsonSerializer.SerializeToElement(new Dictionary<string, string>());

    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public OpenCodeApiClient(Uri baseAddress, string? username, string? password)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = baseAddress,
        };

        if (!string.IsNullOrWhiteSpace(password))
        {
            var user = string.IsNullOrWhiteSpace(username) ? "opencode" : username.Trim();
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        }
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync("/global/health", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return json.RootElement.TryGetProperty("healthy", out var healthy) &&
                   healthy.ValueKind == JsonValueKind.True;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> CreateSessionAsync(
        string directory,
        object requestBody,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            BuildPath("/session", directory),
            requestBody,
            _jsonOptions,
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var sessionId = ReadString(json.RootElement, "id");
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("OpenCode create session response did not include an id.");
        }

        return sessionId;
    }

    public async Task PromptAsync(
        string sessionId,
        string directory,
        object requestBody,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            BuildPath($"/session/{sessionId}/prompt_async", directory),
            requestBody,
            _jsonOptions,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NoContent || response.IsSuccessStatusCode)
        {
            return;
        }

        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<JsonDocument> GetMessagesAsync(
        string sessionId,
        string directory,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            BuildPath($"/session/{sessionId}/message", directory),
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    public async Task<JsonDocument> GetDiffAsync(
        string sessionId,
        string directory,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            BuildPath($"/session/{sessionId}/diff", directory),
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    public async Task<JsonDocument> GetStatusesAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            BuildPath("/session/status", directory),
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    public async IAsyncEnumerable<OpenCodeSseEvent> SubscribeEventsAsync(
        string directory,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildPath("/event", directory));
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var data = new StringBuilder();

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                data.AppendLine(line[5..].TrimStart());
                continue;
            }

            if (line.Length != 0)
            {
                continue;
            }

            if (data.Length == 0)
            {
                continue;
            }

            var payload = data.ToString().Trim();
            data.Clear();
            var parsed = TryParseEvent(payload);
            if (parsed is not null)
            {
                yield return parsed;
            }
        }

        if (data.Length > 0)
        {
            var payload = data.ToString().Trim();
            var parsed = TryParseEvent(payload);
            if (parsed is not null)
            {
                yield return parsed;
            }
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private static string BuildPath(string path, string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return path;
        }

        var encoded = Uri.EscapeDataString(directory);
        return $"{path}?directory={encoded}";
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(
            $"OpenCode request failed with status {(int)response.StatusCode}: {body}",
            null,
            response.StatusCode);
    }

    private static OpenCodeSseEvent? TryParseEvent(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(payload);
            if (!json.RootElement.TryGetProperty("type", out var typeElement))
            {
                return null;
            }

            var type = typeElement.GetString();
            if (string.IsNullOrWhiteSpace(type))
            {
                return null;
            }

            var properties = json.RootElement.TryGetProperty("properties", out var props)
                ? props.Clone()
                : s_emptyObject;

            return new OpenCodeSseEvent(type, properties);
        }
        catch
        {
            return null;
        }
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }
}
