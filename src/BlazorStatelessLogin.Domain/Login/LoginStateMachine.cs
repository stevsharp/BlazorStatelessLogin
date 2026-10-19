using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Stateless;

namespace BlazorStatelessLogin.Domain.Login;

/// <summary>
/// Owns the whole login flow. Callers only <see cref="Submit"/> credentials or <see cref="Logout"/>;
/// the machine drives <c>Authenticating → Authenticated | Failed</c> itself, delegating the
/// credential check to an <see cref="ICredentialValidator"/>.
/// </summary>
public sealed class LoginStateMachine
{
    private const string CredentialsRequired = "Username and password are required";
    private const string InvalidCredentials = "Invalid username or password.";

    private readonly StateMachine<LoginState, LoginTrigger> _machine;
    private readonly StateMachine<LoginState, LoginTrigger>.TriggerWithParameters<string, string> _submit;
    private readonly ICredentialValidator _validator;
    private readonly ILogger _logger;

    public LoginStateMachine(
        LoginState initialState,
        ICredentialValidator validator,
        ILogger? logger = null)
    {
        _validator = validator;
        _logger = logger ?? NullLogger.Instance;
        _machine = new StateMachine<LoginState, LoginTrigger>(initialState);
        _submit = _machine.SetTriggerParameters<string, string>(LoginTrigger.SubmitCredentials);
        Configure();
        _logger.LogInformation("Login machine created in state {State}", initialState);
    }

    public LoginState State => _machine.State;

    /// <summary>The signed-in user, or <c>null</c> unless the machine is in <see cref="LoginState.Authenticated"/>.</summary>
    public AuthenticatedUser? User { get; private set; }

    public string? CurrentUser => User?.Username;

    public string? ErrorMessage { get; private set; }

    public bool IsAuthenticated => State == LoginState.Authenticated;

    /// <summary>Rule helper: true only when authenticated and the user holds <paramref name="role"/>.</summary>
    public bool IsInRole(string role) => IsAuthenticated && User is not null && User.IsInRole(role);

    /// <summary>Rule helper: true only when authenticated and <paramref name="rule"/> accepts the user.</summary>
    public bool Satisfies(Func<AuthenticatedUser, bool> rule) => IsAuthenticated && User is not null && rule(User);

    /// <summary>
    /// Submits credentials. Returns <c>true</c> when the machine ends in <see cref="LoginState.Authenticated"/>.
    /// On any rejection <see cref="ErrorMessage"/> explains why.
    /// </summary>
    public bool Submit(string? username, string? password)
    {
        username ??= string.Empty;
        password ??= string.Empty;
        ErrorMessage = null;

        if (!_machine.CanFire(_submit, username, password))
        {
            ErrorMessage = WhyCannotSubmit(username, password);
            _logger.LogWarning("SubmitCredentials rejected in state {State}: {Reason}", State, ErrorMessage);
            return false;
        }

        // Stateless queues triggers fired from inside entry actions, so by the time this
        // returns the machine has already moved on to Authenticated or Failed.
        _machine.Fire(_submit, username, password);

        return IsAuthenticated;
    }

    public void Logout()
    {
        if (_machine.CanFire(LoginTrigger.Logout))
        {
            _machine.Fire(LoginTrigger.Logout);
        }

        ErrorMessage = null;
    }

    public string WhyCannotSubmit(string username, string password) =>
        State switch
        {
            LoginState.Anonymous or LoginState.Failed when !HasCredentials(username, password) =>
                $"Blocked: {CredentialsRequired}.",
            LoginState.Anonymous or LoginState.Failed =>
                "Unknown reason — all known guards pass.",
            _ => $"No transition '{LoginTrigger.SubmitCredentials}' exists from '{State}'."
        };

    private static bool HasCredentials(string username, string password) =>
        !string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password);

    private void Configure()
    {
        _machine.OnTransitioned(t =>
            _logger.LogInformation("Login transition {Source} --{Trigger}--> {Destination}",
                t.Source, t.Trigger, t.Destination));

        _machine.Configure(LoginState.Anonymous)
            .OnEntry(t =>
            {
                User = null;
                LogEntry(LoginState.Anonymous, t);
            })
            .OnExit(t => LogExit(LoginState.Anonymous, t))
            .PermitIf(_submit, LoginState.Authenticating, HasCredentials, CredentialsRequired);

        _machine.Configure(LoginState.Authenticating)
            .OnEntry(t => LogEntry(LoginState.Authenticating, t))
            .OnEntryFrom(_submit, Authenticate)
            .OnExit(t => LogExit(LoginState.Authenticating, t))
            .Permit(LoginTrigger.Succeed, LoginState.Authenticated)
            .Permit(LoginTrigger.Fail, LoginState.Failed);

        _machine.Configure(LoginState.Failed)
            .OnEntry(t =>
            {
                User = null;
                ErrorMessage = InvalidCredentials;
                LogEntry(LoginState.Failed, t);
            })
            .OnExit(t => LogExit(LoginState.Failed, t))
            .Permit(LoginTrigger.Retry, LoginState.Authenticating)
            .PermitIf(_submit, LoginState.Authenticating, HasCredentials, CredentialsRequired)
            .Permit(LoginTrigger.Logout, LoginState.Anonymous);

        _machine.Configure(LoginState.Authenticated)
            .OnEntry(t => LogEntry(LoginState.Authenticated, t))
            .OnExit(t => LogExit(LoginState.Authenticated, t))
            .Permit(LoginTrigger.Logout, LoginState.Anonymous);
    }

    /// <summary>Runs on entry to <see cref="LoginState.Authenticating"/> and decides the next step.</summary>
    private void Authenticate(string username, string password)
    {
        var trimmed = username.Trim();
        _logger.LogInformation("Validating credentials for {Username}", trimmed);

        var user = _validator.Validate(trimmed, password);
        if (user is not null)
        {
            User = user;
            _logger.LogInformation("User {Username} authenticated with roles [{Roles}]",
                user.Username, string.Join(", ", user.Roles));
            _machine.Fire(LoginTrigger.Succeed);
        }
        else
        {
            _logger.LogWarning("Credential validation failed for {Username}", trimmed);
            _machine.Fire(LoginTrigger.Fail);
        }
    }

    private void LogEntry(LoginState state, StateMachine<LoginState, LoginTrigger>.Transition t) =>
        _logger.LogInformation("Entering state {State} (from {Source} via {Trigger})",
            state, t.Source, t.Trigger);

    private void LogExit(LoginState state, StateMachine<LoginState, LoginTrigger>.Transition t) =>
        _logger.LogInformation("Exiting state {State} (to {Destination} via {Trigger})",
            state, t.Destination, t.Trigger);
}
