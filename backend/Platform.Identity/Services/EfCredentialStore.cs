using Microsoft.EntityFrameworkCore;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;
using Platform.Identity.Persistence;

namespace Platform.Identity.Services;

internal sealed class EfCredentialStore(PlatformIdentityDbContext db) : ICredentialStore
{
    public Task<CredentialRecord?> FindByEmailAsync(string normalizedEmail, CancellationToken ct = default) =>
        db.Accounts
            .AsNoTracking()
            .Where(account => account.Email == normalizedEmail)
            .Select(account => new CredentialRecord(
                account.Id, account.Status, account.PasswordHash, account.FailedLoginCount, account.LockoutEnd))
            .FirstOrDefaultAsync(ct);

    public async Task<DateTimeOffset?> RecordFailureAsync(
        string normalizedEmail, int maxAttempts, DateTimeOffset lockoutEnd, CancellationToken ct = default)
    {
        var account = db.Accounts.Where(a => a.Email == normalizedEmail);

        // One UPDATE, so parallel failures cannot lose an increment.
        await account.ExecuteUpdateAsync(set => set
            .SetProperty(a => a.LockoutEnd, a => a.FailedLoginCount + 1 >= maxAttempts ? lockoutEnd : a.LockoutEnd)
            .SetProperty(a => a.FailedLoginCount, a => a.FailedLoginCount + 1 >= maxAttempts ? 0 : a.FailedLoginCount + 1), ct);

        return await account.Select(a => a.LockoutEnd).FirstOrDefaultAsync(ct);
    }

    public Task ClearFailuresAsync(Guid accountId, CancellationToken ct = default) =>
        db.Accounts
            .Where(a => a.Id == accountId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(a => a.FailedLoginCount, 0)
                .SetProperty(a => a.LockoutEnd, (DateTimeOffset?)null), ct);
}
