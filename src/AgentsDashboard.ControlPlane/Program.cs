using AgentsDashboard.ControlPlane;
using AgentsDashboard.ControlPlane.Components;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Services;
using System.Security.Cryptography.X509Certificates;
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
