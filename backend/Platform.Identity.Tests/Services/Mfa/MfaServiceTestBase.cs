using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Platform.Api.Services.Identity;
using Platform.Audit.Contracts;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Interfaces.Mfa;
using Platform.Identity.Services;
using Platform.Identity.Tests.Helpers;

namespace Platform.Identity.Tests.Services.Mfa;

/// <summary>IDN-06: MfaService with strict mocks of the stores, a fixed clock and a recorded Audit.</summary>
public abstract class MfaServiceTestBase
{
    protected const string Token = "session-token";
    protected static readonly Guid UserId = Guid.NewGuid();
    protected readonly DateTimeOffset _now = new(2026, 11, 14, 17, 0, 0, TimeSpan.Zero);
    protected Mock<ISessionStore> _sessions = null!;
    protected Mock<IMfaCredentialsStore> _credentials = null!;
    protected RecordingAuditWriters _audit = null!;
    protected Mock<TimeProvider> _clock = null!;
    protected ITotpSecretProtector _protector = null!;
    protected FakeMfaUnitOfWork _work = null!;
    protected MfaService _service = null!;

    [SetUp]
    public void SetUpService()
    {
        _sessions = new Mock<ISessionStore>(MockBehavior.Strict);
        _credentials = new Mock<IMfaCredentialsStore>(MockBehavior.Strict);
        _audit = new RecordingAuditWriters();
        _clock = new Mock<TimeProvider>(MockBehavior.Strict);
        _clock.Setup(clock => clock.GetUtcNow()).Returns(_now);
        _work = new FakeMfaUnitOfWork();
        _credentials.Setup(store => store.BeginAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_work);
        _protector = new DataProtectionTotpSecretProtector(new EphemeralDataProtectionProvider());
        _service = new MfaService(_credentials.Object, _sessions.Object,
            new IdentityAuditProducer(_audit, _audit, NullLogger<IdentityAuditProducer>.Instance),
            _clock.Object, _protector, Options.Create(new MfaLockoutOptions()), NullLogger<MfaService>.Instance);
    }

    protected readonly Guid _sessionId = Guid.NewGuid();

    protected void SetupSession(AuthLevel authLevel = AuthLevel.Active, DateTimeOffset? now = null)
    {
        var sessionNow = now ?? _now;
        var session = new Session(
            _sessionId, UserId, new byte[32], Guid.NewGuid(),
            authLevel, sessionNow.AddHours(-1), sessionNow.AddHours(8));
        _sessions.Setup(store => store.FindByTokenAsync(Token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
    }

    protected void SetupPendingCredentials(string ciphertext, DateTimeOffset? expiresAt = null)
    {
        _credentials.Setup(store => store.GetAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MfaCredentials(UserId)
            {
                PendingTotpSecretCiphertext = ciphertext,
                PendingTotpSecretKeyVersion = 1,
                PendingEnrollmentExpiresAt = expiresAt ?? _now.AddMinutes(5)
            });
    }

    protected void SetupVerification(
        string secret, DateTimeOffset now, long? currentStep = null, DateTimeOffset? lockedUntil = null)
    {
        _clock.Setup(clock => clock.GetUtcNow()).Returns(now);
        SetupSession(AuthLevel.MfaPending, now);
        _credentials.Setup(store => store.GetAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MfaCredentials(UserId)
            {
                MfaEnabled = true,
                TotpSecretCiphertext = _protector.Protect(secret),
                TotpSecretKeyVersion = 1,
                LastTotpTimeStep = currentStep,
                MfaLockedUntil = lockedUntil
            });
    }

    protected void SetupConfirmation(string secret, DateTimeOffset now)
    {
        _clock.Setup(clock => clock.GetUtcNow()).Returns(now);
        SetupSession(now: now);
        SetupPendingCredentials(_protector.Protect(secret), now.AddMinutes(5));
    }

    protected void VerifyAuditDoesNotContainSecret(
        string expectedReason,
        string eventType = AuditEventTypes.AuthMfaEnabled,
        AuditOutcome outcome = AuditOutcome.Failure)
    {
        var write = _audit.Writes.Single();
        Assert.That(write.Draft.EventType, Is.EqualTo(eventType));
        Assert.That(write.Draft.Outcome, Is.EqualTo(outcome));
        // The real Audit validator accepts the record with this reason for its event and outcome.
        Assert.That(new Platform.Audit.Application.AuditDraftValidator().Validate(write.Draft, write.Context),
            Is.EqualTo(AuditRejectionCode.None));
        Assert.That(write.Draft.Details, Is.EqualTo(new Dictionary<string, string> { ["reasonCode"] = expectedReason }));
        var serialized = string.Join('|', write.Draft.EventType, write.Draft.Resource?.Id, write.Context.CorrelationId,
            string.Join(',', write.Draft.Details!.Select(pair => $"{pair.Key}={pair.Value}")));
        Assert.That(serialized, Does.Not.Contain("ciphertext").And.Not.Contain("123456").And.Not.Contain("000000"));
    }
}
