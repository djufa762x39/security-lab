using Microsoft.EntityFrameworkCore;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Persistence;

namespace Platform.Identity.Services;

public sealed class AccountLockoutAdministrationService(
    PlatformIdentityDbContext db,
    ICredentialStore store,
    TimeProvider clock) : IAccountLockoutAdministration
{
    public async Task<UnlockOutcome> UnlockAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var account = await db.Accounts
            .AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => new { a.LockoutEnd })
            .FirstOrDefaultAsync(cancellationToken);

        if (account is null)
            return UnlockOutcome.NotFound;

        await store.ClearFailuresAsync(accountId, cancellationToken);

        return account.LockoutEnd > clock.GetUtcNow() ? UnlockOutcome.Unlocked : UnlockOutcome.NotLocked;
    }
}
