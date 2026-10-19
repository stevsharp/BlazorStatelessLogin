namespace BlazorStatelessLogin.Domain.Login;

/// <summary>Who is signed in and what they are allowed to do. Produced by <see cref="ICredentialValidator"/>.</summary>
public sealed record AuthenticatedUser(string Username, IReadOnlySet<string> Roles)
{
    public static AuthenticatedUser Create(string username, params string[] roles) =>
        new(username, new HashSet<string>(roles, StringComparer.OrdinalIgnoreCase));

    public bool IsInRole(string role) => Roles.Contains(role);
}

/// <summary>Well-known role names so callers do not scatter magic strings.</summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string User = "User";
}
