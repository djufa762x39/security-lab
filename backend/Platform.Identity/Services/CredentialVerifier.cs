using Platform.Identity.Interfaces;
using Platform.Identity.Models;

namespace Platform.Identity.Services;

public sealed class CredentialVerifier(ICredentialStore store, IPasswordHasher hasher) : ICredentialVerifier
{
    public const int MaxEmailLength = 320;
    public const int MaxPasswordLength = 128;

    /// <summary>Accounts must be stored with this form of the e-mail (also when seeded).</summary>
    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public async Task<CredentialResult> VerifyAsync(string email, string password, CancellationToken ct = default)
    {
        var record = await store.FindByEmailAsync(NormalizeEmail(email), ct);

        // Verify runs even when the account is unknown, so timing does not reveal which e-mails exist.
        var passwordMatches = hasher.Verify(password, record?.PasswordHash);

        return record is not null && passwordMatches
            ? new CredentialResult(true, record.AccountId, record.Status)
            : CredentialResult.Invalid;
    }
}
