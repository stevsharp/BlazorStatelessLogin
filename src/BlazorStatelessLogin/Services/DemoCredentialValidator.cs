using BlazorStatelessLogin.Domain.Login;

namespace BlazorStatelessLogin.Services;

/// <summary>Hardcoded demo users. Swap this registration for a real store when needed.</summary>
public sealed class DemoCredentialValidator : ICredentialValidator
{
    private sealed record DemoAccount(string Password, string[] Roles);

    private static readonly Dictionary<string, DemoAccount> DemoUsers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["admin"] = new("password", [Roles.Admin, Roles.User]),
        ["demo"] = new("demo", [Roles.User])
    };

    public AuthenticatedUser? Validate(string username, string password) =>
        DemoUsers.TryGetValue(username, out var account)
        && string.Equals(account.Password, password, StringComparison.Ordinal)
            ? AuthenticatedUser.Create(username, account.Roles)
            : null;
}
