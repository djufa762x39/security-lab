using Platform.Identity.Models;

namespace Platform.Identity.Interfaces;

/// <summary>Finds the data needed for a login check. The e-mail passed in is already normalized.</summary>
public interface ICredentialStore
{
    Task<CredentialRecord?> FindByEmailAsync(string normalizedEmail, CancellationToken ct = default);

    /// <summary>
    /// IDN-05: counts one failed login. The attempt that reaches <paramref name="maxAttempts"/> sets
    /// <paramref name="lockoutEnd"/> and restarts the counter. An unknown e-mail changes nothing.
    /// </summary>
    /// <returns>The lockout end stored after the update, null when there is none.</returns>
    Task<DateTimeOffset?> RecordFailureAsync(
        string normalizedEmail, int maxAttempts, DateTimeOffset lockoutEnd, CancellationToken ct = default);

    /// <summary>IDN-05: resets the counter and removes any lockout.</summary>
    Task ClearFailuresAsync(Guid accountId, CancellationToken ct = default);
}
