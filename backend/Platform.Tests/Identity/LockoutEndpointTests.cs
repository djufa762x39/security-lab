using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Services.Identity;
using Platform.Identity.Interfaces;
using Platform.Identity.Enums;

namespace Platform.Tests.Identity;

// IDN-05 kroz pravi Platform.Api host. Provera lozinke i otključavanje su zamenjeni lažnim
// implementacijama, pa test ne koristi nikakvu bazu.
[TestFixture]
public sealed class LockoutEndpointTests
{
    private const string AdminId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string StudentId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
    private static readonly Guid LockedAccountId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid ActiveAccountId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private const string CorrectPassword = "Str0ng-Password!2026";

    private sealed class SubjectHolder
    {
        public string? Id { get; set; }
    }

    private sealed class FakeSubjectResolver(SubjectHolder holder) : IRequestSubjectResolver
    {
        public SubjectRef? Resolve(HttpContext context) =>
            holder.Id is null ? null : new SubjectRef(holder.Id, AccountState.Active);
    }

    private sealed class FakeAuthenticator : ICredentialAuthenticator
    {
        public Task<CredentialAuthenticationResult> AuthenticateAsync(
            string email, string password, CancellationToken cancellationToken = default) =>
            Task.FromResult(email switch
            {
                "locked@example.com" => new CredentialAuthenticationResult(
                    CredentialAuthenticationOutcome.LockedOut, LockedAccountId),
                "active@example.com" when password == CorrectPassword => new CredentialAuthenticationResult(
                    CredentialAuthenticationOutcome.Valid, ActiveAccountId, AccountStatus.Active),
                _ => new CredentialAuthenticationResult(CredentialAuthenticationOutcome.Invalid)
            });
    }

    private sealed class FakeLockouts : IAccountLockoutAdministration
    {
        public List<Guid> Calls { get; } = new();

        public Task<UnlockOutcome> UnlockAsync(Guid accountId, CancellationToken cancellationToken = default)
        {
            Calls.Add(accountId);
            return Task.FromResult(accountId == LockedAccountId ? UnlockOutcome.Unlocked : UnlockOutcome.NotFound);
        }
    }

    private sealed class Factory(SubjectHolder subject, FakeLockouts lockouts) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Identity:AdminAccountIds:0"] = AdminId }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IRequestSubjectResolver>();
                services.AddSingleton(subject);
                services.AddSingleton<IRequestSubjectResolver, FakeSubjectResolver>();

                services.RemoveAll<ICredentialAuthenticator>();
                services.AddSingleton<ICredentialAuthenticator, FakeAuthenticator>();

                services.RemoveAll<IAccountLockoutAdministration>();
                services.AddSingleton<IAccountLockoutAdministration>(lockouts);
            });
        }
    }

    private SubjectHolder _subject = null!;
    private FakeLockouts _lockouts = null!;
    private Factory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _subject = new SubjectHolder();
        _lockouts = new FakeLockouts();
        _factory = new Factory(_subject, _lockouts);
        _client = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task Unknown_wrong_password_and_locked_logins_get_identical_responses()
    {
        var unknown = await LoginAsync("nobody@example.com", CorrectPassword);
        var wrong = await LoginAsync("active@example.com", "Wr0ng-Password!2026");
        var locked = await LoginAsync("locked@example.com", CorrectPassword);

        Assert.That(unknown.Status, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(wrong, Is.EqualTo(unknown));
        Assert.That(locked, Is.EqualTo(unknown));
        Assert.That(unknown.Body, Does.Contain("invalid_credentials"));
        Assert.That(unknown.Body, Does.Not.Contain("lock").IgnoreCase);
    }

    [Test]
    public async Task Valid_login_returns_the_temporary_fake_token()
    {
        var response = await LoginAsync("active@example.com", CorrectPassword);

        Assert.That(response.Status, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Body, Does.Contain("fake-"));
    }

    [Test]
    public async Task Unlock_without_subject_is_unauthenticated()
    {
        using var response = await _client.PostAsync($"/api/admin/accounts/{LockedAccountId}/unlock", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(_lockouts.Calls, Is.Empty);
    }

    [Test]
    public async Task Non_admin_gets_the_same_forbidden_for_existing_and_unknown_accounts()
    {
        _subject.Id = StudentId;

        using var existing = await _client.PostAsync($"/api/admin/accounts/{LockedAccountId}/unlock", null);
        using var unknown = await _client.PostAsync($"/api/admin/accounts/{Guid.NewGuid()}/unlock", null);

        Assert.That(existing.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(await ErrorOf(existing), Is.EqualTo(await ErrorOf(unknown)));
        Assert.That(_lockouts.Calls, Is.Empty);
    }

    [Test]
    public async Task Admin_unlocks_a_locked_account()
    {
        _subject.Id = AdminId;

        using var response = await _client.PostAsync($"/api/admin/accounts/{LockedAccountId}/unlock", null);
        var body = await response.Content.ReadFromJsonAsync<UnlockResponse>();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body!.Id, Is.EqualTo(LockedAccountId));
        Assert.That(body.Unlocked, Is.True);
        Assert.That(_lockouts.Calls, Is.EqualTo(new[] { LockedAccountId }));
    }

    [Test]
    public async Task Admin_unlock_of_unknown_account_is_not_found()
    {
        _subject.Id = AdminId;

        using var response = await _client.PostAsync($"/api/admin/accounts/{Guid.NewGuid()}/unlock", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await ErrorOf(response), Is.EqualTo("account_not_found"));
    }

    private sealed record UnlockResponse(Guid Id, bool Unlocked, string CorrelationId);

    private sealed record ErrorResponse(string Error, string CorrelationId);

    private static async Task<string?> ErrorOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ErrorResponse>())?.Error;

    private async Task<(HttpStatusCode Status, string? ContentType, string Body)> LoginAsync(string email, string password)
    {
        using var response = await _client.PostAsJsonAsync("/api/auth/login", new { email, password });
        return (response.StatusCode, response.Content.Headers.ContentType?.ToString(), await response.Content.ReadAsStringAsync());
    }
}
