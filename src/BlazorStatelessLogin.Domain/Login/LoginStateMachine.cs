using Stateless;

namespace BlazorStatelessLogin.Domain.Login;

public sealed class LoginStateMachine
{
    private readonly StateMachine<LoginState, LoginTrigger> _machine;
    private readonly Func<LoginGuardContext> _contextProvider;

    public LoginStateMachine(LoginState initialState, Func<LoginGuardContext> contextProvider)
    {
        _contextProvider = contextProvider;
        _machine = new StateMachine<LoginState, LoginTrigger>(initialState);
        Configure();
    }

    public LoginState State => _machine.State;

    public bool CanFire(LoginTrigger trigger) => _machine.CanFire(trigger);

    public void Fire(LoginTrigger trigger)
    {
        if (!_machine.CanFire(trigger))
        {
            var reason = WhyCannotFire(trigger);
            throw new InvalidOperationException(
                $"Cannot fire '{trigger}' from state '{_machine.State}'. {reason}");
        }

        _machine.Fire(trigger);
    }

    public string WhyCannotFire(LoginTrigger trigger)
    {
        var ctx = _contextProvider();

        return (_machine.State, trigger) switch
        {
            (LoginState.Anonymous, LoginTrigger.SubmitCredentials) =>
                BuildReason((!ctx.HasCredentials, "Username and password are required")),

            (LoginState.Failed, LoginTrigger.SubmitCredentials) =>
                BuildReason((!ctx.HasCredentials, "Username and password are required")),

            (LoginState.Failed, LoginTrigger.Retry) =>
                BuildReason(),

            (LoginState.Authenticating, LoginTrigger.Succeed) =>
                BuildReason(),

            (LoginState.Authenticating, LoginTrigger.Fail) =>
                BuildReason(),

            (LoginState.Authenticated, LoginTrigger.Logout) =>
                BuildReason(),

            (LoginState.Failed, LoginTrigger.Logout) =>
                BuildReason(),

            _ => $"No transition '{trigger}' exists from '{_machine.State}'."
        };
    }

    private static string BuildReason(params (bool failing, string message)[] checks)
    {
        var failures = checks.Where(c => c.failing).Select(c => c.message).ToArray();
        return failures.Length == 0
            ? "Unknown reason — all known guards pass."
            : "Blocked: " + string.Join("; ", failures) + ".";
    }

    private void Configure()
    {
        _machine.Configure(LoginState.Anonymous)
            .PermitIf(LoginTrigger.SubmitCredentials, LoginState.Authenticating,
                () => _contextProvider().HasCredentials);

        _machine.Configure(LoginState.Authenticating)
            .Permit(LoginTrigger.Succeed, LoginState.Authenticated)
            .Permit(LoginTrigger.Fail, LoginState.Failed);

        _machine.Configure(LoginState.Failed)
            .Permit(LoginTrigger.Retry, LoginState.Authenticating)
            .PermitIf(LoginTrigger.SubmitCredentials, LoginState.Authenticating,
                () => _contextProvider().HasCredentials)
            .Permit(LoginTrigger.Logout, LoginState.Anonymous);

        _machine.Configure(LoginState.Authenticated)
            .Permit(LoginTrigger.Logout, LoginState.Anonymous);
    }
}
