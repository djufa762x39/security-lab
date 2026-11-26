using Microsoft.EntityFrameworkCore;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Persistence;
using Platform.SessionSecurity.Contracts;

// IDN-11 TIM 09
namespace Platform.Api.Modules.SessionSecurity.Sessions;

/// <summary>
/// IDN-11: <see cref="ISessionRotationStore"/> over <c>identity.sessions</c> (Team 1), without a schema
/// change. A rotated session is an ordinary Team 1 session, so SessionCheckMiddleware rejects the old
/// token and accepts the new one (SEC-03).
/// </summary>
/// <remarks>
/// <para>
/// D4: identity.sessions has no chain, so <see cref="RotatableSession.ChainId"/> is the
/// <see cref="Session.SessionHandle"/> of the session that was found. The handle is unique (IDN-07 looks
/// sessions up by it), so every rotation gives the new session a new handle. For that reason
/// <see cref="RevokeChainAsync"/> revokes every session of the subject, not only one chain: fail-closed,
/// the user is logged out everywhere.
/// </para>
/// <para>
/// D5: "pending activation" is logical. <see cref="RotateAsync"/> stores the new session as an active
/// Team 1 session, but its token leaves the server only after the rotation was audited; when the audit
/// fails, <see cref="Platform.SessionSecurity.Application.SessionRotationService"/> revokes every session of the subject.
/// <see cref="ActivateAsync"/> therefore changes nothing and only checks that the session is still active.
/// </para>
/// <para>
/// D6, D13: the new session keeps the absolute expiry of the old one. A rotation locks the account
/// (FOR SHARE) before the session (FOR UPDATE), in the same order as the Team 1 status change, so a
/// concurrent block cannot deadlock with it and no session outlives a block (IDN-03).
/// </para>
/// </remarks>
internal sealed class IdentitySessionRotationStore(PlatformIdentityDbContext db) : ISessionRotationStore
{
    public async ValueTask<RotatableSession?> FindAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        if (!SessionTokenHashConverter.TryToBytes(tokenHash, out var hash))
        {
            return null;
        }

        var session = await db.Sessions
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.TokenHash == hash, cancellationToken);
        return session is null ? null : ToRotatable(session);
    }

    public async ValueTask<SessionRotationStoreStatus> RotateAsync(
        string oldTokenHash,
        RotatableSession replacement,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (!SessionTokenHashConverter.TryToBytes(oldTokenHash, out var oldHash))
        {
            return SessionRotationStoreStatus.NotFound;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // D13: the owner is read without a lock, so the account can be locked before the session.
        var userId = await FindOwnerAsync(oldHash, cancellationToken);
        if (userId is null)
        {
            return SessionRotationStoreStatus.NotFound;
        }

        if (!await LockActiveAccountAsync(userId.Value, cancellationToken))
        {
            return SessionRotationStoreStatus.AlreadyRevoked;
        }

        var current = await LockSessionAsync(oldHash, cancellationToken);
        var status = CheckLockedSession(current, userId.Value, now);
        if (status is not null)
        {
            return status.Value;
        }

        // Not null: CheckLockedSession returns a status for a missing session.
        await ReplaceAsync(current!, replacement, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return SessionRotationStoreStatus.Rotated;
    }

    public async ValueTask<bool> ActivateAsync(
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (!SessionTokenHashConverter.TryToBytes(tokenHash, out var hash))
        {
            return false;
        }

        var session = await db.Sessions
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.TokenHash == hash, cancellationToken);

        // D5: the Team 1 activity rule decides, so a later change of it applies here as well.
        return session is not null && session.IsActiveAt(now);
    }

    public async ValueTask<int> RevokeChainAsync(
        Guid chainId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var userId = await db.Sessions
            .AsNoTracking()
            .Where(session => session.SessionHandle == chainId)
            .Select(session => (Guid?)session.UserId)
            .SingleOrDefaultAsync(cancellationToken);

        return userId is null ? 0 : await RevokeAllAsync(userId.Value, now, cancellationToken);
    }

    public async ValueTask<int> RevokeAllForSubjectAsync(
        string subjectId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        // D1: the subject is the user ID in the "D" format; anything else matches no session.
        return Guid.TryParseExact(subjectId, "D", out var userId)
            ? await RevokeAllAsync(userId, now, cancellationToken)
            : 0;
    }

    private Task<Guid?> FindOwnerAsync(byte[] tokenHash, CancellationToken cancellationToken) =>
        db.Sessions
            .AsNoTracking()
            .Where(session => session.TokenHash == tokenHash)
            .Select(session => (Guid?)session.UserId)
            .SingleOrDefaultAsync(cancellationToken);

    // FOR SHARE pairs with the FOR UPDATE of the Team 1 status change, the same lock as
    // SessionStore.CreateForActiveAccountAsync (IDN-03).
    private async Task<bool> LockActiveAccountAsync(Guid userId, CancellationToken cancellationToken)
    {
        var account = await db.Accounts
            .FromSqlInterpolated($"""
                SELECT * FROM identity.accounts WHERE id = {userId} FOR SHARE
                """)
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        return account is not null && account.Status == AccountStatus.Active;
    }

    // Not tracked: the request's context may already track this session (SessionManager.ValidateAsync)
    // with values from before the lock, and the checks below need the locked row.
    private Task<Session?> LockSessionAsync(byte[] tokenHash, CancellationToken cancellationToken) =>
        db.Sessions
            .FromSqlInterpolated($"""
                SELECT * FROM identity.sessions WHERE token_hash = {tokenHash} FOR UPDATE
                """)
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

    // A concurrent rotation that committed first is seen here as a revoked session.
    private static SessionRotationStoreStatus? CheckLockedSession(Session? current, Guid userId, DateTimeOffset now)
    {
        if (current is null || current.UserId != userId)
        {
            return SessionRotationStoreStatus.NotFound;
        }

        if (current.RevokedAt is not null)
        {
            return SessionRotationStoreStatus.AlreadyRevoked;
        }

        return current.ExpiresAt <= now ? SessionRotationStoreStatus.Expired : null;
    }

    private async Task ReplaceAsync(
        Session current,
        RotatableSession replacement,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var (replacementHash, level) = ValidateReplacement(current, replacement);

        // The row is locked by this transaction, so exactly this session is revoked.
        await db.Sessions
            .Where(session => session.Id == current.Id && session.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(session => session.RevokedAt, (DateTimeOffset?)now), cancellationToken);

        // Guid.NewGuid only for the non-secret Id and SessionHandle, as in Team 1. The handle is new because
        // the column is unique (D4); the expiry is the old one (D6).
        db.Sessions.Add(new Session(
            Guid.NewGuid(), current.UserId, replacementHash, Guid.NewGuid(), level, now, current.ExpiresAt));
        await db.SaveChangesAsync(cancellationToken);
    }

    // Programming errors of the caller, checked before anything is written. The messages carry no values.
    private static (byte[] Hash, AuthLevel Level) ValidateReplacement(Session current, RotatableSession replacement)
    {
        if (replacement.ChainId != current.SessionHandle || replacement.AbsoluteExpiresAt != current.ExpiresAt)
        {
            throw new InvalidOperationException(
                "The replacement must keep the chain and the absolute expiry of the old session.");
        }

        if (!replacement.IsPendingActivation || replacement.IsRevoked)
        {
            throw new InvalidOperationException(
                "The replacement must be stored as pending activation and not revoked.");
        }

        if (!SessionTokenHashConverter.TryToBytes(replacement.TokenHash, out var hash))
        {
            throw new InvalidOperationException("The replacement must carry a SHA-256 token hash.");
        }

        return (hash, SessionPrivilegeLevelMapper.ToIdentity(replacement.PrivilegeLevel));
    }

    private Task<int> RevokeAllAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Sessions
            .Where(session => session.UserId == userId && session.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(session => session.RevokedAt, (DateTimeOffset?)now), cancellationToken);

    private static RotatableSession ToRotatable(Session session) => new(
        session.SessionHandle,
        session.UserId.ToString("D"),
        SessionTokenHashConverter.ToHex(session.TokenHash),
        SessionPrivilegeLevelMapper.ToCore(session.AuthLevel),
        session.CreatedAt,
        session.ExpiresAt,
        session.RevokedAt,
        IsPendingActivation: false);
}
