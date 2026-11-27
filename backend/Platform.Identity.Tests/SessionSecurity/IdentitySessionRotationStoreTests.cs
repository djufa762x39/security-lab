using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Modules.SessionSecurity.Sessions;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Persistence;
using Platform.Identity.Tests.Helpers;
using Platform.Identity.Tests.Integration;
using Platform.Identity.Tests.SessionSecurity.Helpers;
using Platform.SessionSecurity.Application;
using Platform.SessionSecurity.Contracts;
using Platform.SessionSecurity.Domain;
using Platform.SessionSecurity.Infrastructure;

// IDN-11 TIM 09
namespace Platform.Identity.Tests.SessionSecurity;

/// <summary>
/// IDN-11: <see cref="IdentitySessionRotationStore"/> over the real identity.sessions table (PostgreSQL,
/// Testcontainers). Proves that the Team 1 session check rejects the old token and accepts the new one,
/// that a rotation is atomic, keeps the session handle so IDN-07 revocation still ends the session (D4),
/// never extends the lifetime (D6), never rotates a blocked account (IDN-03) and locks the account before
/// the session (D13), and that the fail-closed cleanup revokes every session of the subject (D4, D5, AUD-10).
/// </summary>
/// <remarks>
/// <see cref="TestDatabaseFixture"/> is a SetUpFixture of the Integration namespace and does not run for
/// this namespace, so this fixture starts the same database through it and builds the same
/// <see cref="CustomWebApplicationFactory{TProgram}"/> instead of inheriting IntegrationTestBase.
/// </remarks>
[TestFixture]
[Category("IDN-11")]
public sealed class IdentitySessionRotationStoreTests
{
    private const string Password = "Password123!";
    private const string UserA = "user-a@example.com";
    private const string UserB = "user-b@example.com";
    private const string CorrelationId = "corr-idn11-store";
    private const string MalformedHash = "not-a-hex-hash";

    private static readonly TimeSpan ShortLifetime = TimeSpan.FromMinutes(1);
    private static readonly CryptoSessionTokenGenerator TokenGenerator = new();

    private readonly TestDatabaseFixture database = new();
    private CustomWebApplicationFactory<Program> factory = null!;

    [OneTimeSetUp]
    public async Task StartDatabase()
    {
        await database.RunBeforeAnyTests();
        factory = new CustomWebApplicationFactory<Program>();
    }

    [OneTimeTearDown]
    public async Task StopDatabase()
    {
        await factory.DisposeAsync();
        await database.RunAfterAnyTests();
    }

    [SetUp]
    public Task ResetDatabase() => TestDatabaseFixture.ResetDatabaseAsync();

    // Negative: rejections leave identity.sessions unchanged.

    [Test]
    public async Task FindAsync_WhenHashIsNotHex_ReturnsNull()
    {
        var found = await WithStoreAsync(store => store.FindAsync(MalformedHash));

        Assert.That(found, Is.Null);
    }

    [Test]
    public async Task RotateAsync_WhenHashIsNotHex_ReturnsNotFound()
    {
        var seeded = await SeedSessionAsync(UserA);
        var replacement = ReplacementFor(seeded.Current, TokenGenerator.Generate(), Now());

        var status = await WithStoreAsync(store => store.RotateAsync(MalformedHash, replacement, Now()));

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(status, Is.EqualTo(SessionRotationStoreStatus.NotFound));
            Assert.That(await CountSessionsAsync(seeded.UserId), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RotateAsync_WhenOldTokenIsRevoked_ReturnsAlreadyRevoked()
    {
        var seeded = await SeedSessionAsync(UserA);
        await RevokeWithSessionManagerAsync(seeded.UserId, seeded.Session.SessionHandle);
        var issued = TokenGenerator.Generate();

        var status = await WithStoreAsync(store => store.RotateAsync(
            seeded.Current.TokenHash, ReplacementFor(seeded.Current, issued, Now()), Now()));

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(status, Is.EqualTo(SessionRotationStoreStatus.AlreadyRevoked));
            Assert.That(await CountSessionsAsync(seeded.UserId), Is.EqualTo(1));
            Assert.That(await IsAcceptedBySessionCheckAsync(issued.Value), Is.False);
        });
    }

    [Test]
    public async Task RotateAsync_WhenSessionExpired_ReturnsExpired()
    {
        var seeded = await SeedSessionAsync(UserA, lifetime: ShortLifetime);
        var afterExpiry = seeded.Current.AbsoluteExpiresAt;

        var status = await WithStoreAsync(store => store.RotateAsync(
            seeded.Current.TokenHash, ReplacementFor(seeded.Current, TokenGenerator.Generate(), afterExpiry), afterExpiry));

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(status, Is.EqualTo(SessionRotationStoreStatus.Expired));
            Assert.That(await CountSessionsAsync(seeded.UserId), Is.EqualTo(1));
            Assert.That((await FindAsync(seeded.RawToken))!.RevokedAt, Is.Null);
        });
    }

    [Test]
    public async Task RotateAsync_WhenAccountIsBlocked_ReturnsAlreadyRevokedAndStoresNothing()
    {
        var seeded = await SeedSessionAsync(UserA, AccountStatus.Blocked);

        var status = await WithStoreAsync(store => store.RotateAsync(
            seeded.Current.TokenHash, ReplacementFor(seeded.Current, TokenGenerator.Generate(), Now()), Now()));

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(status, Is.EqualTo(SessionRotationStoreStatus.AlreadyRevoked));
            Assert.That(await CountSessionsAsync(seeded.UserId), Is.EqualTo(1));
            Assert.That((await FindAsync(seeded.RawToken))!.RevokedAt, Is.Null);
        });
    }

    [Test]
    public async Task RotateAsync_WhenBlockingHoldsAccountLock_ReturnsAlreadyRevokedAndStoresNothing()
    {
        var seeded = await SeedSessionAsync(UserA);
        var replacement = ReplacementFor(seeded.Current, TokenGenerator.Generate(), Now());
        using var blockScope = factory.Services.CreateScope();
        await using var block = await blockScope.ServiceProvider.GetRequiredService<IAccountStatusStore>().BeginAsync();
        var account = await block.FindForUpdateAsync(seeded.UserId);
        // Not null: the account was seeded above and nothing deletes it.
        await block.ApplyAsync(account!, AccountStatus.Blocked);

        var rotation = WithStoreAsync(store => store.RotateAsync(seeded.Current.TokenHash, replacement, Now()));
        await block.CommitAsync();
        var status = await rotation;

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(status, Is.EqualTo(SessionRotationStoreStatus.AlreadyRevoked));
            Assert.That(await CountSessionsAsync(seeded.UserId), Is.EqualTo(1));
            Assert.That(await CountActiveSessionsAsync(seeded.UserId), Is.Zero);
        });
    }

    [Test]
    public async Task RotateAsync_WhenChainIdDoesNotMatch_ThrowsAndStoresNothing()
    {
        var seeded = await SeedSessionAsync(UserA);
        var replacement = ReplacementFor(seeded.Current, TokenGenerator.Generate(), Now()) with
        {
            ChainId = Guid.NewGuid(),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await WithStoreAsync(store => store.RotateAsync(seeded.Current.TokenHash, replacement, Now())));

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(await CountSessionsAsync(seeded.UserId), Is.EqualTo(1));
            Assert.That(await IsAcceptedBySessionCheckAsync(seeded.RawToken), Is.True);
        });
    }

    [Test]
    public async Task RotateAsync_WhenReplacementExtendsExpiry_ThrowsAndStoresNothing()
    {
        var seeded = await SeedSessionAsync(UserA);
        var replacement = ReplacementFor(seeded.Current, TokenGenerator.Generate(), Now()) with
        {
            AbsoluteExpiresAt = seeded.Current.AbsoluteExpiresAt.AddHours(1),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await WithStoreAsync(store => store.RotateAsync(seeded.Current.TokenHash, replacement, Now())));

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(await CountSessionsAsync(seeded.UserId), Is.EqualTo(1));
            Assert.That(await IsAcceptedBySessionCheckAsync(seeded.RawToken), Is.True);
        });
    }

    [Test]
    public async Task ActivateAsync_WhenSessionWasRevoked_ReturnsFalse()
    {
        var seeded = await SeedSessionAsync(UserA);
        await RevokeWithSessionManagerAsync(seeded.UserId, seeded.Session.SessionHandle);

        var activated = await WithStoreAsync(store => store.ActivateAsync(seeded.Current.TokenHash, Now()));

        Assert.That(activated, Is.False);
    }

    [Test]
    public async Task RevokeChainAsync_WhenChainIsUnknown_ReturnsZero()
    {
        var seeded = await SeedSessionAsync(UserA);

        var revoked = await WithStoreAsync(store => store.RevokeChainAsync(Guid.NewGuid(), Now()));

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(revoked, Is.Zero);
            Assert.That(await IsAcceptedBySessionCheckAsync(seeded.RawToken), Is.True);
        });
    }

    [Test]
    public async Task RevokeAllForSubjectAsync_WhenSubjectIsNotGuid_ReturnsZero()
    {
        var seeded = await SeedSessionAsync(UserA);

        var revoked = await WithStoreAsync(store => store.RevokeAllForSubjectAsync("user-a", Now()));

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(revoked, Is.Zero);
            Assert.That(await IsAcceptedBySessionCheckAsync(seeded.RawToken), Is.True);
        });
    }

    [Test]
    public async Task RevokeChainAsync_DoesNotRevokeSessionsOfOtherSubjects()
    {
        var seeded = await SeedSessionAsync(UserA);
        var other = await SeedSessionAsync(UserB);

        await WithStoreAsync(store => store.RevokeChainAsync(seeded.Current.ChainId, Now()));

        Assert.That(await IsAcceptedBySessionCheckAsync(other.RawToken), Is.True);
    }

    [Test]
    public async Task RotateAsync_WhenOldTokenWasAlreadyRotated_ReturnsNotFoundAndKeepsNewToken()
    {
        var seeded = await SeedSessionAsync(UserA);
        var issued = TokenGenerator.Generate();
        await RotateAsync(seeded.Current, issued);

        var status = await RotateAsync(seeded.Current, TokenGenerator.Generate());

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(status, Is.EqualTo(SessionRotationStoreStatus.NotFound));
            Assert.That(await IsAcceptedBySessionCheckAsync(issued.Value), Is.True);
            Assert.That(await CountSessionsAsync(seeded.UserId), Is.EqualTo(1));
        });
    }

    // IDN-07: the handle listed before a rotation still ends the session after it.

    [Test]
    public async Task RevokeSession_WhenHandleWasListedBeforeRotation_RejectsNewToken()
    {
        var seeded = await SeedSessionAsync(UserA);
        var issued = TokenGenerator.Generate();
        await RotateAsync(seeded.Current, issued);

        var revoked = await RevokeWithSessionManagerAsync(seeded.UserId, seeded.Session.SessionHandle);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(revoked, Is.True);
            Assert.That(await IsAcceptedBySessionCheckAsync(issued.Value), Is.False);
            Assert.That(await CountActiveSessionsAsync(seeded.UserId), Is.Zero);
        });
    }

    [Test]
    public async Task RotateAsync_WhenSessionIsRevokedByHandleDuringRotation_LeavesNoActiveSession()
    {
        var seeded = await SeedSessionAsync(UserA);
        var issued = TokenGenerator.Generate();

        var rotation = RotateAsync(seeded.Current, issued);
        var revocation = RevokeWithSessionManagerAsync(seeded.UserId, seeded.Session.SessionHandle);
        await Task.WhenAll(rotation, revocation);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(await rotation, Is.AnyOf(SessionRotationStoreStatus.Rotated, SessionRotationStoreStatus.AlreadyRevoked));
            Assert.That(await revocation, Is.True);
            Assert.That(await IsAcceptedBySessionCheckAsync(seeded.RawToken), Is.False);
            Assert.That(await IsAcceptedBySessionCheckAsync(issued.Value), Is.False);
            Assert.That(await CountActiveSessionsAsync(seeded.UserId), Is.Zero);
        });
    }

    // Positive: the rotation is visible to the Team 1 session check.

    [Test]
    public async Task Store_IsRegisteredAsIdentitySessionRotationStore()
    {
        var store = await WithStoreAsync(store => ValueTask.FromResult(store));

        Assert.That(store, Is.InstanceOf<IdentitySessionRotationStore>());
    }

    [Test]
    public async Task FindAsync_WhenSessionExists_MapsIdentitySession()
    {
        var seeded = await SeedSessionAsync(UserA);

        Assert.Multiple(() =>
        {
            Assert.That(seeded.Current.ChainId, Is.EqualTo(seeded.Session.SessionHandle));
            Assert.That(seeded.Current.SubjectId, Is.EqualTo(seeded.UserId.ToString("D")));
            Assert.That(seeded.Current.TokenHash, Is.EqualTo(TokenGenerator.ComputeHash(seeded.RawToken)));
            Assert.That(seeded.Current.PrivilegeLevel, Is.EqualTo(SessionPrivilegeLevel.Authenticated));
            Assert.That(seeded.Current.RevokedAt, Is.Null);
            Assert.That(seeded.Current.IsPendingActivation, Is.False);
        });
    }

    [Test]
    public async Task RotateAsync_WhenSessionIsActive_OldTokenIsRejectedBySessionCheck()
    {
        var seeded = await SeedSessionAsync(UserA);

        var status = await RotateAsync(seeded.Current, TokenGenerator.Generate());

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(status, Is.EqualTo(SessionRotationStoreStatus.Rotated));
            Assert.That(await IsAcceptedBySessionCheckAsync(seeded.RawToken), Is.False);
        });
    }

    [Test]
    public async Task RotateAsync_WhenSessionIsActive_NewTokenIsAcceptedBySessionCheck()
    {
        var seeded = await SeedSessionAsync(UserA);
        var issued = TokenGenerator.Generate();

        await RotateAsync(seeded.Current, issued);

        Assert.That(await IsAcceptedBySessionCheckAsync(issued.Value), Is.True);
    }

    [Test]
    public async Task RotateAsync_KeepsAbsoluteExpiry()
    {
        var seeded = await SeedSessionAsync(UserA);
        var issued = TokenGenerator.Generate();

        await RotateAsync(seeded.Current, issued);

        Assert.That((await FindAsync(issued.Value))!.AbsoluteExpiresAt, Is.EqualTo(seeded.Current.AbsoluteExpiresAt));
    }

    [Test]
    public async Task RotateAsync_KeepsTheHandleListedForSessionReview()
    {
        var seeded = await SeedSessionAsync(UserA);

        await RotateAsync(seeded.Current, TokenGenerator.Generate());

        using var scope = factory.Services.CreateScope();
        var listed = await scope.ServiceProvider.GetRequiredService<ISessionManager>().ListForUser(seeded.UserId);
        Assert.That(listed.Select(session => session.Handle), Is.EqualTo(new[] { seeded.Session.SessionHandle }));
    }

    [Test]
    public async Task ActivateAsync_WhenSessionIsActive_ReturnsTrue()
    {
        var seeded = await SeedSessionAsync(UserA);

        var activated = await WithStoreAsync(store => store.ActivateAsync(seeded.Current.TokenHash, Now()));

        Assert.That(activated, Is.True);
    }

    [Test]
    public async Task RotateAsync_WhenTwoRequestsUseSameOldToken_ExactlyOneSucceeds()
    {
        var seeded = await SeedSessionAsync(UserA);

        var statuses = await Task.WhenAll(
            RotateAsync(seeded.Current, TokenGenerator.Generate()),
            RotateAsync(seeded.Current, TokenGenerator.Generate()));

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(statuses.Count(status => status == SessionRotationStoreStatus.Rotated), Is.EqualTo(1));
            // The loser waits on the row lock and sees the new hash (AlreadyRevoked), or starts after the
            // commit and no longer finds the old hash (NotFound). The client gets the same answer for both.
            Assert.That(
                statuses.Single(status => status != SessionRotationStoreStatus.Rotated),
                Is.AnyOf(SessionRotationStoreStatus.AlreadyRevoked, SessionRotationStoreStatus.NotFound));
            Assert.That(await CountSessionsAsync(seeded.UserId), Is.EqualTo(1));
            Assert.That(await CountActiveSessionsAsync(seeded.UserId), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RotateAsync_WhenAccountIsBlockedDuringRotation_LeavesNoActiveSession()
    {
        var seeded = await SeedSessionAsync(UserA);

        var rotation = RotateAsync(seeded.Current, TokenGenerator.Generate());
        var block = BlockAccountAsync(seeded.UserId);
        await Task.WhenAll(rotation, block);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(await rotation, Is.AnyOf(SessionRotationStoreStatus.Rotated, SessionRotationStoreStatus.AlreadyRevoked));
            Assert.That(await CountActiveSessionsAsync(seeded.UserId), Is.Zero);
        });
    }

    [Test]
    public async Task RevokeChainAsync_RevokesEverySessionOfTheSubject()
    {
        var seeded = await SeedSessionAsync(UserA);
        var (secondToken, _) = await SessionTestData.CreateAsync(factory, seeded.UserId);
        var issued = TokenGenerator.Generate();
        await RotateAsync(seeded.Current, issued);

        var revoked = await WithStoreAsync(store => store.RevokeChainAsync(seeded.Current.ChainId, Now()));

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(revoked, Is.EqualTo(2));
            Assert.That(await IsAcceptedBySessionCheckAsync(issued.Value), Is.False);
            Assert.That(await IsAcceptedBySessionCheckAsync(secondToken), Is.False);
            Assert.That(await CountActiveSessionsAsync(seeded.UserId), Is.Zero);
        });
    }

    [Test]
    public async Task RevokeAllForSubjectAsync_RevokesEverySessionOfTheSubject()
    {
        var seeded = await SeedSessionAsync(UserA);
        var issued = TokenGenerator.Generate();
        await RotateAsync(seeded.Current, issued);

        var revoked = await WithStoreAsync(store => store.RevokeAllForSubjectAsync(seeded.UserId.ToString("D"), Now()));

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(revoked, Is.EqualTo(1));
            Assert.That(await IsAcceptedBySessionCheckAsync(issued.Value), Is.False);
        });
    }

    // End to end with the real SessionRotationService.

    [Test]
    public async Task RotateAsync_ThroughService_OldTokenIsRejectedAndNewTokenWorks()
    {
        var seeded = await SeedSessionAsync(UserA);
        var audit = new RecordingSessionSecurityAuditWriter();

        var result = await RotateThroughServiceAsync(seeded, audit);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(await IsAcceptedBySessionCheckAsync(seeded.RawToken), Is.False);
            // Not null: a successful result always carries the new token.
            Assert.That(await IsAcceptedBySessionCheckAsync(result.NewToken!), Is.True);
            Assert.That(audit.Entries.Single().Outcome, Is.EqualTo(SessionRotationCodes.Rotated));
        });
    }

    [Test]
    public async Task RotateAsync_ThroughServiceWhenAuditFails_RevokesEverySessionOfTheSubject()
    {
        var seeded = await SeedSessionAsync(UserA);
        var (secondToken, _) = await SessionTestData.CreateAsync(factory, seeded.UserId);

        var result = await RotateThroughServiceAsync(seeded, new RecordingSessionSecurityAuditWriter(writeSucceeds: false));

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(result.Code, Is.EqualTo(SessionRotationCodes.AuditFailed));
            Assert.That(result.NewToken, Is.Null);
            Assert.That(await IsAcceptedBySessionCheckAsync(seeded.RawToken), Is.False);
            Assert.That(await IsAcceptedBySessionCheckAsync(secondToken), Is.False);
            Assert.That(await CountActiveSessionsAsync(seeded.UserId), Is.Zero);
        });
    }

    [Test]
    public async Task RotateAsync_ThroughService_AuditEntryHasNoTokenHashOrHandle()
    {
        var seeded = await SeedSessionAsync(UserA);
        var audit = new RecordingSessionSecurityAuditWriter();

        var result = await RotateThroughServiceAsync(seeded, audit);
        var recorded = audit.Entries.Single().ToString();

        Assert.Multiple(() =>
        {
            Assert.That(recorded, Does.Not.Contain(seeded.RawToken));
            Assert.That(recorded, Does.Not.Contain(seeded.Current.TokenHash));
            Assert.That(recorded, Does.Not.Contain(seeded.Session.SessionHandle.ToString()));
            // Not null: the rotation succeeded, see the test above.
            Assert.That(recorded, Does.Not.Contain(result.NewToken!));
            Assert.That(recorded, Does.Not.Contain(TokenGenerator.ComputeHash(result.NewToken!)));
        });
    }

    private async Task<SeededSession> SeedSessionAsync(
        string email,
        AccountStatus status = AccountStatus.Active,
        TimeSpan? lifetime = null)
    {
        var user = await UserTestData.AddAsync(factory, email, Password, status);
        var (rawToken, session) = await SessionTestData.CreateAsync(factory, user.Id, lifetime: lifetime);
        // Not null: the session was stored just above.
        var current = (await FindAsync(rawToken))!;
        return new SeededSession(user.Id, rawToken, session, current);
    }

    private static RotatableSession ReplacementFor(
        RotatableSession current,
        IssuedSessionToken issued,
        DateTimeOffset now) =>
        new(current.ChainId, current.SubjectId, issued.Hash, SessionPrivilegeLevel.Authenticated,
            now, current.AbsoluteExpiresAt, IsPendingActivation: true);

    private Task<SessionRotationStoreStatus> RotateAsync(RotatableSession current, IssuedSessionToken issued) =>
        WithStoreAsync(store => store.RotateAsync(current.TokenHash, ReplacementFor(current, issued, Now()), Now()));

    private async Task<SessionRotationResult> RotateThroughServiceAsync(
        SeededSession seeded,
        ISessionSecurityAuditWriter audit)
    {
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var service = new SessionRotationService(
            services.GetRequiredService<ISessionRotationStore>(),
            TokenGenerator,
            audit,
            services.GetRequiredService<TimeProvider>());

        return await service.RotateAsync(new RotateSessionRequest(
            seeded.RawToken, seeded.UserId.ToString("D"), RotationTrigger.MfaCompleted,
            SessionPrivilegeLevel.Elevated, CorrelationId));
    }

    private async Task<T> WithStoreAsync<T>(Func<ISessionRotationStore, ValueTask<T>> action)
    {
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<ISessionRotationStore>());
    }

    private Task<RotatableSession?> FindAsync(string rawToken) =>
        WithStoreAsync(store => store.FindAsync(TokenGenerator.ComputeHash(rawToken)));

    private async Task<bool> IsAcceptedBySessionCheckAsync(string rawToken)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISessionManager>().ValidateAsync(rawToken) is not null;
    }

    private async Task<bool> RevokeWithSessionManagerAsync(Guid userId, Guid sessionHandle)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISessionManager>().RevokeSession(userId, sessionHandle);
    }

    private async Task BlockAccountAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        await using var block = await scope.ServiceProvider.GetRequiredService<IAccountStatusStore>().BeginAsync();
        var account = await block.FindForUpdateAsync(userId);
        // Not null: the account was seeded by the calling test and nothing deletes it.
        await block.ApplyAsync(account!, AccountStatus.Blocked);
        await block.CommitAsync();
    }

    private async Task<int> CountSessionsAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();
        return await db.Sessions.CountAsync(session => session.UserId == userId);
    }

    private async Task<int> CountActiveSessionsAsync(Guid userId)
    {
        var now = Now();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();
        return await db.Sessions.CountAsync(session =>
            session.UserId == userId && session.RevokedAt == null && session.ExpiresAt > now);
    }

    private DateTimeOffset Now() => factory.Services.GetRequiredService<TimeProvider>().GetUtcNow();

    private sealed record SeededSession(Guid UserId, string RawToken, Session Session, RotatableSession Current)
    {
        // AUD-03: never print the session token, its hash or the handle in test output.
        public override string ToString() => $"SeededSession {{ UserId = {UserId} }}";
    }
}
