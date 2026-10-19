namespace BlazorStatelessLogin.Domain.Login;

public enum LoginState
{
    Anonymous,
    Authenticating,
    Authenticated,
    Failed
}
