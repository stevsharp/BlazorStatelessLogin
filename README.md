# Blazor Stateless Login

Simple Blazor Server demo login driven by the [Stateless](https://github.com/dotnet-state-machine/stateless) state machine. **No ASP.NET Identity, cookies, or JWT** — in-memory session only.

## Run

```bash
dotnet run --project src/BlazorStatelessLogin
```

Open `/login`. Demo users:

| Username | Password |
|----------|----------|
| admin    | password |
| demo     | demo     |

## Login machine

States: `Anonymous` → `Authenticating` → `Authenticated` | `Failed`

Triggers: `SubmitCredentials`, `Succeed`, `Fail`, `Retry`, `Logout`

Guard: username and password must be non-empty before `SubmitCredentials`.
