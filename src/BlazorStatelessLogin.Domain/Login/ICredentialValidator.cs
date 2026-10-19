namespace BlazorStatelessLogin.Domain.Login;

/// <summary>
/// The machine delegates the actual credential check to this when it enters <see cref="LoginState.Authenticating"/>.
/// </summary>
public interface ICredentialValidator
{
    /// <summary>Returns the authenticated user with their roles, or <c>null</c> when the credentials are invalid.</summary>
    AuthenticatedUser? Validate(string username, string password);
}
