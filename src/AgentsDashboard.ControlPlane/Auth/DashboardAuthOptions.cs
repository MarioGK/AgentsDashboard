namespace AgentsDashboard.ControlPlane.Auth;

public sealed class DashboardAuthOptions
{
    public const string SectionName = "Authentication";

    public bool Enabled { get; set; } = true;
    public List<DashboardAuthUser> Users { get; set; } =
    [
        new DashboardAuthUser
        {
            Username = "admin",
            Password = "change-me",
            Roles = ["admin", "operator", "viewer"],
        }
    ];
}

public sealed class DashboardAuthUser
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = [];
}
