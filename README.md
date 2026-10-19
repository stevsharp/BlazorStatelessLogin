# Blazor Stateless Login

Simple Blazor Server demo login driven by the [Stateless](https://github.com/dotnet-state-machine/stateless) state machine. **No ASP.NET Identity, cookies, or JWT** — in-memory session only.

## Run

```bash
dotnet run --project src/BlazorStatelessLogin
```

Open `/login`. Demo users:

| Username | Password | Roles       |
|----------|----------|-------------|
| admin    | password | Admin, User |
| demo     | demo     | User        |

`/admin` and the "Admin" nav link are only visible to the Admin role.

See [docs/TUTORIAL.md](docs/TUTORIAL.md) for a step-by-step walkthrough of how the machine, session, and components fit together.

## Login machine

States: `Anonymous` → `Authenticating` → `Authenticated` | `Failed`

Triggers: `SubmitCredentials`, `Succeed`, `Fail`, `Retry`, `Logout`

Guard: username and password must be non-empty before `SubmitCredentials`.
