using BlazorStatelessLogin.Domain.Login;

namespace BlazorStatelessLogin.Services;

/// <summary>
/// Per-circuit holder for the login machine. It only forwards calls and raises <see cref="Changed"/>;
/// the machine itself owns the flow and the resulting user / error state.
/// </summary>
public sealed class LoginSession(ICredentialValidator validator, ILogger<LoginStateMachine> logger)
{
    private readonly LoginStateMachine _machine = new(LoginState.Anonymous, validator, logger);

    public LoginState State => _machine.State;

    public string? CurrentUser => _machine.CurrentUser;

    public string? ErrorMessage => _machine.ErrorMessage;

    public bool IsAuthenticated => _machine.IsAuthenticated;

    public AuthenticatedUser? User => _machine.User;

    public bool IsInRole(string role) => _machine.IsInRole(role);

    public bool Satisfies(Func<AuthenticatedUser, bool> rule) => _machine.Satisfies(rule);

    public event Action? Changed;

    public bool TryLogin(string username, string password)
    {
        var ok = _machine.Submit(username, password);
        Notify();
        return ok;
    }

    public void Logout()
    {
        _machine.Logout();
        Notify();
    }

    private void Notify() => Changed?.Invoke();
}
