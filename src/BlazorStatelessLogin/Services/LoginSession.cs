using BlazorStatelessLogin.Domain.Login;

namespace BlazorStatelessLogin.Services;

public sealed class LoginSession
{
    private static readonly Dictionary<string, string> DemoUsers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["admin"] = "password",
        ["demo"] = "demo"
    };

    private readonly LoginGuardContext _guards = new();
    private readonly LoginStateMachine _machine;

    public LoginSession()
    {
        _machine = new LoginStateMachine(LoginState.Anonymous, () => _guards);
    }

    public LoginState State => _machine.State;

    public string? CurrentUser { get; private set; }

    public string? ErrorMessage { get; private set; }

    public bool IsAuthenticated => State == LoginState.Authenticated;

    public event Action? Changed;

    public bool TryLogin(string username, string password)
    {
        ErrorMessage = null;
        _guards.Username = username ?? string.Empty;
        _guards.Password = password ?? string.Empty;

        if (!_machine.CanFire(LoginTrigger.SubmitCredentials))
        {
            ErrorMessage = _machine.WhyCannotFire(LoginTrigger.SubmitCredentials);
            Notify();
            return false;
        }

        _machine.Fire(LoginTrigger.SubmitCredentials);

        if (IsValidCredentials(_guards.Username, _guards.Password))
        {
            _machine.Fire(LoginTrigger.Succeed);
            CurrentUser = _guards.Username.Trim();
            ErrorMessage = null;
            Notify();
            return true;
        }

        _machine.Fire(LoginTrigger.Fail);
        CurrentUser = null;
        ErrorMessage = "Invalid username or password.";
        Notify();
        return false;
    }

    public void Logout()
    {
        if (_machine.CanFire(LoginTrigger.Logout))
        {
            _machine.Fire(LoginTrigger.Logout);
        }

        CurrentUser = null;
        ErrorMessage = null;
        _guards.Username = string.Empty;
        _guards.Password = string.Empty;
        Notify();
    }

    private static bool IsValidCredentials(string username, string password) =>
        DemoUsers.TryGetValue(username.Trim(), out var expected)
        && string.Equals(expected, password, StringComparison.Ordinal);

    private void Notify() => Changed?.Invoke();
}
