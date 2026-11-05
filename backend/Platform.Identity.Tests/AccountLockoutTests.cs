using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Platform.Identity.Entities;
using Platform.Identity.Interfaces;
using Platform.Identity.Enums;
using Platform.Identity.Models;
using Platform.Identity.Persistence;
using Testcontainers.PostgreSql;

namespace Platform.Identity.Tests;

// IDN-05: zaključavanje naloga posle uzastopnih neuspešnih prijava.
// Baza je jednokratni Testcontainers Postgres, nikad deljena baza.
[TestFixture]
public sealed class AccountLockoutTests
{
    private const string Password = "Str0ng-Password!2026";
    private const string WrongPassword = "Wr0ng-Password!2026";
    private const int MaxAttempts = 3;
    private const int LockoutMinutes = 15;

    private PostgreSqlContainer _db = null!;
    private ServiceProvider _provider = null!;
    private readonly HashCounter _hashes = new();

    [OneTimeSetUp]
    public async Task StartDatabase()
    {
        _db = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await _db.StartAsync();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Identity"] = _db.GetConnectionString(),
                ["Identity:Lockout:MaxFailedAccessAttempts"] = MaxAttempts.ToString(),
                ["Identity:Lockout:LockoutMinutes"] = LockoutMinutes.ToString()
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddIdentityModule();

        // Broji provere heša, da se lažni heš za nepostojeći/zaključan nalog proveri bez merenja vremena.
        services.AddSingleton(_hashes);
        services.AddScoped<IPasswordHasher<AppUser>>(sp => new CountingPasswordHasher(
            new PasswordHasher<AppUser>(sp.GetRequiredService<IOptions<PasswordHasherOptions>>()),
            sp.GetRequiredService<HashCounter>()));

        _provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        using var scope = _provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AccountDbContext>()
            .Database.MigrateAsync();
    }

    [OneTimeTearDown]
    public async Task StopDatabase()
    {
        await _provider.DisposeAsync();
        await _db.DisposeAsync();
    }

    [Test]
    public async Task Failures_below_limit_do_not_lock_the_account()
    {
        var (id, email) = await CreateAccountAsync();

        for (var i = 0; i < MaxAttempts - 1; i++)
        {
            var result = await CheckAsync(email, WrongPassword);
            Assert.That(result.Outcome, Is.EqualTo(PasswordCheckOutcome.Invalid));
        }

        var user = await FindAsync(id);
        Assert.That(user.AccessFailedCount, Is.EqualTo(MaxAttempts - 1));
        Assert.That(user.LockoutEnd, Is.Null);
    }

    [Test]
    public async Task Reaching_the_limit_locks_the_account_for_the_configured_time()
    {
        var (id, email) = await CreateAccountAsync();
        var before = DateTimeOffset.UtcNow;

        PasswordCheckResult last = null!;
        for (var i = 0; i < MaxAttempts; i++)
            last = await CheckAsync(email, WrongPassword);

        Assert.That(last.Outcome, Is.EqualTo(PasswordCheckOutcome.LockedOut));
        Assert.That(last.AccountId, Is.EqualTo(id));

        var user = await FindAsync(id);
        Assert.That(user.LockoutEnd, Is.Not.Null);
        Assert.That(user.LockoutEnd!.Value,
            Is.EqualTo(before.AddMinutes(LockoutMinutes)).Within(TimeSpan.FromMinutes(1)));
    }

    [Test]
    public async Task Correct_password_is_rejected_while_the_account_is_locked()
    {
        var (id, email) = await CreateAccountAsync();
        await LockAsync(email);

        var result = await CheckAsync(email, Password);

        Assert.That(result.Outcome, Is.EqualTo(PasswordCheckOutcome.LockedOut));
        Assert.That(result.Status, Is.Null);
        Assert.That((await FindAsync(id)).LockoutEnd, Is.GreaterThan(DateTimeOffset.UtcNow));
    }

    [Test]
    public async Task Successful_login_resets_the_failure_counter()
    {
        var (id, email) = await CreateAccountAsync();

        for (var i = 0; i < MaxAttempts - 1; i++)
            await CheckAsync(email, WrongPassword);

        var success = await CheckAsync(email, Password);
        Assert.That(success.Outcome, Is.EqualTo(PasswordCheckOutcome.Valid));
        Assert.That(success.AccountId, Is.EqualTo(id));
        Assert.That(success.Status, Is.EqualTo(AccountStatus.Active));
        Assert.That((await FindAsync(id)).AccessFailedCount, Is.EqualTo(0));

        // Posle reseta opet treba punih MaxAttempts grešaka za zaključavanje.
        for (var i = 0; i < MaxAttempts - 1; i++)
        {
            var result = await CheckAsync(email, WrongPassword);
            Assert.That(result.Outcome, Is.EqualTo(PasswordCheckOutcome.Invalid));
        }
    }

    [Test]
    public async Task Expired_lockout_allows_login_again()
    {
        var (id, email) = await CreateAccountAsync();
        await LockAsync(email);

        using (var scope = _provider.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await users.FindByIdAsync(id.ToString());
            await users.SetLockoutEndDateAsync(user!, DateTimeOffset.UtcNow.AddMinutes(-1));
        }

        var result = await CheckAsync(email, Password);

        Assert.That(result.Outcome, Is.EqualTo(PasswordCheckOutcome.Valid));
    }

    [Test]
    public async Task Email_is_matched_case_insensitively_and_trimmed()
    {
        var (_, email) = await CreateAccountAsync();

        var result = await CheckAsync($"  {email.ToUpperInvariant()} ", Password);

        Assert.That(result.Outcome, Is.EqualTo(PasswordCheckOutcome.Valid));
    }

    [Test]
    public async Task Unknown_email_is_invalid_and_still_verifies_a_hash()
    {
        _hashes.Reset();

        var result = await CheckAsync($"nobody-{Guid.NewGuid():N}@example.com", Password);

        Assert.That(result.Outcome, Is.EqualTo(PasswordCheckOutcome.Invalid));
        Assert.That(result.AccountId, Is.Null);
        Assert.That(_hashes.Verifications, Is.EqualTo(1));
    }

    [Test]
    public async Task Already_locked_account_still_verifies_a_hash()
    {
        var (_, email) = await CreateAccountAsync();
        await LockAsync(email);
        _hashes.Reset();

        var result = await CheckAsync(email, WrongPassword);

        Assert.That(result.Outcome, Is.EqualTo(PasswordCheckOutcome.LockedOut));
        Assert.That(_hashes.Verifications, Is.EqualTo(1));
    }

    [Test]
    public async Task Wrong_password_verifies_exactly_one_hash()
    {
        var (_, email) = await CreateAccountAsync();
        _hashes.Reset();

        await CheckAsync(email, WrongPassword);

        Assert.That(_hashes.Verifications, Is.EqualTo(1));
    }

    [TestCase(null, Password)]
    [TestCase("", Password)]
    [TestCase("someone@example.com", null)]
    [TestCase("someone@example.com", "")]
    public async Task Missing_input_is_invalid_and_still_verifies_a_hash(string? email, string? password)
    {
        _hashes.Reset();

        var result = await CheckAsync(email, password);

        Assert.That(result.Outcome, Is.EqualTo(PasswordCheckOutcome.Invalid));
        Assert.That(_hashes.Verifications, Is.EqualTo(1));
    }

    [Test]
    public async Task Unknown_account_never_creates_lockout_state()
    {
        var email = $"nobody-{Guid.NewGuid():N}@example.com";

        for (var i = 0; i < MaxAttempts + 2; i++)
        {
            var result = await CheckAsync(email, WrongPassword);
            Assert.That(result.Outcome, Is.EqualTo(PasswordCheckOutcome.Invalid));
        }

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountDbContext>();
        Assert.That(await db.Users.CountAsync(u => u.NormalizedEmail == email.ToUpperInvariant()), Is.EqualTo(0));
    }

    [Test]
    public async Task Unlock_clears_lockout_and_counter_so_correct_password_works()
    {
        var (id, email) = await CreateAccountAsync();
        await LockAsync(email);

        var outcome = await UnlockAsync(id);

        Assert.That(outcome, Is.EqualTo(UnlockOutcome.Unlocked));
        var user = await FindAsync(id);
        Assert.That(user.LockoutEnd, Is.Null);
        Assert.That(user.AccessFailedCount, Is.EqualTo(0));
        Assert.That((await CheckAsync(email, Password)).Outcome, Is.EqualTo(PasswordCheckOutcome.Valid));
    }

    [Test]
    public async Task Unlock_of_account_that_is_not_locked_reports_not_locked_and_resets_counter()
    {
        var (id, email) = await CreateAccountAsync();
        await CheckAsync(email, WrongPassword);

        var outcome = await UnlockAsync(id);

        Assert.That(outcome, Is.EqualTo(UnlockOutcome.NotLocked));
        Assert.That((await FindAsync(id)).AccessFailedCount, Is.EqualTo(0));
    }

    [Test]
    public async Task Unlock_of_unknown_account_reports_not_found()
    {
        Assert.That(await UnlockAsync(Guid.NewGuid()), Is.EqualTo(UnlockOutcome.NotFound));
    }

    private async Task<(Guid Id, string Email)> CreateAccountAsync()
    {
        var email = $"lockout-{Guid.NewGuid():N}@example.com";
        using var scope = _provider.CreateScope();
        var registration = scope.ServiceProvider.GetRequiredService<IAccountRegistration>();
        var result = await registration.RegisterAsync(new RegisterAccountCommand(email, Password));
        Assert.That(result.Outcome, Is.EqualTo(RegistrationOutcome.Created));
        return (result.AccountId!.Value, email);
    }

    private async Task LockAsync(string email)
    {
        for (var i = 0; i < MaxAttempts; i++)
            await CheckAsync(email, WrongPassword);
    }

    private async Task<PasswordCheckResult> CheckAsync(string? email, string? password)
    {
        using var scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IPasswordSignInCheck>().CheckAsync(email, password);
    }

    private async Task<UnlockOutcome> UnlockAsync(Guid id)
    {
        using var scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IAccountLockoutAdministration>().UnlockAsync(id);
    }

    private async Task<AppUser> FindAsync(Guid id)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountDbContext>();
        return await db.Users.AsNoTracking().SingleAsync(u => u.Id == id);
    }

    private sealed class HashCounter
    {
        private int _verifications;
        public int Verifications => Volatile.Read(ref _verifications);
        public void Increment() => Interlocked.Increment(ref _verifications);
        public void Reset() => Interlocked.Exchange(ref _verifications, 0);
    }

    private sealed class CountingPasswordHasher(IPasswordHasher<AppUser> inner, HashCounter counter)
        : IPasswordHasher<AppUser>
    {
        public string HashPassword(AppUser user, string password) => inner.HashPassword(user, password);

        public PasswordVerificationResult VerifyHashedPassword(AppUser user, string hashedPassword, string providedPassword)
        {
            counter.Increment();
            return inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
        }
    }
}
