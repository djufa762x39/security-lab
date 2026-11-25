using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Api.Middleware;
using Platform.Api.Services.Identity;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Persistence;
using Platform.Identity.Tests.Helpers;
using Platform.Identity.Testing;

namespace Platform.Identity.Tests.Integration;

/// <summary>
/// US-15 / SEC-03 through the real HTTP pipeline and PostgreSQL. A state-changing request that carries our session
/// cookie (or is a login) must also carry a valid CSRF token; without it the request is refused with
/// <c>403 csrf_invalid</c> and nothing changes, not even the session's last activity. The attack scenario is the
/// revocation of a session: a foreign page can make the browser send the cookie, but it cannot read the token or
/// add the header. The token is bound to the session cookie, so a pair only works with the session it was issued for.
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

    // Must be an id in Stub:AdminIds of appsettings.Development.json (as in AccountStatusChangeIntegrationTests).
    private static readonly Guid AdminId = Guid.Parse("11111111-1111-1111-1111-111111111111");

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
        var pair = await CsrfPair.FetchAsync(_http, caller.RawToken);

        using var again = new HttpRequestMessage(HttpMethod.Get, "/api/auth/csrf");
        again.Headers.Add("Cookie", $"{SessionCookie.Name}={caller.RawToken}; {pair.Cookie}");
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
        var pair = await CsrfPair.FetchAsync(_http, caller.RawToken);

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
        var victimPair = await CsrfPair.FetchAsync(_http, caller.RawToken);
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
        var pair = await CsrfPair.FetchAsync(_http, caller.RawToken);

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
        var pair = await CsrfPair.FetchAsync(_http, caller.RawToken);

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

    // A refused request has no side effect (review of PR #228, 2a)

    [Test]
    public async Task RefusedRequest_DoesNotExtendTheSession()
    {
        var user = await UserTestData.AddAsync(Factory, "user@example.com", Password);
        var caller = await SessionTestData.CreateAsync(Factory, user.Id);
        var otherDevice = await SessionTestData.CreateAsync(Factory, user.Id);
        var before = (await SessionTestData.GetAsync(Factory, caller.Session.Id))!.LastActivityAt;

        using var forged = new HttpRequestMessage(HttpMethod.Delete, $"/api/auth/sessions/{otherDevice.Session.SessionHandle}");
        forged.Headers.Add("Cookie", $"{SessionCookie.Name}={caller.RawToken}");
        forged.Headers.Add("Origin", AttackerOrigin);
        using var response = await _http.SendAsync(forged);

        await AssertCsrfRejectedAsync(response);
        Assert.That((await SessionTestData.GetAsync(Factory, caller.Session.Id))!.LastActivityAt, Is.EqualTo(before),
            "a refused request must not count as activity (US-14 inactivity)");
    }

    // The token is bound to the session (review of PR #228, 2b)

    [Test]
    public async Task TokenOfAnotherSession_IsRejected_AndChangesNothing()
    {
        var user = await UserTestData.AddAsync(Factory, "user@example.com", Password);
        var other = await UserTestData.AddAsync(Factory, "other@example.com", Password);
        var caller = await SessionTestData.CreateAsync(Factory, user.Id);
        var otherDevice = await SessionTestData.CreateAsync(Factory, user.Id);
        var otherUser = await SessionTestData.CreateAsync(Factory, other.Id);
        var pairOfOtherUser = await CsrfPair.FetchAsync(_http, otherUser.RawToken);

        // A complete, valid pair, but issued for another session in the same browser.
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/auth/sessions/{otherDevice.Session.SessionHandle}");
        pairOfOtherUser.ApplyTo(request, caller.RawToken);
        using var response = await _http.SendAsync(request);

        await AssertCsrfRejectedAsync(response);
        Assert.That(await MeStatusAsync(otherDevice.RawToken), Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Login_IssuesATokenForTheNewSession_AndTheOldTokenStopsWorking()
    {
        var user = await UserTestData.AddAsync(Factory, "user@example.com", Password);
        var otherDevice = await SessionTestData.CreateAsync(Factory, user.Id);
        var anonymous = await CsrfPair.FetchAsync(_http);

        using var login = new HttpRequestMessage(HttpMethod.Post, LoginUrl)
        {
            Content = JsonContent.Create(new { email = "user@example.com", password = Password })
        };
        anonymous.ApplyTo(login);
        using var loggedIn = await _http.SendAsync(login);
        Assert.That(loggedIn.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var session = SetCookieValue(loggedIn, SessionCookie.Name);
        var issued = SetCookieValue(loggedIn, CsrfToken.ReadableCookieName);
        Assert.That(session, Is.Not.Null);
        Assert.That(issued, Is.Not.Null.And.Not.EqualTo(anonymous.Token), "login issues a request token for the new session");

        using var withOldToken = new HttpRequestMessage(HttpMethod.Delete, $"/api/auth/sessions/{otherDevice.Session.SessionHandle}");
        anonymous.ApplyTo(withOldToken, session);
        using var refused = await _http.SendAsync(withOldToken);
        await AssertCsrfRejectedAsync(refused);

        using var withNewToken = new HttpRequestMessage(HttpMethod.Delete, $"/api/auth/sessions/{otherDevice.Session.SessionHandle}");
        (anonymous with { Token = issued! }).ApplyTo(withNewToken, session);
        using var revoked = await _http.SendAsync(withNewToken);
        Assert.That(revoked.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task Logout_ClearsTheCsrfCookies_AndThePairOfTheEndedSessionIsRejectedOnTheNextLogin()
    {
        await UserTestData.AddAsync(Factory, "user@example.com", Password);
        var other = await UserTestData.AddAsync(Factory, "other@example.com", Password);
        var session = await SessionTestData.CreateAsync(Factory, other.Id);
        var pairOfEndedSession = await CsrfPair.FetchAsync(_http, session.RawToken);

        using var logout = new HttpRequestMessage(HttpMethod.Post, LogoutUrl);
        logout.Headers.Add("Cookie", $"{SessionCookie.Name}={session.RawToken}; {pairOfEndedSession.Cookie}");
        using var loggedOut = await _http.SendAsync(logout);
        Assert.That(loggedOut.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        foreach (var name in new[] { CsrfToken.CookieName, CsrfToken.ReadableCookieName })
            Assert.That(loggedOut.Headers.GetValues("Set-Cookie")
                    .Any(value => value.StartsWith(name + "=;", StringComparison.Ordinal) && value.Contains("1970")),
                Is.True, $"logout removes {name}");

        // The next user of the same browser logs in with the old pair: refused, and no session is issued.
        using var login = new HttpRequestMessage(HttpMethod.Post, LoginUrl)
        {
            Content = JsonContent.Create(new { email = "user@example.com", password = Password })
        };
        pairOfEndedSession.ApplyTo(login);
        using var response = await _http.SendAsync(login);

        await AssertCsrfRejectedAsync(response);
        Assert.That(SetCookieValue(response, SessionCookie.Name), Is.Null);
    }

    // Order of checks on IDN-03 and IDN-07 routes (review of PR #228, 3.2)

    [TestCase("revoked")]
    [TestCase("expired")]
    public async Task InvalidSession_WithoutToken_GetsSessionInvalid_NotTheCsrfAnswer(string state)
    {
        var user = await UserTestData.AddAsync(Factory, "user@example.com", Password);
        var session = await SessionTestData.CreateAsync(Factory, user.Id,
            lifetime: state == "expired" ? TimeSpan.FromMinutes(-1) : null);
        var otherDevice = await SessionTestData.CreateAsync(Factory, user.Id);
        if (state == "revoked")
            await RevokeAsync(session.Session.Id);

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/auth/sessions/{otherDevice.Session.SessionHandle}");
        request.Headers.Add("Cookie", $"{SessionCookie.Name}={session.RawToken}");
        using var response = await _http.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(await CodeAsync(response), Is.EqualTo(SessionCheckMiddleware.InvalidSessionCode));
        Assert.That(response.Headers.GetValues("Set-Cookie")
                .Any(value => value.StartsWith(SessionCookie.Name + "=", StringComparison.Ordinal) && value.Contains("1970")),
            Is.True, "the rejected session cookie is removed");
        Assert.That(await MeStatusAsync(otherDevice.RawToken), Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task AdminStatusChange_WithoutToken_IsRefused_AndChangesNoStatusOrSession()
    {
        var admin = new UserAccount(AdminId, "admin@example.com", AccountStatus.Active, AccountRole.Administrator);
        await InsertAsync(admin);
        var student = await UserTestData.AddAsync(Factory, "student@example.com", Password);
        var adminSession = await SessionTestData.CreateAsync(Factory, admin.Id);
        var studentSession = await SessionTestData.CreateAsync(Factory, student.Id);

        using var response = await StatusChangeWithoutTokenAsync(student.Id, adminSession.RawToken);

        await AssertCsrfRejectedAsync(response);
        Assert.That((await UserTestData.GetAsync(Factory, student.Id))!.Status, Is.EqualTo(AccountStatus.Active));
        Assert.That((await SessionTestData.GetAsync(Factory, studentSession.Session.Id))!.RevokedAt, Is.Null);
        Assert.That(await MeStatusAsync(studentSession.RawToken), Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task NonAdminStatusChange_WithoutToken_GetsTheSameAnswerForAnExistingAndAnUnknownAccount()
    {
        var student = await UserTestData.AddAsync(Factory, "student@example.com", Password);
        var target = await UserTestData.AddAsync(Factory, "target@example.com", Password);
        var studentSession = await SessionTestData.CreateAsync(Factory, student.Id);

        // The check runs before Access and before any account is read, so the answer tells nothing about accounts.
        using var existing = await StatusChangeWithoutTokenAsync(target.Id, studentSession.RawToken);
        using var unknown = await StatusChangeWithoutTokenAsync(Guid.NewGuid(), studentSession.RawToken);

        await AssertCsrfRejectedAsync(existing);
        await AssertCsrfRejectedAsync(unknown);
        Assert.That(await existing.Content.ReadAsStringAsync(), Is.EqualTo(await unknown.Content.ReadAsStringAsync()));
        Assert.That((await UserTestData.GetAsync(Factory, target.Id))!.Status, Is.EqualTo(AccountStatus.Active));
    }

    // Helpers

    private static async Task<HttpResponseMessage> StatusChangeWithoutTokenAsync(Guid accountId, string sessionToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/accounts/{accountId}/status")
        {
            Content = JsonContent.Create(new { status = "BLOCKED" })
        };
        request.Headers.Add("Cookie", $"{SessionCookie.Name}={sessionToken}");
        return await _http.SendAsync(request);
    }

    private static async Task RevokeAsync(Guid sessionId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();
        (await db.Sessions.SingleAsync(candidate => candidate.Id == sessionId)).Revoke(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
    }

    // The value of a cookie the response sets, or null when it does not set it (or only removes it).
    private static string? SetCookieValue(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.Where(value => value.StartsWith(name + "=", StringComparison.Ordinal))
                .Select(value => value.Split(';')[0][(name.Length + 1)..])
                .FirstOrDefault(value => value.Length > 0)
            : null;

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
