using Platform.Api.Enums.Identity;
using Platform.Api.Services.Identity;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Moq;

namespace Platform.Identity.Tests.Services;

[TestFixture]
[Category("US-06")]
public class StoreSessionManagerTests
{
    private Mock<ISessionStore> _store = null!;
    private StoreSessionManager _manager = null!;

    [SetUp]
    public void SetUp()
    {
        _store = new Mock<ISessionStore>();
        _manager = new StoreSessionManager(_store.Object);
    }

    [Test]
    public async Task UnknownToken_ReturnsNull()
    {
        StoreReturns("unknown-token", null);

        var result = await _manager.ValidateAsync("unknown-token");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ExpiredSession_ReturnsNull()
    {
        StoreReturns("token", NewSession(AuthLevel.Active, expiresIn: TimeSpan.FromMinutes(-1)));

        var result = await _manager.ValidateAsync("token");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ValidSession_ReturnsHandleUserAndActiveLevel()
    {
        var session = NewSession(AuthLevel.Active, expiresIn: TimeSpan.FromHours(1));
        StoreReturns("token", session);

        var result = await _manager.ValidateAsync("token");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Handle, Is.EqualTo(session.SessionHandle));
        Assert.That(result.Handle, Is.Not.EqualTo(session.Id));
        Assert.That(result.UserId, Is.EqualTo(session.UserId));
        Assert.That(result.Level, Is.EqualTo(AuthenticationLevel.Active));
    }

    [Test]
    public async Task MfaPendingSession_MapsToMfaPending()
    {
        StoreReturns("token", NewSession(AuthLevel.MfaPending, expiresIn: TimeSpan.FromHours(1)));

        var result = await _manager.ValidateAsync("token");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Level, Is.EqualTo(AuthenticationLevel.MfaPending));
    }

    [Test]
    public async Task ValidSession_UpdatesLastActivity()
    {
        var session = NewSession(AuthLevel.Active, expiresIn: TimeSpan.FromHours(1));
        StoreReturns("token", session);

        await _manager.ValidateAsync("token");

        _store.Verify(s => s.TouchAsync(session, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ExpiredSession_DoesNotUpdateLastActivity()
    {
        StoreReturns("token", NewSession(AuthLevel.Active, expiresIn: TimeSpan.FromMinutes(-1)));

        await _manager.ValidateAsync("token");

        _store.Verify(s => s.TouchAsync(It.IsAny<Session>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Opozvana sesija: čeka opoziv iz US-03 (RevokedAt se trenutno ne može postaviti).

    private void StoreReturns(string token, Session? session) =>
        _store.Setup(s => s.FindByTokenAsync(token, It.IsAny<CancellationToken>()))
              .ReturnsAsync(session);

    private static Session NewSession(AuthLevel level, TimeSpan expiresIn)
    {
        var now = DateTimeOffset.UtcNow;
        return new Session(Guid.NewGuid(), Guid.NewGuid(), new byte[32], Guid.NewGuid(),
            level, now, now + expiresIn);
    }
}