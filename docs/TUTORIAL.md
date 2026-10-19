# How the Stateless login works

This walkthrough explains how a Blazor Server form, a scoped session service, and a
[Stateless](https://github.com/dotnet-state-machine/stateless) state machine fit together
to produce a login flow with no ASP.NET Identity, cookies, or tokens. Everything lives in
memory for the lifetime of one Blazor circuit.

Read it top down. Each section builds on the previous one.

---

## 1. The big picture

```
┌───────────────────────────┐
│  Login.razor / NavMenu    │   Blazor components: render state, call TryLogin / Logout
└─────────────┬─────────────┘
              │ inject (scoped)
┌─────────────▼─────────────┐
│  LoginSession             │   One instance per circuit. Forwards calls, raises Changed.
└─────────────┬─────────────┘
              │ owns
┌─────────────▼─────────────┐
│  LoginStateMachine        │   Owns the flow. Decides Succeed / Fail itself.
└─────────────┬─────────────┘
              │ delegates
┌─────────────▼─────────────┐
│  ICredentialValidator     │   "Are these credentials valid?" Nothing else.
└───────────────────────────┘
```

The important design choice is in the middle layer. The state machine does not just
record what happened; it **drives** the flow. The UI never fires `Succeed` or `Fail`.
It submits credentials and the machine takes it from there.

---

## 2. States and triggers

Two enums in `src/BlazorStatelessLogin.Domain/Login` define the vocabulary.

```csharp
public enum LoginState   { Anonymous, Authenticating, Authenticated, Failed }
public enum LoginTrigger { SubmitCredentials, Succeed, Fail, Retry, Logout }
```

The allowed moves:

```
                 SubmitCredentials (guard: both fields non-blank)
   Anonymous ───────────────────────────────────────────► Authenticating
       ▲                                                        │
       │ Logout                                   Succeed  ◄────┴────►  Fail
       │                                             │                   │
   Authenticated ◄───────────────────────────────────┘             Failed
       │                                                             │  ▲
       └──────────────────────── Logout ◄────────────────────────────┘  │
                                                                        │
                   Failed ──SubmitCredentials (same guard)──► Authenticating
```

Rules to notice:

- `Authenticating` is a **transient** state. The machine passes through it in a single
  `Submit` call and never rests there.
- `Succeed` and `Fail` are only valid from `Authenticating`. Nothing outside the machine
  can fire them, so the UI cannot "force" a login.
- `Logout` works from both `Authenticated` and `Failed` and always lands in `Anonymous`.

---

## 3. Configuring the machine

`LoginStateMachine` wraps `StateMachine<LoginState, LoginTrigger>` from Stateless.
Here is the core of `Configure()` with logging stripped out so the shape is visible:

```csharp
_submit = _machine.SetTriggerParameters<string, string>(LoginTrigger.SubmitCredentials);

_machine.Configure(LoginState.Anonymous)
    .OnEntry(_ => CurrentUser = null)
    .PermitIf(_submit, LoginState.Authenticating, HasCredentials, "Username and password are required");

_machine.Configure(LoginState.Authenticating)
    .OnEntryFrom(_submit, Authenticate)
    .Permit(LoginTrigger.Succeed, LoginState.Authenticated)
    .Permit(LoginTrigger.Fail,    LoginState.Failed);

_machine.Configure(LoginState.Failed)
    .OnEntry(_ => { CurrentUser = null; ErrorMessage = "Invalid username or password."; })
    .PermitIf(_submit, LoginState.Authenticating, HasCredentials, "Username and password are required")
    .Permit(LoginTrigger.Logout, LoginState.Anonymous);

_machine.Configure(LoginState.Authenticated)
    .Permit(LoginTrigger.Logout, LoginState.Anonymous);
```

Three Stateless features do the heavy lifting.

### 3.1 Parameterized triggers

```csharp
_submit = _machine.SetTriggerParameters<string, string>(LoginTrigger.SubmitCredentials);
```

This turns `SubmitCredentials` into a trigger that **carries data**. Firing it looks like
`_machine.Fire(_submit, username, password)`. The guard and the entry action both receive
those two strings. Without this, the machine would need some side channel to see what the
user typed. An earlier version of this project used a mutable "guard context" object for
exactly that purpose. Trigger parameters remove it.

### 3.2 Guards with descriptions

```csharp
.PermitIf(_submit, LoginState.Authenticating, HasCredentials, "Username and password are required")
```

`PermitIf` only allows the transition when the guard returns `true`. Here the guard is
a plain static method:

```csharp
private static bool HasCredentials(string username, string password) =>
    !string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password);
```

The fourth argument is a human-readable description. Stateless includes it in the
exception message if someone fires the trigger while the guard is false, and
`WhyCannotSubmit` reuses the same text for the UI.

### 3.3 Entry actions that fire the next trigger

```csharp
_machine.Configure(LoginState.Authenticating)
    .OnEntryFrom(_submit, Authenticate)
```

`OnEntryFrom` runs `Authenticate(username, password)` every time the machine enters
`Authenticating` **via the `_submit` trigger**, and it hands over the trigger's parameters.
That method is where the delegation happens:

```csharp
private void Authenticate(string username, string password)
{
    var trimmed = username.Trim();

    if (_validator.Validate(trimmed, password))
    {
        CurrentUser = trimmed;
        _machine.Fire(LoginTrigger.Succeed);
    }
    else
    {
        _machine.Fire(LoginTrigger.Fail);
    }
}
```

Firing a trigger from inside an entry action is allowed. Stateless runs in **queued
firing mode** by default: the inner `Fire` is enqueued, and the outer `Fire` call
processes the queue before it returns. So when `Submit` calls
`_machine.Fire(_submit, ...)`, control does not come back until the machine has already
moved on to `Authenticated` or `Failed`.

---

## 4. The public surface

Callers see a very small API:

```csharp
public bool Submit(string? username, string? password)
{
    username ??= string.Empty;
    password ??= string.Empty;
    ErrorMessage = null;

    if (!_machine.CanFire(_submit, username, password))
    {
        ErrorMessage = WhyCannotSubmit(username, password);
        return false;
    }

    _machine.Fire(_submit, username, password);
    return IsAuthenticated;
}

public void Logout()
{
    if (_machine.CanFire(LoginTrigger.Logout))
        _machine.Fire(LoginTrigger.Logout);

    ErrorMessage = null;
}
```

Walk through a successful `Submit("admin", "password")` from `Anonymous`:

| Step | What happens | State afterwards |
|---|---|---|
| 1 | `CanFire` evaluates the `HasCredentials` guard. Both non-blank, so `true`. | `Anonymous` |
| 2 | `Fire(_submit, ...)` exits `Anonymous`, enters `Authenticating`. | `Authenticating` |
| 3 | `OnEntryFrom` runs `Authenticate`. The validator says yes. `CurrentUser = "admin"`. | `Authenticating` |
| 4 | `Authenticate` fires `Succeed`. Stateless queues it, then processes it. | `Authenticated` |
| 5 | The outer `Fire` returns. `IsAuthenticated` is `true`, so `Submit` returns `true`. | `Authenticated` |

And a failed one, `Submit("admin", "wrong")`:

| Step | What happens | State afterwards |
|---|---|---|
| 1–2 | Same as above. | `Authenticating` |
| 3 | Validator says no. `Authenticate` fires `Fail`. | `Failed` |
| 4 | `Failed`'s `OnEntry` sets `ErrorMessage = "Invalid username or password."` | `Failed` |
| 5 | `Submit` returns `false`. The UI reads `ErrorMessage`. | `Failed` |

Notice that **`CurrentUser` and `ErrorMessage` are set by entry actions, not by the caller.**
If you move to a new state, the state itself decides what those properties should be. That
keeps the data and the state from drifting apart.

### Explaining a rejection

```csharp
public string WhyCannotSubmit(string username, string password) =>
    State switch
    {
        LoginState.Anonymous or LoginState.Failed when !HasCredentials(username, password) =>
            "Blocked: Username and password are required.",
        LoginState.Anonymous or LoginState.Failed =>
            "Unknown reason — all known guards pass.",
        _ => $"No transition 'SubmitCredentials' exists from '{State}'."
    };
```

Stateless can tell you *that* a trigger is blocked but not *why* in a UI-friendly way.
This method mirrors the `PermitIf` guards by hand. If you add a guard in `Configure()`,
add a matching case here.

---

## 5. Delegation: the validator

The machine knows nothing about users. It asks:

```csharp
public interface ICredentialValidator
{
    bool Validate(string username, string password);
}
```

The web project supplies the demo implementation and registers it as a singleton:

```csharp
// Services/DemoCredentialValidator.cs
public sealed class DemoCredentialValidator : ICredentialValidator
{
    private static readonly Dictionary<string, string> DemoUsers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["admin"] = "password",
        ["demo"]  = "demo"
    };

    public bool Validate(string username, string password) =>
        DemoUsers.TryGetValue(username, out var expected)
        && string.Equals(expected, password, StringComparison.Ordinal);
}

// Program.cs
builder.Services.AddSingleton<ICredentialValidator, DemoCredentialValidator>();
```

Usernames are case-insensitive, passwords are not. To use a database, LDAP, or anything
else, write another `ICredentialValidator` and change that one registration line. The
machine and the UI do not change.

---

## 6. The session: one machine per circuit

Blazor Server keeps a SignalR connection, called a **circuit**, per browser tab. A
service registered as `Scoped` lives exactly as long as that circuit. That makes it the
natural home for login state in an app that has no cookies.

```csharp
builder.Services.AddScoped<LoginSession>();
```

```csharp
public sealed class LoginSession(ICredentialValidator validator, ILogger<LoginStateMachine> logger)
{
    private readonly LoginStateMachine _machine = new(LoginState.Anonymous, validator, logger);

    public LoginState State        => _machine.State;
    public string?    CurrentUser  => _machine.CurrentUser;
    public string?    ErrorMessage => _machine.ErrorMessage;
    public bool       IsAuthenticated => _machine.IsAuthenticated;

    public event Action? Changed;

    public bool TryLogin(string username, string password)
    {
        var ok = _machine.Submit(username, password);
        Changed?.Invoke();
        return ok;
    }

    public void Logout()
    {
        _machine.Logout();
        Changed?.Invoke();
    }
}
```

The session adds exactly one thing the machine lacks: a `Changed` event. Stateless has no
notion of observers, and several components on screen need to re-render when the login
state moves. The session raises `Changed` after every call so they all stay in sync.

Consequences of this design:

- Refreshing the browser starts a new circuit, a new `LoginSession`, and a fresh machine
  in `Anonymous`. You are logged out. That is intentional for this demo.
- Two tabs are two circuits with two independent logins.
- The validator is a singleton, shared by all circuits. It must be stateless and thread-safe.

---

## 7. The components

Every component that shows login state follows the same three-step pattern.

```razor
@inject LoginSession Session
@implements IDisposable

@code {
    protected override void OnInitialized()
        => Session.Changed += OnSessionChanged;            // 1. subscribe

    private void OnSessionChanged()
        => InvokeAsync(StateHasChanged);                    // 2. re-render on the renderer thread

    public void Dispose()
        => Session.Changed -= OnSessionChanged;            // 3. unsubscribe
}
```

Step 3 matters. The session outlives any single page, so a component that forgets to
unsubscribe keeps getting called after it is gone.

`Login.razor` is the only component that writes:

```razor
<EditForm Model="_model" OnValidSubmit="HandleLogin">
    <InputText @bind-Value="_model.Username" ... />
    <InputText @bind-Value="_model.Password" type="password" ... />

    @if (!string.IsNullOrEmpty(Session.ErrorMessage))
    {
        <div class="alert alert-danger">@Session.ErrorMessage</div>
    }

    <button type="submit">Sign in</button>
</EditForm>

@code {
    private void HandleLogin()
    {
        if (Session.TryLogin(_model.Username, _model.Password))
            Navigation.NavigateTo("/");
    }
}
```

The whole login is one synchronous call. There is no `await`, no HTTP round trip, no
redirect dance. The machine finishes before `TryLogin` returns, and the page either shows
the error the machine produced or navigates home.

`NavMenu.razor` and `Home.razor` only read. They check `Session.IsAuthenticated` to decide
whether to show "Login" or "Logout (username)".

---

## 8. Reading the logs

Every state has `OnEntry` and `OnExit` logging, and `OnTransitioned` logs each move. A
successful login prints:

```
info: Login machine created in state Anonymous
info: Exiting state Anonymous (to Authenticating via SubmitCredentials)
info: Login transition Anonymous --SubmitCredentials--> Authenticating
info: Entering state Authenticating (from Anonymous via SubmitCredentials)
info: Validating credentials for admin
info: Exiting state Authenticating (to Authenticated via Succeed)
info: Login transition Authenticating --Succeed--> Authenticated
info: Entering state Authenticated (from Authenticating via Succeed)
```

A wrong password prints the same first four lines, then:

```
warn: Credential validation failed for admin
info: Exiting state Authenticating (to Failed via Fail)
info: Login transition Authenticating --Fail--> Failed
info: Entering state Failed (from Authenticating via Fail)
```

A blank field never reaches the machine's transitions at all:

```
warn: SubmitCredentials rejected in state Anonymous: Blocked: Username and password are required.
```

The logger is `ILogger<LoginStateMachine>`, so the category is
`BlazorStatelessLogin.Domain.Login.LoginStateMachine`. Lower it to `Warning` in
`appsettings.json` to keep only the rejections.

---

## 9. Extending it

**Add a state.** Say you want `Locked` after three failures.

1. Add `Locked` to `LoginState` and `Lock` to `LoginTrigger`.
2. In `Configure()`, permit `Lock` from `Failed` and decide what `Locked` permits.
3. Keep a failure counter in the machine and fire `Lock` from `Failed`'s `OnEntry` when
   it hits the limit. Firing from an entry action is fine, as section 3.3 showed.
4. Add a `Locked` case to `WhyCannotSubmit` so the UI gets a clear message.

**Make validation asynchronous.** Stateless supports `FireAsync`, `OnEntryFromAsync`, and
`PermitIf` guards stay synchronous. Change `ICredentialValidator.Validate` to return
`Task<bool>`, switch `Authenticate` to an async entry action, and have `Submit` become
`SubmitAsync` that awaits `FireAsync`. The session's `TryLogin` becomes async too.

**Persist across refresh.** The machine takes an `initialState` in its constructor. Store
the state and user in `ProtectedSessionStorage`, read them when the session is created,
and construct the machine from that state. This is the one place where the "stateless"
name gets ironic.

---

## 10. Rules: showing a view only to some users

Authentication answers "who are you". Rules answer "what may you see". The project keeps
them separate but lets rules build on the authenticated user.

### 10.1 The validator returns roles, not a bool

```csharp
public sealed record AuthenticatedUser(string Username, IReadOnlySet<string> Roles)
{
    public bool IsInRole(string role) => Roles.Contains(role);
}

public interface ICredentialValidator
{
    AuthenticatedUser? Validate(string username, string password);   // null = invalid
}
```

The demo validator gives `admin` the roles `Admin` and `User`, and `demo` only `User`.
Role names are case-insensitive. The constants live in `Roles`:

```csharp
public static class Roles
{
    public const string Admin = "Admin";
    public const string User  = "User";
}
```

### 10.2 The machine stores the user and exposes rule helpers

When `Authenticate` succeeds it stores the whole `AuthenticatedUser`, not just the name.
Two helpers wrap the common checks and both return `false` unless the machine is in
`Authenticated`:

```csharp
public AuthenticatedUser? User { get; private set; }

public bool IsInRole(string role) =>
    IsAuthenticated && User is not null && User.IsInRole(role);

public bool Satisfies(Func<AuthenticatedUser, bool> rule) =>
    IsAuthenticated && User is not null && rule(User);
```

`LoginSession` forwards `User`, `IsInRole`, and `Satisfies` unchanged.

### 10.3 The `AccessRule` component

`Components/Shared/AccessRule.razor` renders its child content only when every rule you
set passes. It subscribes to `Session.Changed` like every other component, so the view
appears or disappears the moment someone logs in or out.

Role-based:

```razor
<AccessRule Role="@Roles.Admin">
    <p>Only admins see this.</p>
</AccessRule>
```

With a fallback for everyone else:

```razor
<AccessRule Role="@Roles.Admin">
    <ChildContent>
        <p>Only admins see this.</p>
    </ChildContent>
    <Denied>
        <p>You need the Admin role.</p>
    </Denied>
</AccessRule>
```

Any custom predicate over the user:

```razor
<AccessRule Rule="u => u.Username.StartsWith('d')">
    <p>Usernames starting with d only.</p>
</AccessRule>
```

`Role` and `Rule` can be combined. The component's logic is one expression:

```csharp
private bool Allowed =>
    Session.IsAuthenticated
    && (Role is null || Session.IsInRole(Role))
    && (Rule is null || Session.Satisfies(Rule));
```

### 10.4 Where it is used

- `Pages/Admin.razor` at `/admin` wraps its whole body in `<AccessRule Role="@Roles.Admin">`
  with a `Denied` block pointing to the login page.
- `Layout/NavMenu.razor` wraps the "Admin" link in the same rule, so non-admins never see it.

Sign in as `demo` / `demo` and the link is absent and `/admin` shows the denied message.
Sign in as `admin` / `password` and both appear without a page reload.

### 10.5 Adding your own rule

Say only users whose name starts with "a" may see a report.

1. **Inline**, if it is used once:
   ```razor
   <AccessRule Rule="u => u.Username.StartsWith('a', StringComparison.OrdinalIgnoreCase)">
   ```
2. **Named**, if it is reused. Add a static class in the Domain project:
   ```csharp
   public static class LoginRules
   {
       public static bool CanSeeReports(AuthenticatedUser u) =>
           u.IsInRole(Roles.Admin) || u.Username.StartsWith('a');
   }
   ```
   and use `<AccessRule Rule="LoginRules.CanSeeReports">`.
3. **As a role**, if it represents a permission the credential store should own. Add a
   constant to `Roles`, grant it in the validator, and use `Role="..."`.

A rule is a read-only check over the user. It never changes machine state. If you need a
state change (for example, lock the account after too many failures), that belongs in the
machine as a new state and trigger, as described in section 9.

---

## 11. One known rough edge

`Failed` permits a bare `Retry` trigger into `Authenticating`. Because `Retry` carries no
parameters, `OnEntryFrom(_submit, ...)` does not run, and the machine would sit in
`Authenticating` forever. Nothing in the app fires `Retry` today. Either remove it or give
it parameters before using it.
