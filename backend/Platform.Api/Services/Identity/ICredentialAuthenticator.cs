using Platform.Identity.Enums;

namespace Platform.Api.Services.Identity;

public enum CredentialAuthenticationOutcome
{
    Valid,
    Invalid,

    // IDN-05: nalog je privremeno zaključan. Koristi se samo za audit; klijent vidi isto što i za Invalid.
    LockedOut,
    Unavailable
}

public sealed record CredentialAuthenticationResult(
    CredentialAuthenticationOutcome Outcome,
    Guid? AccountId = null,
    AccountStatus? Status = null);

/// <summary>
/// IDN-02 boundary: verifies the supplied credentials and returns the account id
/// only after successful verification. Implementations must not log the password.
/// </summary>
public interface ICredentialAuthenticator
{
    Task<CredentialAuthenticationResult> AuthenticateAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default);
}
