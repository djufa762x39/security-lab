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
            .Select(account => new CredentialRecord(account.Id, account.Status, account.PasswordHash))
            .FirstOrDefaultAsync(ct);
}
