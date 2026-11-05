using System.Security.Cryptography;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;

namespace Platform.Api.Services.Identity;

/// <summary>
/// Safe default until IDN-02 supplies real credential verification.
/// Login fails closed instead of accepting an unverified password.
/// </summary>
public sealed class UnavailableCredentialAuthenticator : ICredentialAuthenticator
{
    public Task<CredentialAuthenticationResult> AuthenticateAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new CredentialAuthenticationResult(CredentialAuthenticationOutcome.Unavailable));
}

/// <summary>
/// Safe default until IDN-02 supplies persistent, revocable sessions.
/// </summary>
public sealed class UnavailableSessionIssuer : ISessionIssuer
{
    public SessionIssueResult Issue(Guid accountId) => new(false);
}

/// <summary>
/// PRIVREMENO (IDN-05), samo za Development i Testing dok se IDN-02 ne spoji.
/// Vraća nasumičan token koji se nigde ne čuva i ne daje pristup ničemu; nije prava sesija.
/// Kad IDN-02 bude na main grani, ovu klasu treba obrisati i registrovati pravi ISessionIssuer.
/// </summary>
public sealed class FakeSessionIssuer : ISessionIssuer
{
    public SessionIssueResult Issue(Guid accountId) =>
        new(true, "fake-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant());
}
