using System.Data.Common;
using Moq;
using Platform.Api.Enums.Identity;
using Platform.Audit.Contracts;
using Platform.Identity.Entities;
using Platform.Identity.Enums;

namespace Platform.Identity.Tests.Services.Mfa;

/// <summary>IDN-06 enrollment (EnrollAsync): who may start it and what is stored.</summary>
[TestFixture]
[Category("US-17")]
public sealed class MfaServiceEnrollTests : MfaServiceTestBase
{
    [Test]
    public async Task Enroll_WithoutToken_IsUnauthorized_AndWritesNoAudit()
    {
        var result = await _service.EnrollAsync(null, "corr-1", CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(MfaOutcome.Unauthorized));
        Assert.That(result.Uri, Is.Null);
        Assert.That(_audit.Writes, Is.Empty);
    }

    [Test]
    public async Task Enroll_WithMfaPendingSession_IsUnauthorized()
    {
        SetupSession(AuthLevel.MfaPending);

        var result = await _service.EnrollAsync(Token, "corr-2", CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(MfaOutcome.Unauthorized));
        _credentials.Verify(store => store.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Enroll_WhenMfaIsAlreadyEnabled_ReturnsConflict_AndDoesNotReplaceSecret()
    {
        SetupSession();
        var credentials = new MfaCredentials(UserId)
        {
            MfaEnabled = true,
            TotpSecretCiphertext = "existing-ciphertext",
            TotpSecretKeyVersion = 1
        };
        _credentials.Setup(store => store.GetAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(credentials);

        var result = await _service.EnrollAsync(Token, "corr-3", CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(MfaOutcome.AlreadyEnabled));
        Assert.That(result.Uri, Is.Null);
        Assert.That(_work.CreatePendingCalls, Is.Zero);
        Assert.That(_audit.Writes, Is.Empty);
    }

    [Test]
    public async Task Enroll_WithActiveSession_CreatesPendingEnrollment_AndReturnsTotpUri()
    {
        SetupSession();
        _credentials.Setup(store => store.GetAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MfaCredentials?)null);
        _credentials.Setup(store => store.GetEmailAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("student@example.test");

        var result = await _service.EnrollAsync(Token, "corr-success", CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(MfaOutcome.Success));
        Assert.That(result.Uri, Does.StartWith("otpauth://totp/"));
        Assert.That(result.Uri, Does.Contain("student%40example.test"));
        Assert.That(_work.LockedSession, Is.EqualTo((UserId, _sessionId, AuthLevel.Active)));
        Assert.That(_work.CreatedPending?.UserId, Is.EqualTo(UserId));
        Assert.That(_work.CreatedPending?.Ciphertext, Is.Not.Null.And.Not.Empty);
        Assert.That(_work.CreatedPending?.KeyVersion, Is.EqualTo(1));
        Assert.That(_work.CreatedPending?.ExpiresAt, Is.EqualTo(_now.AddMinutes(10)));
        Assert.That(_work.Committed, Is.True);
        var started = _audit.Writes.Single();
        Assert.That(started.Draft.EventType, Is.EqualTo(AuditEventTypes.AuthMfaEnrollmentStarted));
        Assert.That(started.Transaction, Is.Null);
        Assert.That(started.Context.Actor, Is.EqualTo(AuditActor.ForUser(UserId)));
    }

    [Test]
    public async Task Enroll_WithUnexpiredPendingEnrollment_IsRejected()
    {
        SetupSession();
        _credentials.Setup(store => store.GetAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MfaCredentials(UserId)
            {
                PendingTotpSecretCiphertext = "pending-ciphertext",
                PendingTotpSecretKeyVersion = 1,
                PendingEnrollmentExpiresAt = _now.AddMinutes(5)
            });
        _credentials.Setup(store => store.GetEmailAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("student@example.test");
        _work.CreatePendingResult = false;

        var result = await _service.EnrollAsync(Token, "corr-4", CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(MfaOutcome.EnrollmentPending));
        Assert.That(result.Uri, Is.Null);
        Assert.That(_work.CreatePendingCalls, Is.EqualTo(1));
        Assert.That(_work.Committed, Is.False);
    }

    // IDN-03 and IDN-07: the session and the account are checked again under a lock in the writing transaction.
    [Test]
    public async Task Enroll_WhenSessionOrAccountIsNoLongerActiveAtTheWrite_IsUnauthorized_AndStoresNothing()
    {
        SetupSession();
        _credentials.Setup(store => store.GetAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MfaCredentials?)null);
        _credentials.Setup(store => store.GetEmailAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("student@example.test");
        _work.SessionLockResult = false;

        var result = await _service.EnrollAsync(Token, "corr-enroll-blocked", CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(MfaOutcome.Unauthorized));
        Assert.That(result.Uri, Is.Null);
        Assert.That(_work.LockedSession, Is.EqualTo((UserId, _sessionId, AuthLevel.Active)));
        Assert.That(_work.CreatePendingCalls, Is.Zero);
        Assert.That(_work.Committed, Is.False);
        Assert.That(_audit.Writes, Is.Empty);
    }

    [Test]
    public async Task Enroll_WhenTheDatabaseFails_ReturnsUnavailable()
    {
        SetupSession();
        _credentials.Setup(store => store.GetAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MfaCredentials?)null);
        _credentials.Setup(store => store.GetEmailAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("student@example.test");
        _work.CreatePendingFailure = new Microsoft.EntityFrameworkCore.DbUpdateException("storage failed");

        var result = await _service.EnrollAsync(Token, "corr-db-down", CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(MfaOutcome.Unavailable));
        Assert.That(result.Uri, Is.Null);
        Assert.That(_audit.Writes, Is.Empty);
    }
}
