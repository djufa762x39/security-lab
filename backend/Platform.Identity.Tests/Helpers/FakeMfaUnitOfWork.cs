using System.Data.Common;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces.Mfa;

namespace Platform.Identity.Tests.Helpers;

/// <summary>An <see cref="IMfaUnitOfWork"/> that records the calls of MfaService and returns the configured results.</summary>
public sealed class FakeMfaUnitOfWork : IMfaUnitOfWork
{
    public bool SessionLockResult { get; set; } = true;
    public (Guid UserId, Guid SessionId, AuthLevel Level)? LockedSession { get; private set; }
    public bool CreatePendingResult { get; set; } = true;
    public Exception? CreatePendingFailure { get; set; }
    public (Guid UserId, string Ciphertext, int KeyVersion, DateTimeOffset ExpiresAt)? CreatedPending { get; private set; }
    public int CreatePendingCalls { get; private set; }
    public bool ConfirmResult { get; set; } = true;
    public bool ConsumeResult { get; set; } = true;
    public const string NewToken = "token-of-the-new-active-session";
    public bool ReplaceResult { get; set; } = true;
    public int ConfirmCalls { get; private set; }
    public long? ConsumedStep { get; private set; }
    public (Guid UserId, Guid PendingSessionId, TimeSpan Lifetime)? ReplacedSession { get; private set; }
    public Session? NewSession { get; private set; }
    public bool Committed { get; private set; }
    public bool Disposed { get; private set; }

    public DbTransaction Transaction { get; } = new MarkerTransaction();

    public Task<bool> TryLockActiveSessionAsync(
        Guid userId, Guid sessionId, AuthLevel level, DateTimeOffset now, CancellationToken ct)
    {
        LockedSession = (userId, sessionId, level);
        return Task.FromResult(SessionLockResult);
    }

    public Task<bool> TryCreatePendingAsync(
        Guid userId, string encryptedSecret, int keyVersion, DateTimeOffset expiresAt, DateTimeOffset now,
        CancellationToken ct)
    {
        CreatePendingCalls++;
        if (CreatePendingFailure is not null)
            return Task.FromException<bool>(CreatePendingFailure);
        if (CreatePendingResult)
            CreatedPending = (userId, encryptedSecret, keyVersion, expiresAt);
        return Task.FromResult(CreatePendingResult);
    }

    public Task<bool> TryConfirmEnrollmentAsync(
        Guid userId, string pendingCiphertext, long timeStep, DateTimeOffset now, CancellationToken ct)
    {
        ConfirmCalls++;
        return Task.FromResult(ConfirmResult);
    }

    public Task<bool> TryConsumeTimeStepAsync(Guid userId, long timeStep, DateTimeOffset now, CancellationToken ct)
    {
        if (ConsumeResult)
            ConsumedStep = timeStep;
        return Task.FromResult(ConsumeResult);
    }

    public Task<(string RawToken, Session Session)?> TryReplaceWithActiveSessionAsync(
        Guid userId, Guid pendingSessionId, TimeSpan lifetime, DateTimeOffset now, CancellationToken ct)
    {
        if (!ReplaceResult)
            return Task.FromResult<(string, Session)?>(null);

        ReplacedSession = (userId, pendingSessionId, lifetime);
        NewSession = new Session(Guid.NewGuid(), userId, [1, 2, 3], Guid.NewGuid(), AuthLevel.Active, now,
            now.Add(lifetime));
        return Task.FromResult<(string, Session)?>((NewToken, NewSession));
    }

    public Task CommitAsync(CancellationToken ct)
    {
        Committed = true;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
