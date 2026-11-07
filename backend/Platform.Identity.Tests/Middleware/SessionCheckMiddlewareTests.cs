using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Middleware;
using Platform.Api.Models.Identity;
using Platform.Api.Services.Identity;
using Moq;

namespace Platform.Identity.Tests.Middleware;

[TestFixture]
[Category("US-06")]
public class SessionCheckMiddlewareTests
{
    private bool _nextCalled;
    private Mock<ISessionManager> _sessions = null!;
    private SessionCheckMiddleware _middleware = null!;

    [SetUp]
    public void SetUp()
    {
        _sessions = new Mock<ISessionManager>();
        _nextCalled = false;

        RequestDelegate mockNextMiddleware = (Context) =>
        {
            _nextCalled = true;
            return Task.CompletedTask;
        };

        _middleware = new SessionCheckMiddleware(mockNextMiddleware);
    }

    [Test]
    public async Task UnknownEndpoint_NoCookie_CallNext()
    {
        var context = new DefaultHttpContext();

        await _middleware.Invoke(context, _sessions.Object);

        Assert.That(context.Response.StatusCode, Is.Not.EqualTo(401));
        Assert.That(_nextCalled, Is.True);
    }

    [Test]
    public async Task PublicEndpoint_NoCookie_CallsNext()
    {
        var endpoint = new Endpoint(null, new EndpointMetadataCollection(new AllowAnonymousAttribute()), "public");
        var context = new DefaultHttpContext();
        context.SetEndpoint(endpoint);

        await _middleware.Invoke(context, _sessions.Object);

        Assert.That(_nextCalled, Is.True);
        Assert.That(context.Response.StatusCode, Is.Not.EqualTo(401));
    }

    [Test]
    public async Task ProtectedEndpoint_NoCookie_Returns401_AndDoesNotCallNext()
    {
        var endpoint = new Endpoint(null, null, "protected");
        var context = new DefaultHttpContext();
        context.SetEndpoint(endpoint);

        await _middleware.Invoke(context, _sessions.Object);

        Assert.That(_nextCalled, Is.False);
        Assert.That(context.Response.StatusCode, Is.EqualTo(401));
    }

    [Test]
    public async Task ProtectedEndpoint_UnknownToken_Returns401_AndDoesNotCallNext()
    {
        var endpoint = new Endpoint(null, null, "protected");
        var context = new DefaultHttpContext();
        context.SetEndpoint(endpoint);
        SessionReturns("nepoznat-token", null);
        context.Request.Headers.Cookie = $"{SessionCookie.Name}=nepoznat-token";

        await _middleware.Invoke(context, _sessions.Object);

        Assert.That(_nextCalled, Is.False);
        Assert.That(context.Response.StatusCode, Is.EqualTo(401));
    }

    [Test]
    public async Task ProtectedEndpoint_ValidToken_CallNext_AndStoresSession()
    {
        var endpoint = new Endpoint(null, null, "protected");
        var context = new DefaultHttpContext();
        context.SetEndpoint(endpoint);
        var userId = Guid.NewGuid();
        var token = "valid-token";

        SessionReturns(token, new ValidateSession(Guid.NewGuid(), userId, AuthenticationLevel.Active));
        context.Request.Headers.Cookie = $"{SessionCookie.Name}={token}";

        await _middleware.Invoke(context, _sessions.Object);

        Assert.That(_nextCalled, Is.True);
        Assert.That(context.Response.StatusCode, Is.Not.EqualTo(401));

        var session = context.Items[SessionCheckMiddleware.SessionItemKey] as ValidateSession;

        Assert.That(session, Is.Not.Null);
        Assert.That(session!.UserId, Is.EqualTo(userId));

    }

    [Test]
    public async Task ProtectedEndpoint_RevokedSession_Returns401_AndDoesNotCallNext()
    {
        var endpoint = new Endpoint(null, null, "protected");
        var context = new DefaultHttpContext();
        context.SetEndpoint(endpoint);
        var userId = Guid.NewGuid();
        var token = "revoked-token";

        SessionReturns(token, null);

        context.Request.Headers.Cookie = $"{SessionCookie.Name}={token}";

        await _middleware.Invoke(context, _sessions.Object);

        Assert.That(_nextCalled, Is.False);
        Assert.That(context.Response.StatusCode, Is.EqualTo(401));
    }

    [Test]
    public async Task ProtectedEndpoint_ExpiredSession_Returns401_AndDoesNotCallNext()
    {
        var endpoint = new Endpoint(null, null, "protected");
        var context = new DefaultHttpContext();
        context.SetEndpoint(endpoint);
        var userId = Guid.NewGuid();
        var token = "expired-token";

        SessionReturns(token, null);
        context.Request.Headers.Cookie = $"{SessionCookie.Name}={token}";

        await _middleware.Invoke(context, _sessions.Object);

        Assert.That(_nextCalled, Is.False);
        Assert.That(context.Response.StatusCode, Is.EqualTo(401));
    }

    [TestCase("/api/laboratories")]
    [TestCase("/api/kt1/info")]
    [TestCase("/api/access/demo-deny")]
    public async Task DevelopmentModuleRoute_NoCookie_CallsNext(string path)
    {
        var context = ProtectedRequest(path, Environments.Development);

        await _middleware.Invoke(context, _sessions.Object);

        Assert.That(_nextCalled, Is.True);
        Assert.That(context.Response.StatusCode, Is.Not.EqualTo(401));
    }

    [TestCase("/api/laboratories")]
    [TestCase("/api/kt1/info")]
    [TestCase("/api/access/demo-deny")]
    public async Task ModuleRouteOutsideDevelopment_NoCookie_Returns401(string path)
    {
        var context = ProtectedRequest(path, Environments.Production);

        await _middleware.Invoke(context, _sessions.Object);

        Assert.That(_nextCalled, Is.False);
        Assert.That(context.Response.StatusCode, Is.EqualTo(401));
    }

    [TestCase("/api/kt1x")]
    [TestCase("/api/access/demo-deny-other")]
    [TestCase("/api/auth/me")]
    public async Task OtherRouteInDevelopment_NoCookie_Returns401(string path)
    {
        var context = ProtectedRequest(path, Environments.Development);

        await _middleware.Invoke(context, _sessions.Object);

        Assert.That(_nextCalled, Is.False);
        Assert.That(context.Response.StatusCode, Is.EqualTo(401));
    }

    private static DefaultHttpContext ProtectedRequest(string path, string environment)
    {
        var host = new Mock<IHostEnvironment>();
        host.SetupGet(h => h.EnvironmentName).Returns(environment);
        var services = new ServiceCollection().AddSingleton(host.Object).BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Path = path;
        context.SetEndpoint(new Endpoint(null, null, "protected"));
        return context;
    }

    private void SessionReturns(string token, ValidateSession? session) =>
        _sessions.Setup(s => s.ValidateAsync(token, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(session);
}