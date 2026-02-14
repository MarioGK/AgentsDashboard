using System.Security.Claims;
using System.Text.Json;
using AgentsDashboard.ControlPlane.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.ControlPlane.Endpoints;

public static class AuthEndpoints
{
    public sealed record LoginRequest(string Username, string Password, string? ReturnUrl);

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/login", async (
            HttpRequest request,
            HttpContext context,
            IOptions<DashboardAuthOptions> authOptions) =>
        {
            LoginRequest payload;
            if (request.HasFormContentType)
            {
                var form = await request.ReadFormAsync();
                payload = new LoginRequest(
                    form["username"].ToString(),
                    form["password"].ToString(),
                    form["returnUrl"].ToString());
            }
            else
            {
                payload = await JsonSerializer.DeserializeAsync<LoginRequest>(request.Body, cancellationToken: context.RequestAborted)
                    ?? new LoginRequest(string.Empty, string.Empty, "/");
            }

            var options = authOptions.Value;
            var user = options.Users.FirstOrDefault(x =>
                string.Equals(x.Username, payload.Username, StringComparison.OrdinalIgnoreCase) &&
                x.Password == payload.Password);

            if (user is null)
            {
                if (request.HasFormContentType)
                {
                    return Results.Redirect("/login?error=invalid");
                }
                return Results.Unauthorized();
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Username),
                new(ClaimTypes.Name, user.Username),
            };

            claims.AddRange(user.Roles.Select(role => new Claim(ClaimTypes.Role, role)));

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

            var redirect = string.IsNullOrWhiteSpace(payload.ReturnUrl) ? "/" : payload.ReturnUrl;
            if (request.HasFormContentType)
            {
                return Results.Redirect(redirect);
            }
            return Results.Ok(new { redirect });
        }).AllowAnonymous().DisableAntiforgery();

        app.MapPost("/auth/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok();
        }).DisableAntiforgery();

        app.MapGet("/auth/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/login");
        }).AllowAnonymous();

        app.MapGet("/auth/me", (HttpContext context) =>
        {
            if (context.User.Identity?.IsAuthenticated != true)
            {
                return Results.Unauthorized();
            }

            var roles = context.User.FindAll(ClaimTypes.Role).Select(x => x.Value).ToArray();
            return Results.Ok(new
            {
                name = context.User.Identity?.Name ?? string.Empty,
                roles,
            });
        }).RequireAuthorization("viewer");

        return app;
    }
}
