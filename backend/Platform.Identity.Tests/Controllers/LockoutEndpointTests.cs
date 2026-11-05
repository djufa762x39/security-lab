using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Persistence;
using Testcontainers.PostgreSql;

namespace Platform.Identity.Tests.Controllers;

// IDN-05 through the real Platform.Api host: real login, real session cookies, real Postgres.
// Nothing is replaced. The database is a throwaway Testcontainers Postgres.
[TestFixture]
[Category("IDN-05")]
public sealed class LockoutEndpointTests
{
    private const string Password = "Str0ng-Password!2026";
    private const string WrongPassword = "Wr0ng-Password!2026";
    private const int MaxAttempts = 3;

    private static readonly Guid AdminId = Guid.NewGuid();
    private const string AdminEmail = "lockout-admin@example.com";

    private sealed class Factory(string connectionString, string maxAttempts = "3") : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.UseSetting("ConnectionStrings:Identity", connectionString);
            builder.UseSetting("Identity:AdminAccountIds:0", AdminId.ToString());
            builder.UseSetting("Identity:Lockout:MaxFailedAccessAttempts", maxAttempts);
        }
    }

    private PostgreSqlContainer _db = null!;
    private Factory _factory = null!;
    private string _passwordHash = null!;

    [OneTimeSetUp]
    public async Task StartHost()
    {
        _db = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await _db.StartAsync();

        _factory = new Factory(_db.GetConnectionString());

        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>().Database.MigrateAsync();

        _passwordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Hash(Password);
        await CreateAccountAsync(AdminId, AdminEmail, AccountRole.Administrator);
    }

    [OneTimeTearDown]
    public async Task StopHost()
    {
        await _factory.DisposeAsync();
        await _db.DisposeAsync();
    }

    [Test]
    public async Task Unknown_wrong_and_locked_logins_get_identical_responses()
    {
        var (_, wrongEmail) = await CreateStudentAsync();
        var (_, lockedEmail) = await CreateStudentAsync();
        await LockAsync(lockedEmail);

        var unknown = await LoginAsync($"nobody-{Guid.NewGuid():N}@example.com", Password);
        var wrong = await LoginAsync(wrongEmail, WrongPassword);
        var lockedWrong = await LoginAsync(lockedEmail, WrongPassword);
        var lockedCorrect = await LoginAsync(lockedEmail, Password);

        Assert.That(unknown.Status, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(unknown.Body, Does.Contain("invalid_credentials"));
        Assert.That(wrong, Is.EqualTo(unknown));
        Assert.That(lockedWrong, Is.EqualTo(unknown));
        Assert.That(lockedCorrect, Is.EqualTo(unknown));
    }

    [Test]
    public async Task The_attempt_that_triggers_the_lockout_looks_like_any_other_failure()
    {
        var (_, email) = await CreateStudentAsync();

        var first = await LoginAsync(email, WrongPassword);
        await LoginAsync(email, WrongPassword);
        var triggering = await LoginAsync(email, WrongPassword);

        Assert.That(triggering, Is.EqualTo(first));
    }

    [Test]
    public async Task Successful_login_resets_the_counter_so_typos_do_not_add_up()
    {
        var (_, email) = await CreateStudentAsync();

        for (var round = 0; round < 3; round++)
        {
            for (var i = 0; i < MaxAttempts - 1; i++)
                await LoginAsync(email, WrongPassword);

            Assert.That((await LoginAsync(email, Password)).Status, Is.EqualTo(HttpStatusCode.OK));
        }
    }

    [Test]
    public async Task Admin_with_a_real_session_unlocks_an_account_and_login_works_again()
    {
        var (id, email) = await CreateStudentAsync();
        await LockAsync(email);
        using var admin = await LoggedInClientAsync(AdminEmail);

        using var response = await admin.PostAsync($"/api/admin/accounts/{id}/unlock", null);
        var body = await response.Content.ReadFromJsonAsync<UnlockResponse>();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body!.Id, Is.EqualTo(id));
        Assert.That(body.Unlocked, Is.True);
        Assert.That((await LoginAsync(email, Password)).Status, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Unlock_without_a_session_is_refused_and_changes_nothing()
    {
        var (id, email) = await CreateStudentAsync();
        await LockAsync(email);
        using var anonymous = NewClient();

        using var response = await anonymous.PostAsync($"/api/admin/accounts/{id}/unlock", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await LoginAsync(email, Password)).Status, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Non_admin_gets_the_same_403_for_existing_and_unknown_accounts()
    {
        var (lockedId, lockedEmail) = await CreateStudentAsync();
        await LockAsync(lockedEmail);
        var (_, studentEmail) = await CreateStudentAsync();
        using var student = await LoggedInClientAsync(studentEmail);

        using var existing = await student.PostAsync($"/api/admin/accounts/{lockedId}/unlock", null);
        using var unknown = await student.PostAsync($"/api/admin/accounts/{Guid.NewGuid()}/unlock", null);

        Assert.That(existing.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await existing.Content.ReadFromJsonAsync<ErrorResponse>())!.Error,
            Is.EqualTo((await unknown.Content.ReadFromJsonAsync<ErrorResponse>())!.Error));
        Assert.That((await LoginAsync(lockedEmail, Password)).Status, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Admin_unlock_of_an_unknown_account_is_not_found()
    {
        using var admin = await LoggedInClientAsync(AdminEmail);

        using var response = await admin.PostAsync($"/api/admin/accounts/{Guid.NewGuid()}/unlock", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await response.Content.ReadFromJsonAsync<ErrorResponse>())!.Error, Is.EqualTo("account_not_found"));
    }

    [Test]
    public void Host_refuses_to_start_with_an_invalid_lockout_setting()
    {
        using var factory = new Factory(_db.GetConnectionString(), maxAttempts: "0");

        Assert.Catch<OptionsValidationException>(() => factory.CreateClient());
    }

    private sealed record UnlockResponse(Guid Id, bool Unlocked, string CorrelationId);

    private sealed record ErrorResponse(string Error, string CorrelationId);

    // The session cookie is Secure, so the client must talk https for it to be sent back.
    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost")
    });

    private async Task<HttpClient> LoggedInClientAsync(string email)
    {
        var client = NewClient();
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return client;
    }

    private async Task<(HttpStatusCode Status, string? ContentType, string? CacheControl, string Body)> LoginAsync(
        string email, string password)
    {
        using var client = NewClient();
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        return (
            response.StatusCode,
            response.Content.Headers.ContentType?.ToString(),
            response.Headers.CacheControl?.ToString(),
            await response.Content.ReadAsStringAsync());
    }

    private async Task LockAsync(string email)
    {
        for (var i = 0; i < MaxAttempts; i++)
            await LoginAsync(email, WrongPassword);
    }

    private async Task<(Guid Id, string Email)> CreateStudentAsync()
    {
        var id = Guid.NewGuid();
        var email = $"lockout-{id:N}@example.com";
        await CreateAccountAsync(id, email, AccountRole.Student);
        return (id, email);
    }

    private async Task CreateAccountAsync(Guid id, string email, AccountRole role)
    {
        var account = new UserAccount(id, email, role: role);
        account.SetPasswordHash(_passwordHash);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
    }
}
