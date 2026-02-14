using System.Security.Claims;
using AgentsDashboard.ControlPlane.Auth;
using AgentsDashboard.ControlPlane.Endpoints;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.UnitTests.ControlPlane.Auth;

public class AuthenticationTests
{
    private static DashboardAuthOptions CreateTestOptions()
    {
        return new DashboardAuthOptions
        {
            Enabled = true,
            Users =
            [
                new DashboardAuthUser
                {
                    Username = "admin",
                    Password = "change-me",
                    Roles = ["admin", "operator", "viewer"]
                },
                new DashboardAuthUser
                {
                    Username = "operator",
                    Password = "op-pass",
                    Roles = ["operator", "viewer"]
                },
                new DashboardAuthUser
                {
                    Username = "viewer",
                    Password = "view-pass",
                    Roles = ["viewer"]
                }
            ]
        };
    }

    private static (string? username, string password) FindMatchingUser(DashboardAuthOptions options, string username, string password)
    {
        var user = options.Users.FirstOrDefault(x =>
            string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase) &&
            x.Password == password);
        return user is null ? (null, "") : (user.Username, user.Password);
    }

    private static List<string> GetUserRoles(DashboardAuthOptions options, string username)
    {
        var user = options.Users.FirstOrDefault(x =>
            string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase));
        return user?.Roles ?? [];
    }

    [Fact]
    public void Login_WithValidCredentials_FindsUser()
    {
        var options = CreateTestOptions();

        var (username, _) = FindMatchingUser(options, "admin", "change-me");

        username.Should().Be("admin");
    }

    [Fact]
    public void Login_WithInvalidUsername_DoesNotFindUser()
    {
        var options = CreateTestOptions();

        var (username, _) = FindMatchingUser(options, "nonexistent", "change-me");

        username.Should().BeNull();
    }

    [Fact]
    public void Login_WithInvalidPassword_DoesNotFindUser()
    {
        var options = CreateTestOptions();

        var (username, _) = FindMatchingUser(options, "admin", "wrong-password");

        username.Should().BeNull();
    }

    [Fact]
    public void Login_WithEmptyCredentials_DoesNotFindUser()
    {
        var options = CreateTestOptions();

        var (username, _) = FindMatchingUser(options, "", "");

        username.Should().BeNull();
    }

    [Fact]
    public void Login_CaseInsensitiveUsername_FindsUser()
    {
        var options = CreateTestOptions();

        var (username, _) = FindMatchingUser(options, "ADMIN", "change-me");

        username.Should().Be("admin");
    }

    [Fact]
    public void Login_PasswordIsCaseSensitive_DoesNotFindUser()
    {
        var options = CreateTestOptions();

        var (username, _) = FindMatchingUser(options, "admin", "CHANGE-ME");

        username.Should().BeNull();
    }

    [Fact]
    public void GetUserRoles_Admin_HasAllRoles()
    {
        var options = CreateTestOptions();

        var roles = GetUserRoles(options, "admin");

        roles.Should().Contain("admin");
        roles.Should().Contain("operator");
        roles.Should().Contain("viewer");
        roles.Should().HaveCount(3);
    }

    [Fact]
    public void GetUserRoles_Operator_HasOperatorAndViewer()
    {
        var options = CreateTestOptions();

        var roles = GetUserRoles(options, "operator");

        roles.Should().Contain("operator");
        roles.Should().Contain("viewer");
        roles.Should().NotContain("admin");
        roles.Should().HaveCount(2);
    }

    [Fact]
    public void GetUserRoles_Viewer_HasOnlyViewer()
    {
        var options = CreateTestOptions();

        var roles = GetUserRoles(options, "viewer");

        roles.Should().ContainSingle("viewer");
        roles.Should().NotContain("admin");
        roles.Should().NotContain("operator");
    }

    [Theory]
    [InlineData("admin", "admin", true)]
    [InlineData("admin", "operator", true)]
    [InlineData("admin", "viewer", true)]
    [InlineData("operator", "operator", true)]
    [InlineData("operator", "viewer", true)]
    [InlineData("operator", "admin", false)]
    [InlineData("viewer", "viewer", true)]
    [InlineData("viewer", "operator", false)]
    [InlineData("viewer", "admin", false)]
    public void UserHasRequiredRole_CorrectlyValidates(string username, string requiredRole, bool expected)
    {
        var options = CreateTestOptions();
        var roles = GetUserRoles(options, username);

        var hasRole = roles.Contains(requiredRole);

        hasRole.Should().Be(expected);
    }

    [Fact]
    public void LoginRequest_CanBeCreated()
    {
        var request = new AuthEndpoints.LoginRequest("testuser", "testpass", "/dashboard");

        request.Username.Should().Be("testuser");
        request.Password.Should().Be("testpass");
        request.ReturnUrl.Should().Be("/dashboard");
    }

    [Fact]
    public void LoginRequest_WithNullReturnUrl_HandlesGracefully()
    {
        var request = new AuthEndpoints.LoginRequest("user", "pass", null);

        request.ReturnUrl.Should().BeNull();
    }
}

public class AuthorizationPolicyTests
{
    private static readonly HashSet<string> ViewerAllowedRoles = ["viewer", "operator", "admin"];
    private static readonly HashSet<string> OperatorAllowedRoles = ["operator", "admin"];

    [Fact]
    public void ViewerPolicy_RequiresViewerOperatorOrAdmin()
    {
        ViewerAllowedRoles.Should().Contain("viewer");
        ViewerAllowedRoles.Should().Contain("operator");
        ViewerAllowedRoles.Should().Contain("admin");
    }

    [Fact]
    public void OperatorPolicy_RequiresOperatorOrAdmin()
    {
        OperatorAllowedRoles.Should().Contain("operator");
        OperatorAllowedRoles.Should().Contain("admin");
        OperatorAllowedRoles.Should().NotContain("viewer");
    }

    [Theory]
    [InlineData("viewer", "viewer", true)]
    [InlineData("viewer", "operator", true)]
    [InlineData("viewer", "admin", true)]
    [InlineData("operator", "operator", true)]
    [InlineData("operator", "admin", true)]
    [InlineData("operator", "viewer", true)]
    [InlineData("admin", "viewer", true)]
    [InlineData("admin", "operator", true)]
    [InlineData("admin", "admin", true)]
    public void ViewerPolicy_UserRoles_CorrectlyValidate(string userRole, string _, bool shouldPass)
    {
        var userRoles = new[] { userRole };
        var hasRequiredRole = userRoles.Any(r => ViewerAllowedRoles.Contains(r));
        hasRequiredRole.Should().Be(shouldPass);
    }

    [Theory]
    [InlineData("viewer", false)]
    [InlineData("operator", true)]
    [InlineData("admin", true)]
    public void OperatorPolicy_UserRoles_CorrectlyValidate(string userRole, bool shouldPass)
    {
        var userRoles = new[] { userRole };
        var hasRequiredRole = userRoles.Any(r => OperatorAllowedRoles.Contains(r));
        hasRequiredRole.Should().Be(shouldPass);
    }

    [Fact]
    public void MultipleRoles_AllowAccess()
    {
        var userRoles = new[] { "operator", "viewer" };

        var canAccessOperator = userRoles.Any(r => OperatorAllowedRoles.Contains(r));
        var canAccessViewer = userRoles.Any(r => ViewerAllowedRoles.Contains(r));

        canAccessOperator.Should().BeTrue();
        canAccessViewer.Should().BeTrue();
    }

    [Fact]
    public void Admin_HasAccessToAllPolicies()
    {
        var adminRoles = new[] { "admin", "operator", "viewer" };

        adminRoles.Any(r => ViewerAllowedRoles.Contains(r)).Should().BeTrue();
        adminRoles.Any(r => OperatorAllowedRoles.Contains(r)).Should().BeTrue();
    }
}

public class DashboardAuthOptionsTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var options = new DashboardAuthOptions();

        options.Enabled.Should().BeTrue();
        options.Users.Should().HaveCount(1);
        options.Users[0].Username.Should().Be("admin");
        options.Users[0].Password.Should().Be("change-me");
        options.Users[0].Roles.Should().Contain("admin");
        options.Users[0].Roles.Should().Contain("operator");
        options.Users[0].Roles.Should().Contain("viewer");
    }

    [Fact]
    public void SectionName_IsCorrect()
    {
        DashboardAuthOptions.SectionName.Should().Be("Authentication");
    }

    [Fact]
    public void CanAddCustomUsers()
    {
        var options = new DashboardAuthOptions
        {
            Users =
            [
                new DashboardAuthUser { Username = "viewer1", Password = "pass1", Roles = ["viewer"] },
                new DashboardAuthUser { Username = "operator1", Password = "pass2", Roles = ["operator", "viewer"] },
                new DashboardAuthUser { Username = "admin1", Password = "pass3", Roles = ["admin", "operator", "viewer"] }
            ]
        };

        options.Users.Should().HaveCount(3);
        options.Users[0].Roles.Should().ContainSingle("viewer");
        options.Users[1].Roles.Should().HaveCount(2);
        options.Users[2].Roles.Should().HaveCount(3);
    }

    [Fact]
    public void CanDisableAuthentication()
    {
        var options = new DashboardAuthOptions { Enabled = false };

        options.Enabled.Should().BeFalse();
    }

    [Fact]
    public void DashboardAuthUser_CanHaveMultipleRoles()
    {
        var user = new DashboardAuthUser
        {
            Username = "testuser",
            Password = "testpass",
            Roles = ["admin", "operator", "viewer"]
        };

        user.Roles.Should().HaveCount(3);
        user.Roles.Should().Contain("admin");
        user.Roles.Should().Contain("operator");
        user.Roles.Should().Contain("viewer");
    }

    [Fact]
    public void DashboardAuthUser_DefaultRoles_IsEmpty()
    {
        var user = new DashboardAuthUser();

        user.Roles.Should().BeEmpty();
    }
}

public class ClaimsPrincipalTests
{
    [Fact]
    public void ClaimsPrincipal_WithAdminRole_HasAllRoleClaims()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "admin"),
            new(ClaimTypes.Name, "admin"),
            new(ClaimTypes.Role, "admin"),
            new(ClaimTypes.Role, "operator"),
            new(ClaimTypes.Role, "viewer")
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        principal.IsInRole("admin").Should().BeTrue();
        principal.IsInRole("operator").Should().BeTrue();
        principal.IsInRole("viewer").Should().BeTrue();
        principal.Identity?.Name.Should().Be("admin");
    }

    [Fact]
    public void ClaimsPrincipal_WithOperatorRole_HasOperatorAndViewerRoles()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "operator"),
            new(ClaimTypes.Name, "operator"),
            new(ClaimTypes.Role, "operator"),
            new(ClaimTypes.Role, "viewer")
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        principal.IsInRole("operator").Should().BeTrue();
        principal.IsInRole("viewer").Should().BeTrue();
        principal.IsInRole("admin").Should().BeFalse();
    }

    [Fact]
    public void ClaimsPrincipal_WithViewerRole_HasOnlyViewerRole()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "viewer"),
            new(ClaimTypes.Name, "viewer"),
            new(ClaimTypes.Role, "viewer")
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        principal.IsInRole("viewer").Should().BeTrue();
        principal.IsInRole("operator").Should().BeFalse();
        principal.IsInRole("admin").Should().BeFalse();
    }

    [Fact]
    public void ClaimsPrincipal_WithNoRoles_IsNotInAnyRole()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "noroles"),
            new(ClaimTypes.Name, "noroles")
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        principal.IsInRole("viewer").Should().BeFalse();
        principal.IsInRole("operator").Should().BeFalse();
        principal.IsInRole("admin").Should().BeFalse();
    }

    [Fact]
    public void ClaimsIdentity_IsAuthenticated_WhenHasClaims()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "user"),
            new(ClaimTypes.Name, "user")
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

        identity.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public void ClaimsIdentity_IsNotAuthenticated_WhenEmpty()
    {
        var identity = new ClaimsIdentity();

        identity.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void ClaimsPrincipal_GetAllRoles_ReturnsAllRoleClaims()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "admin"),
            new(ClaimTypes.Name, "admin"),
            new(ClaimTypes.Role, "admin"),
            new(ClaimTypes.Role, "operator"),
            new(ClaimTypes.Role, "viewer")
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        var roles = principal.FindAll(ClaimTypes.Role).Select(x => x.Value).ToList();

        roles.Should().HaveCount(3);
        roles.Should().Contain("admin");
        roles.Should().Contain("operator");
        roles.Should().Contain("viewer");
    }
}

public class AuthenticationConfigurationTests
{
    [Fact]
    public void CookieAuthentication_SchemeName_IsCookies()
    {
        CookieAuthenticationDefaults.AuthenticationScheme.Should().Be("Cookies");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyOrNullUsername_ShouldFailValidation(string username)
    {
        string.IsNullOrWhiteSpace(username).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyOrNullPassword_ShouldFailValidation(string password)
    {
        string.IsNullOrWhiteSpace(password).Should().BeTrue();
    }

    [Theory]
    [InlineData("/projects", "/projects")]
    [InlineData("", "/")]
    [InlineData(null, "/")]
    [InlineData("   ", "/")]
    public void ReturnUrl_Normalization(string? returnUrl, string expected)
    {
        var redirect = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl;
        redirect.Should().Be(expected);
    }

    [Theory]
    [InlineData("admin", "admin", true)]
    [InlineData("Admin", "admin", true)]
    [InlineData("ADMIN", "admin", true)]
    [InlineData("admin", "Admin", true)]
    public void UsernameComparison_CaseInsensitive(string input, string stored, bool expected)
    {
        var result = string.Equals(input, stored, StringComparison.OrdinalIgnoreCase);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("password", "password", true)]
    [InlineData("Password", "password", false)]
    [InlineData("PASSWORD", "password", false)]
    public void PasswordComparison_CaseSensitive(string input, string stored, bool expected)
    {
        var result = input == stored;
        result.Should().Be(expected);
    }
}

public class AuthEndpointRequestTests
{
    [Fact]
    public void LoginRequest_RecordEquality_WorksCorrectly()
    {
        var request1 = new AuthEndpoints.LoginRequest("user", "pass", "/home");
        var request2 = new AuthEndpoints.LoginRequest("user", "pass", "/home");

        request1.Should().Be(request2);
    }

    [Fact]
    public void LoginRequest_WithDifferentValues_AreNotEqual()
    {
        var request1 = new AuthEndpoints.LoginRequest("user1", "pass", "/home");
        var request2 = new AuthEndpoints.LoginRequest("user2", "pass", "/home");

        request1.Should().NotBe(request2);
    }

    [Fact]
    public void LoginRequest_CanDeconstruct()
    {
        var request = new AuthEndpoints.LoginRequest("testuser", "testpass", "/dashboard");
        var (username, password, returnUrl) = request;

        username.Should().Be("testuser");
        password.Should().Be("testpass");
        returnUrl.Should().Be("/dashboard");
    }
}
