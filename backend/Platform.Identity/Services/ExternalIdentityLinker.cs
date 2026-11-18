using Microsoft.EntityFrameworkCore;
using Npgsql;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;
using Platform.Identity.Persistence;

namespace Platform.Identity.Services;

internal sealed class ExternalIdentityLinker(
    PlatformIdentityDbContext db,
    TimeProvider time) : IExternalIdentityLinker
{
    internal const string IssuerSubjectIndex = "ux_external_logins_issuer_subject";
    internal const string AccountIssuerIndex = "ux_external_logins_account_issuer";

    public async Task<FederatedLoginResult> ResolveAsync(ExternalIdentity identity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(identity);

        if (!IsValidKey(identity.Issuer, ExternalLogin.MaxIssuerLength) ||
            !IsValidKey(identity.Subject, ExternalLogin.MaxSubjectLength))
            return FederatedLoginResult.Refused(FederatedLoginOutcome.InvalidIdentity);

        var now = time.GetUtcNow();

        // 1. Repeated identity: the link decides, never the e-mail.
        var existing = await FindLinkAsync(identity.Issuer, identity.Subject, ct);
        if (existing is not null)
        {
            existing.RecordLogin(now);
            await db.SaveChangesAsync(ct);
            return FederatedLoginResult.Existing(existing.AccountId);
        }

        // 2. First login: an unverified e-mail must not give access to someone else's account.
        if (!identity.EmailVerified || string.IsNullOrWhiteSpace(identity.Email))
            return FederatedLoginResult.Refused(FederatedLoginOutcome.EmailNotVerified);

        var email = CredentialVerifier.NormalizeEmail(identity.Email);
        if (email.Length > CredentialVerifier.MaxEmailLength)
            return FederatedLoginResult.Refused(FederatedLoginOutcome.NoMatchingAccount);

        var accountId = await db.Accounts
            .AsNoTracking()
            .Where(account => account.Email == email)
            .Select(account => (Guid?)account.Id)
            .SingleOrDefaultAsync(ct);

        if (accountId is null)
            return FederatedLoginResult.Refused(FederatedLoginOutcome.NoMatchingAccount);

        var alreadyLinked = await db.ExternalLogins
            .AsNoTracking()
            .AnyAsync(login => login.AccountId == accountId && login.Issuer == identity.Issuer, ct);
        if (alreadyLinked)
            return FederatedLoginResult.Refused(FederatedLoginOutcome.AccountAlreadyLinked);

        // 3. Insert; the database has the final word on duplicates.
        var link = new ExternalLogin(Guid.NewGuid(), accountId.Value, identity.Issuer, identity.Subject, now);
        db.ExternalLogins.Add(link);

        try
        {
            await db.SaveChangesAsync(ct);
            return FederatedLoginResult.New(accountId.Value);
        }
        catch (DbUpdateException ex) when (UniqueViolation(ex) is { } index)
        {
            db.ChangeTracker.Clear();

            if (index == IssuerSubjectIndex)
            {
                // A concurrent first login of the same identity won: return its account, do not add a second link.
                var winner = await FindLinkAsync(identity.Issuer, identity.Subject, ct);
                if (winner is not null)
                    return FederatedLoginResult.Existing(winner.AccountId);
            }

            // A different identity from the same issuer was linked to this account at the same moment.
            return FederatedLoginResult.Refused(FederatedLoginOutcome.AccountAlreadyLinked);
        }
    }

    private Task<ExternalLogin?> FindLinkAsync(string issuer, string subject, CancellationToken ct) =>
        db.ExternalLogins.SingleOrDefaultAsync(login => login.Issuer == issuer && login.Subject == subject, ct);

    private static bool IsValidKey(string? value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength;

    private static string? UniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
            ? pg.ConstraintName
            : null;
}