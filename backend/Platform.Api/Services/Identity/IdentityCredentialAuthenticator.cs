using Platform.Identity.Enums;
using Platform.Identity.Interfaces;

namespace Platform.Api.Services.Identity;

/// <summary>
/// IDN-05: provera lozinke iz Platform.Identity (AppUser, lockoutOnFailure) iza IDN-02 granice.
/// </summary>
public sealed class IdentityCredentialAuthenticator(IPasswordSignInCheck check) : ICredentialAuthenticator
{
    public async Task<CredentialAuthenticationResult> AuthenticateAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        var result = await check.CheckAsync(email, password, cancellationToken);

        return result.Outcome switch
        {
            PasswordCheckOutcome.Valid => new(
                CredentialAuthenticationOutcome.Valid, result.AccountId, result.Status),
            PasswordCheckOutcome.LockedOut => new(
                CredentialAuthenticationOutcome.LockedOut, result.AccountId),
            _ => new(CredentialAuthenticationOutcome.Invalid)
        };
    }
}
