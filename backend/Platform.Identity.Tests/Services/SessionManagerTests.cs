using Moq;
using NUnit.Framework;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;

namespace Platform.Identity.Tests.Services;

[TestFixture]
[Category("IDN-02")]
public class SessionManagerTests
{
    private Mock<ISessionManager> _sessionManager = null!;

    [SetUp]
    public void SetUp()
    {
        _sessionManager = new Mock<ISessionManager>();
    }

    [Test]
    public async Task RevokeAllForUser_ReturnsConfiguredRevocationCount()
    {
        var userId = Guid.NewGuid();
        using var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;

        _sessionManager
            .Setup(manager => manager.RevokeAllForUser(userId, "account blocked", cancellationToken))
            .ReturnsAsync(3);

        var result = await _sessionManager.Object.RevokeAllForUser(
            userId,
            "account blocked",
            cancellationToken);

        Assert.That(result, Is.EqualTo(3));
        _sessionManager.Verify(
            manager => manager.RevokeAllForUser(userId, "account blocked", cancellationToken),
            Times.Once);
    }

    [Test]
    public async Task RevokeSession_ReturnsConfiguredResult()
    {
        var userId = Guid.NewGuid();
        var sessionHandle = Guid.NewGuid();

        _sessionManager
            .Setup(manager => manager.RevokeSession(userId, sessionHandle, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _sessionManager.Object.RevokeSession(userId, sessionHandle);

        Assert.That(result, Is.True);
        _sessionManager.Verify(
            manager => manager.RevokeSession(userId, sessionHandle, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task RevokeSession_ReturnsFalseWhenConfiguredForUnknownOrForeignSession()
    {
        var userId = Guid.NewGuid();
        var sessionHandle = Guid.NewGuid();

        _sessionManager
            .Setup(manager => manager.RevokeSession(userId, sessionHandle, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _sessionManager.Object.RevokeSession(userId, sessionHandle);

        Assert.That(result, Is.False);
        _sessionManager.Verify(
            manager => manager.RevokeSession(userId, sessionHandle, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ListForUser_ReturnsConfiguredSessionInfo()
    {
        var userId = Guid.NewGuid();
        var handle = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;
        IReadOnlyList<SessionInfo> expected =
        [
            new SessionInfo(handle, createdAt, createdAt, createdAt.AddHours(8))
        ];

        _sessionManager
            .Setup(manager => manager.ListForUser(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await _sessionManager.Object.ListForUser(userId);

        Assert.That(result, Is.EqualTo(expected));
        _sessionManager.Verify(
            manager => manager.ListForUser(userId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ValidateAsync_ReturnsConfiguredSession()
    {
        var token = "session-token";
        var expected = new ValidateSession(
            Guid.NewGuid(),
            Guid.NewGuid(),
            AuthenticationLevel.Active);

        _sessionManager
            .Setup(manager => manager.ValidateAsync(token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await _sessionManager.Object.ValidateAsync(token);

        Assert.That(result, Is.EqualTo(expected));
        _sessionManager.Verify(
            manager => manager.ValidateAsync(token, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ValidateAsync_ReturnsNullForConfiguredUnknownToken()
    {
        const string token = "unknown-token";
        _sessionManager
            .Setup(manager => manager.ValidateAsync(token, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ValidateSession?)null);

        var result = await _sessionManager.Object.ValidateAsync(token);

        Assert.That(result, Is.Null);
        _sessionManager.Verify(
            manager => manager.ValidateAsync(token, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public void SessionInfo_ExposesNoTokenOrHash()
    {
        var names = typeof(SessionInfo).GetProperties().Select(property => property.Name.ToLowerInvariant());

        Assert.That(names, Has.None.Contains("token"));
        Assert.That(names, Has.None.Contains("hash"));
        Assert.That(names, Has.None.Contains("cookie"));
    }
}
