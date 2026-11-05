using Microsoft.Extensions.Options;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;

namespace Platform.Identity.Services;

public sealed class CredentialVerifier(
    ICredentialStore store,
    IPasswordHasher hasher,
    IOptions<IdentityLockoutOptions> lockout,
    TimeProvider clock) : ICredentialVerifier
{
    public const int MaxEmailLength = 320;
    public const int MaxPasswordLength = 128;

    /// <summary>Accounts must be stored with this form of the e-mail (also when seeded).</summary>
    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public async Task<CredentialResult> VerifyAsync(string email, string password, CancellationToken ct = default)
    {
        var normalizedEmail = NormalizeEmail(email);
        var record = await store.FindByEmailAsync(normalizedEmail, ct);

        // Verify runs even when the account is unknown or locked, so timing does not reveal which e-mails exist.
        var passwordMatches = hasher.Verify(password, record?.PasswordHash);

        var now = clock.GetUtcNow();

        // IDN-05: while locked even the correct password is refused, and the attempt is not counted.
        if (record?.LockoutEnd > now)
            return CredentialResult.LockedOut(record.AccountId);

        if (record is not null && passwordMatches)
        {
            if (record.FailedLoginCount > 0 || record.LockoutEnd is not null)
                await store.ClearFailuresAsync(record.AccountId, ct);

            return new CredentialResult(true, record.AccountId, record.Status);
        }

        // An unknown e-mail runs the same update and matches no row, so it costs the same round trip.
        var options = lockout.Value;
        var lockoutEnd = await store.RecordFailureAsync(
            normalizedEmail, options.MaxFailedAccessAttempts, now.AddMinutes(options.LockoutMinutes), ct);

        return record is not null && lockoutEnd > now
            ? CredentialResult.LockedOut(record.AccountId)
            : CredentialResult.Invalid;
    }
}
