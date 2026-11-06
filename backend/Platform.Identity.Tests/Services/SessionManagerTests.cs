using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Platform.Api.Models.Identity;
using Platform.Api.Services.Identity;
using Platform.Identity.Entities;
using Platform.Identity.Persistence;

namespace Platform.Identity.Tests.Services;

[TestFixture]
[Category("IDN-02")]
[Category("IDN-07")]
public class SessionManagerTests
{
    [Test]
    public async Task ListForUser_ReturnsOnlyUnrevokedUnexpiredSessionsForRequestedUser()
    {
        // Arrange
        var requestedUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        // Active session for the requested user.
        var requestedActive = new Session(
            Guid.NewGuid(),
            requestedUserId,
            TokenHash(),
            Guid.NewGuid(),
            Platform.Identity.Enums.AuthLevel.Active,
            now.AddHours(-2),
            now.AddHours(6));

        // Revoked session for the requested user.
        var requestedRevoked = new Session(
            Guid.NewGuid(),
            requestedUserId,
            TokenHash(),
            Guid.NewGuid(),
            Platform.Identity.Enums.AuthLevel.Active,
            now.AddHours(-2),
            now.AddHours(6));

        requestedRevoked.Revoke(now);

        // Expired session for the requested user.
        var requestedExpired = new Session(
            Guid.NewGuid(),
            requestedUserId,
            TokenHash(),
            Guid.NewGuid(),
            Platform.Identity.Enums.AuthLevel.Active,
            now.AddHours(-8),
            now.AddMinutes(-1));

        // Active session for another user.
        var otherUserActive = new Session(
            Guid.NewGuid(),
            otherUserId,
            TokenHash(),
            Guid.NewGuid(),
            Platform.Identity.Enums.AuthLevel.Active,
            now.AddHours(-1),
            now.AddHours(6));

        var options = new DbContextOptionsBuilder<PlatformIdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new PlatformIdentityDbContext(options);

        db.Sessions.AddRange(
            requestedActive,
            requestedRevoked,
            requestedExpired,
            otherUserActive);

        await db.SaveChangesAsync();

        var manager = new SessionManager(db);

        // Act
        var result = await manager.ListForUser(requestedUserId);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));

        Assert.That(
            result[0].Handle,
            Is.EqualTo(requestedActive.SessionHandle));

        Assert.That(
            result[0].CreatedAt,
            Is.EqualTo(requestedActive.CreatedAt));

        Assert.That(
            result[0].LastActivityAt,
            Is.EqualTo(requestedActive.LastActivityAt));

        Assert.That(
            result[0].ExpiresAt,
            Is.EqualTo(requestedActive.ExpiresAt));
    }

    [Test]
    public async Task ListForUser_ReturnsEmptyListWhenUserHasNoSessions()
    {
        // Arrange
        var requestedUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var otherUserSession = new Session(
            Guid.NewGuid(),
            otherUserId,
            TokenHash(),
            Guid.NewGuid(),
            Platform.Identity.Enums.AuthLevel.Active,
            now.AddHours(-1),
            now.AddHours(6));

        var options = new DbContextOptionsBuilder<PlatformIdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new PlatformIdentityDbContext(options);

        db.Sessions.Add(otherUserSession);
        await db.SaveChangesAsync();

        var manager = new SessionManager(db);

        // Act
        var result = await manager.ListForUser(requestedUserId);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task ListForUser_ThrowsArgumentExceptionWhenUserIdIsEmpty()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<PlatformIdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new PlatformIdentityDbContext(options);

        var manager = new SessionManager(db);

        // Act & Assert
        Assert.That(
            async () => await manager.ListForUser(Guid.Empty),
            Throws.ArgumentException);
    }

    [Test]
    public void SessionInfo_ExposesNoTokenOrHash()
    {
        // Arrange & Act
        var propertyNames = typeof(SessionInfo)
            .GetProperties()
            .Select(property => property.Name.ToLowerInvariant());

        // Assert
        Assert.That(propertyNames, Has.None.Contains("token"));
        Assert.That(propertyNames, Has.None.Contains("hash"));
        Assert.That(propertyNames, Has.None.Contains("cookie"));
    }

    private static byte[] TokenHash() =>
        Guid.NewGuid().ToByteArray()
            .Concat(Guid.NewGuid().ToByteArray())
            .ToArray();
}