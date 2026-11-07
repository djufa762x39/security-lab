using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Platform.Api.Controllers.Identity;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Api.Services.Identity;
using Moq;

namespace Platform.Identity.Tests.Controllers;

[TestFixture]
[Category("US-06")]
public class AuthControllerTests
{
    private Mock<ILoginService> _login = null!;
    private Mock<ILogoutService> _logout = null!;
    private Mock<IIdentityContext> _identity = null!;
    private AuthController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _login = new Mock<ILoginService>();
        _logout = new Mock<ILogoutService>();
        _identity = new Mock<IIdentityContext>();
        _controller = new AuthController(_login.Object, _logout.Object, _identity.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    [Test]
    public void NoCaller_Returns401()
    {
        CallerIs(null);

        var result = _controller.Me();

        Assert.That(result, Is.InstanceOf<UnauthorizedResult>());
    }

    [Test]
    public void MfaPendingCaller_Returns401()
    {
        CallerIs(new CallerIdentity(false, Guid.NewGuid(), AuthenticationLevel.MfaPending, Guid.NewGuid()));

        var result = _controller.Me();

        Assert.That(result, Is.InstanceOf<UnauthorizedResult>());
    }

    [Test]
    public void ActiveCaller_ReturnsOnlyUserIdAndLevel()
    {
        var userId = Guid.NewGuid();
        CallerIs(new CallerIdentity(true, userId, AuthenticationLevel.Active, Guid.NewGuid()));

        var result = _controller.Me() as OkObjectResult;

        Assert.That(result, Is.Not.Null);
        var body = result!.Value!;
        var names = body.GetType().GetProperties().Select(p => p.Name).ToArray();
        Assert.That(names, Is.EquivalentTo(["userId", "level"]));
        Assert.That(body.GetType().GetProperty("userId")!.GetValue(body), Is.EqualTo(userId));
        Assert.That(body.GetType().GetProperty("level")!.GetValue(body), Is.EqualTo("Active"));
        Assert.That(_controller.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
    }

    private void CallerIs(CallerIdentity? caller) =>
        _identity.Setup(i => i.Current).Returns(caller);

    [Test]
    public async Task Logout_RevokesCookieSession_AndClearsTheCookieWithItsAttributes()
    {
        const string token = "opaque-session-token-value";
        var http = HttpContextWithCookie(token);
        _controller.ControllerContext.HttpContext = http;

        var response = await _controller.Logout();

        Assert.That(response, Is.InstanceOf<NoContentResult>());
        _logout.Verify(
            service => service.ExecuteAsync(token, http.TraceIdentifier, CancellationToken.None),
            Times.Once);
        Assert.That(http.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
        AssertCookieCleared(http);
    }

    [Test]
    public async Task Logout_ClientDisconnect_DoesNotCancelTheRevocation()
    {
        const string token = "opaque-session-token-value";
        var http = HttpContextWithCookie(token);
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();
        http.RequestAborted = aborted.Token;
        _controller.ControllerContext.HttpContext = http;

        await _controller.Logout();

        _logout.Verify(
            service => service.ExecuteAsync(token, http.TraceIdentifier, CancellationToken.None),
            Times.Once);
    }

    [TestCase(null)]
    [TestCase("unknown-token")]
    public async Task Logout_WithoutValidSession_AnswersTheSame(string? token)
    {
        var http = HttpContextWithCookie(token);
        _controller.ControllerContext.HttpContext = http;

        var response = await _controller.Logout();

        Assert.That(response, Is.InstanceOf<NoContentResult>());
        _logout.Verify(
            service => service.ExecuteAsync(token, http.TraceIdentifier, CancellationToken.None),
            Times.Once);
        AssertCookieCleared(http);
    }

    [Test]
    public async Task Logout_WhenRevokingFails_KeepsTheCookie()
    {
        const string token = "opaque-session-token-value";
        var http = HttpContextWithCookie(token);
        _controller.ControllerContext.HttpContext = http;
        _logout
            .Setup(service => service.ExecuteAsync(It.IsAny<string?>(), It.IsAny<string>(), CancellationToken.None))
            .ThrowsAsync(new InvalidOperationException("database is down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _controller.Logout());

        Assert.That(http.Response.Headers.ContainsKey("Set-Cookie"), Is.False);
    }

    private static DefaultHttpContext HttpContextWithCookie(string? token)
    {
        var http = new DefaultHttpContext();
        if (token is not null)
            http.Request.Headers.Cookie = $"{SessionCookie.Name}={token}";

        return http;
    }

    private static void AssertCookieCleared(DefaultHttpContext http)
    {
        var setCookie = http.Response.Headers.SetCookie.ToString().ToLowerInvariant();
        Assert.That(setCookie, Does.StartWith(SessionCookie.Name.ToLowerInvariant() + "=;"));
        Assert.That(setCookie, Does.Contain("expires=thu, 01 jan 1970"));
        Assert.That(setCookie,
            Does.Contain("path=/").And.Contain("httponly").And.Contain("secure").And.Contain("samesite=strict"));
    }
}