using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Platform.Api.Middleware;
using Platform.Api.Services.Identity;
using Platform.Identity.Tests.Helpers;
using Platform.Identity.Testing;

namespace Platform.Identity.Tests.Integration;

/// <summary>
/// US-15 / SEC-03 through the real HTTP pipeline and PostgreSQL. A state-changing request that carries our session
/// cookie (or is a login) must also carry a valid CSRF token; without it the request is refused with
/// <c>403 csrf_invalid</c> and nothing changes. The attack scenario is the revocation of a session: a foreign
/// page can make the browser send the cookie, but it cannot read the token or add the header.
/// </summary>
[Category("US-15")]
[Category("SEC-03")]
public class CsrfProtectionIntegrationTests : IntegrationTestBase
{
    private const string Password = "Password123!";
    private const string MeUrl = "/api/auth/me";
    private const string LoginUrl = "/api/auth/login";
    private const string LogoutUrl = "/api/auth/logout";
    private const string AttackerOrigin = "https://evil.example";

    // Cookies are sent explicitly per request, as a browser would, and never kept in a cookie jar.
    private static HttpClient _http = null!;

    [OneTimeSetUp]
    public void Prepare() =>
        _http = Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    [OneTimeTearDown]
    public void Release() => _http.Dispose();

    // The token endpoint

    [Test]
    public async Task CsrfEndpoint_IssuesTokenAndHostOnlyHttpOnlyStrictCookie_WithNoStore()
    {
        using var response = await _http.GetAsync("/api/auth/csrf");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);

        var setCookie = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith(CsrfToken.CookieName + "=", StringComparison.Ordinal));
        Assert.That(setCookie, Does.Contain("httponly").IgnoreCase);
        Assert.That(setCookie, Does.Contain("secure").IgnoreCase);
        Assert.That(setCookie, Does.Contain("samesite=strict").IgnoreCase);
        Assert.That(setCookie, Does.Contain("path=/").IgnoreCase);
        Assert.That(setCookie, Does.Not.Contain("domain=").IgnoreCase, "a __Host- cookie must not name a domain");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var token = body.RootElement.GetProperty("token").GetString();
        Assert.That(token, Is.Not.Null.And.Not.Empty);
        Assert.That(setCookie, Does.Not.Contain(token!), "the header token and the cookie token are different values");

        // The request token is also in a cookie the frontend can read (not HttpOnly), with the same other flags.
        var readable = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith(CsrfToken.ReadableCookieName + "=", StringComparison.Ordinal));
        Assert.That(readable, Does.StartWith($"{CsrfToken.ReadableCookieName}={token};"));
        Assert.That(readable, Does.Not.Contain("httponly").IgnoreCase);
        Assert.That(readable, Does.Contain("secure").IgnoreCase);
        Assert.That(readable, Does.Contain("samesite=strict").IgnoreCase);
        Assert.That(readable, Does.Contain("path=/").IgnoreCase);
    }

    [Test]
    public async Task CsrfEndpoint_WithExistingCookie_KeepsTheCookieToken_AndItsNewRequestTokenWorks()
    {
        var user = await UserTestData.AddAsync(Factory, "user@example.com", Password);
        var caller = await SessionTestData.CreateAsync(Factory, user.Id);
        var otherDevice = await SessionTestData.CreateAsync(Factory, user.Id);
        var pair = await CsrfPair.FetchAsync(_http);

        using var again = new HttpRequestMessage(HttpMethod.Get, "/api/auth/csrf");
        again.Headers.Add("Cookie", pair.Cookie);
        using var response = await _http.SendAsync(again);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Headers.GetValues("Set-Cookie")
            .Any(value => value.StartsWith(CsrfToken.CookieName + "=", StringComparison.Ordinal)), Is.False,
            "the HttpOnly cookie token is kept, so tokens already held by other tabs stay valid");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var refreshed = pair with { Token = body.RootElement.GetProperty("token").GetString()! };
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/auth/sessions/{otherDevice.Session.SessionHandle}");
        refreshed.ApplyTo(request, caller.RawToken);
        using var revoke = await _http.SendAsync(request);

        Assert.That(revoke.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    // The attack (negative) and the normal case (positive)

    [Test]
    public async Task CsrfAttack_RevokeWithSessionCookieButNoToken_Returns403_AndTheSessionStaysValid()
    {
        var user = await UserTestData.AddAsync(Factory, "victim@example.com", Password);
        var victim = await SessionTestData.CreateAsync(Factory, user.Id);
        var otherDevice = await SessionTestData.CreateAsync(Factory, user.Id);

        // What a foreign page can do: the browser adds the session cookie, the page cannot add a token.
        using var forged = new HttpRequestMessage(HttpMethod.Delete, $"/api/auth/sessions/{otherDevice.Session.SessionHandle}");
        forged.Headers.Add("Cookie", $"{SessionCookie.Name}={victim.RawToken}");
        forged.Headers.Add("Origin", AttackerOrigin);
        using var response = await _http.SendAsync(forged);

        await AssertCsrfRejectedAsync(response);
        Assert.That(await MeStatusAsync(otherDevice.RawToken), Is.EqualTo(HttpStatusCode.OK), "the session was not revoked");
        Assert.That(await MeStatusAsync(victim.RawToken), Is.EqualTo(HttpStatusCode.OK), "a refused request does not end the session");
    }

    [Test]
    public async Task RevokeWithSessionCookieAndValidToken_Returns204_AndRevokesTheSession()
    {
        var user = await UserTestData.AddAsync(Factory, "user@example.com", Password);
        var caller = await SessionTestData.CreateAsync(Factory, user.Id);
        var otherDevice = await SessionTestData.CreateAsync(Factory, user.Id);
        var pair = await CsrfPair.FetchAsync(_http);

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/auth/sessions/{otherDevice.Session.SessionHandle}");
        pair.ApplyTo(request, caller.RawToken);
        using var response = await _http.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(await MeStatusAsync(otherDevice.RawToken), Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task TokenFromAnotherBrowser_IsRejected_AndChangesNothing()
    {
        var user = await UserTestData.AddAsync(Factory, "user@example.com", Password);
        var caller = await SessionTestData.CreateAsync(Factory, user.Id);
        var otherDevice = await SessionTestData.CreateAsync(Factory, user.Id);
        var victimPair = await CsrfPair.FetchAsync(_http);
        var attackerPair = await CsrfPair.FetchAsync(_http);

        // The attacker can get a valid token of their own, but it does not match the victim's cookie.
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/auth/sessions/{otherDevice.Session.SessionHandle}");
        request.Headers.Add("Cookie", $"{SessionCookie.Name}={caller.RawToken}; {victimPair.Cookie}");
        request.Headers.Add(CsrfToken.HeaderName, attackerPair.Token);
        using var response = await _http.SendAsync(request);

        await AssertCsrfRejectedAsync(response);
        Assert.That(await MeStatusAsync(otherDevice.RawToken), Is.EqualTo(HttpStatusCode.OK));
    }

    [TestCase("not-a-token")]
    [TestCase("")]
    public async Task ForgedOrEmptyToken_IsRejected_AndChangesNothing(string token)
    {
        var user = await UserTestData.AddAsync(Factory, "user@example.com", Password);
        var caller = await SessionTestData.CreateAsync(Factory, user.Id);
        var otherDevice = await SessionTestData.CreateAsync(Factory, user.Id);
        var pair = await CsrfPair.FetchAsync(_http);

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/auth/sessions/{otherDevice.Session.SessionHandle}");
        request.Headers.Add("Cookie", $"{SessionCookie.Name}={caller.RawToken}; {pair.Cookie}");
        request.Headers.TryAddWithoutValidation(CsrfToken.HeaderName, token);
        using var response = await _http.SendAsync(request);

        await AssertCsrfRejectedAsync(response);
        Assert.That(await MeStatusAsync(otherDevice.RawToken), Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task TokenWithoutItsCookie_IsRejected_AndChangesNothing()
    {
        var user = await UserTestData.AddAsync(Factory, "user@example.com", Password);
        var caller = await SessionTestData.CreateAsync(Factory, user.Id);
        var otherDevice = await SessionTestData.CreateAsync(Factory, user.Id);
        var pair = await CsrfPair.FetchAsync(_http);

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/auth/sessions/{otherDevice.Session.SessionHandle}");
        request.Headers.Add("Cookie", $"{SessionCookie.Name}={caller.RawToken}");
        request.Headers.Add(CsrfToken.HeaderName, pair.Token);
        using var response = await _http.SendAsync(request);

        await AssertCsrfRejectedAsync(response);
        Assert.That(await MeStatusAsync(otherDevice.RawToken), Is.EqualTo(HttpStatusCode.OK));
    }

    // Login CSRF

    [Test]
    public async Task Login_WithoutToken_Returns403_AndIssuesNoSession()
    {
        await UserTestData.AddAsync(Factory, "user@example.com", Password);

        using var request = new HttpRequestMessage(HttpMethod.Post, LoginUrl)
        {
            Content = JsonContent.Create(new { email = "user@example.com", password = Password })
        };
        request.Headers.Add("Origin", AttackerOrigin);
        using var response = await _http.SendAsync(request);

        await AssertCsrfRejectedAsync(response);
        var setsSession = response.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.Any(value => value.StartsWith(SessionCookie.Name + "=", StringComparison.Ordinal));
        Assert.That(setsSession, Is.False);
    }

    [Test]
    public async Task Login_WithValidToken_Returns200_AndSetsTheSessionCookie()
    {
        await UserTestData.AddAsync(Factory, "user@example.com", Password);
        var pair = await CsrfPair.FetchAsync(_http);

        using var request = new HttpRequestMessage(HttpMethod.Post, LoginUrl)
        {
            Content = JsonContent.Create(new { email = "user@example.com", password = Password })
        };
        pair.ApplyTo(request);
        using var response = await _http.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Headers.GetValues("Set-Cookie")
            .Any(value => value.StartsWith(SessionCookie.Name + "=", StringComparison.Ordinal)), Is.True);
    }

    // What is deliberately not checked

    [Test]
    public async Task Logout_WithoutToken_StillRevokesTheSession()
    {
        var user = await UserTestData.AddAsync(Factory, "user@example.com", Password);
        var session = await SessionTestData.CreateAsync(Factory, user.Id);

        using var request = new HttpRequestMessage(HttpMethod.Post, LogoutUrl);
        request.Headers.Add("Cookie", $"{SessionCookie.Name}={session.RawToken}");
        using var response = await _http.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(await MeStatusAsync(session.RawToken), Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task SafeRequest_WithSessionCookieAndNoToken_IsNotChecked()
    {
        var user = await UserTestData.AddAsync(Factory, "user@example.com", Password);
        var session = await SessionTestData.CreateAsync(Factory, user.Id);

        Assert.That(await MeStatusAsync(session.RawToken), Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task StateChangingRequest_WithoutSessionCookie_GetsTheSessionAnswerNotTheCsrfAnswer()
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/auth/sessions/{Guid.NewGuid()}");
        using var response = await _http.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(await CodeAsync(response), Is.EqualTo(SessionCheckMiddleware.InvalidSessionCode));
    }

    // Helpers

    private static async Task<HttpStatusCode> MeStatusAsync(string sessionToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, MeUrl);
        request.Headers.Add("Cookie", $"{SessionCookie.Name}={sessionToken}");
        using var response = await _http.SendAsync(request);
        return response.StatusCode;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("code").GetString();
    }

    private static async Task AssertCsrfRejectedAsync(HttpResponseMessage response)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
        Assert.That(await CodeAsync(response), Is.EqualTo(CsrfToken.InvalidCode));
    }
}
