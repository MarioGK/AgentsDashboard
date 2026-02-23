using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Collections.Generic;
using AgentsDashboard.ControlPlane;
using AgentsDashboard.ControlPlane.Components;
using ZLogger;

AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddStructuredContainerLogging("ControlPlane");

builder.Services.AddControlPlaneHealthChecks();
builder.Services.AddOptions<OrchestratorOptions>()
    .Bind(builder.Configuration.GetSection(OrchestratorOptions.SectionName))
    .PostConfigure(options =>
    {
        options.LiteDbPath = RepositoryPathResolver.ResolveDataPath(
            options.LiteDbPath,
            OrchestratorOptions.DefaultLiteDbPath);
        options.ArtifactsRootPath = RepositoryPathResolver.ResolveDataPath(
            options.ArtifactsRootPath,
            OrchestratorOptions.DefaultArtifactsRootPath);
    })
    .ValidateOnStart();

var startupOptions = builder.Configuration.GetSection(OrchestratorOptions.SectionName).Get<OrchestratorOptions>();

if (startupOptions is not null)
{
    startupOptions.LiteDbPath.EnsureLiteDbDirectoryExists();
    startupOptions.ArtifactsRootPath.EnsureArtifactsDirectoryExists();
}

builder.Services.AddControlPlaneServices(builder.Environment.IsDevelopment());

ConfigureDevelopmentFallbackPort(builder);
UseTerrascaleCertificateIfAvailable(builder);

var app = builder.Build();
Microsoft.AspNetCore.Hosting.StaticWebAssets.StaticWebAssetsLoader.UseStaticWebAssets(app.Environment, app.Configuration);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
if (app.Urls.Any(url => url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
{
    app.UseHttpsRedirection();
}
app.UseAntiforgery();

app.MapStaticAssets();

app.MapMagicOnionService();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapControlPlaneHealthChecks();

app.Run();

static void ConfigureDevelopmentFallbackPort(WebApplicationBuilder builder)
{
    if (!builder.Environment.IsDevelopment())
    {
        return;
    }

    var configuredUrlsRaw = builder.Configuration["ASPNETCORE_URLS"];
    if (string.IsNullOrWhiteSpace(configuredUrlsRaw))
    {
        return;
    }
    var configuredUrls = configuredUrlsRaw;

    if (!TryGetConfiguredHttpsUrlAndPort(configuredUrls, out var configuredHttpsUrl, out var configuredPort))
    {
        return;
    }

    if (!IsPortInUse(configuredPort))
    {
        return;
    }

    var fallbackPort = FindAvailablePort(startPort: configuredPort + 1, endPort: configuredPort + 10);
    if (fallbackPort is null)
    {
        return;
    }

    var fallbackUrl = configuredUrls.Replace(
        configuredHttpsUrl,
        $"https://0.0.0.0:{fallbackPort}",
        StringComparison.OrdinalIgnoreCase);
    builder.WebHost.UseUrls(fallbackUrl);
    Console.WriteLine($"ControlPlane local HTTPS URL {configuredPort} is in use; falling back to {fallbackUrl}.");
}

static bool TryGetConfiguredHttpsUrlAndPort(
    string? configuredUrls,
    out string configuredHttpsUrl,
    out int configuredPort)
{
    configuredHttpsUrl = string.Empty;
    configuredPort = 0;

    if (string.IsNullOrWhiteSpace(configuredUrls))
    {
        return false;
    }

    var urls = configuredUrls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    foreach (var url in urls)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsedUri))
        {
            continue;
        }

        if (!string.Equals(parsedUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        if (!string.Equals(parsedUri.Host, "0.0.0.0", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        if (parsedUri.Port <= 0)
        {
            continue;
        }

        configuredHttpsUrl = $"{parsedUri.Scheme}://{parsedUri.Host}:{parsedUri.Port}";
        configuredPort = parsedUri.Port;
        return true;
    }

    return false;
}

static int? FindAvailablePort(int startPort, int endPort)
{
    for (var port = startPort; port <= endPort; port++)
    {
        if (!IsPortInUse(port))
        {
            return port;
        }
    }

    return null;
}

static bool IsPortInUse(int port)
{
    using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    try
    {
        socket.Bind(new IPEndPoint(IPAddress.Any, port));
        return false;
    }
    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
    {
        return true;
    }
}

static void UseTerrascaleCertificateIfAvailable(WebApplicationBuilder builder)
{
    if (!builder.Environment.IsDevelopment())
    {
        return;
    }

    var configuredCertificatePath = builder.Configuration["ASPNETCORE_Kestrel__Certificates__Default__Path"];
    var configuredCertificateKeyPath = builder.Configuration["ASPNETCORE_Kestrel__Certificates__Default__KeyPath"];
    if (!string.IsNullOrWhiteSpace(configuredCertificatePath) || !string.IsNullOrWhiteSpace(configuredCertificateKeyPath))
    {
        return;
    }

    var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    var certPath = Path.Combine(localAppData, "mkcert", "terrascale-dev.pem");
    var keyPath = Path.Combine(localAppData, "mkcert", "terrascale-dev-key.pem");
    if (!File.Exists(certPath) || !File.Exists(keyPath))
    {
        return;
    }

    var certificate = X509Certificate2.CreateFromPemFile(certPath, keyPath);
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ConfigureHttpsDefaults(httpsDefaults =>
        {
            httpsDefaults.ServerCertificate = certificate;
        });
    });
}
