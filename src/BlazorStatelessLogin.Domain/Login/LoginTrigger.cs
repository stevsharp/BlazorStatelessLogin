namespace BlazorStatelessLogin.Domain.Login;

public enum LoginTrigger
{
    SubmitCredentials,
    Succeed,
    Fail,
    Retry,
    Logout
}
