using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;
using Platform.Identity.Persistence;
using Platform.Identity.Services;
using Testcontainers.PostgreSql;

namespace Platform.Identity.Tests.Services;

// IDN-05: lockout on the real login store (identity.accounts).
// The database is a throwaway Testcontainers Postgres, never a shared one.
[TestFixture]
[Category("IDN-05")]
public sealed class AccountLockoutTests
{
    private const string Password = "Str0ng-Password!2026";
    private const string WrongPassword = "Wr0ng-Password!2026";
    private const int MaxAttempts = 3;
    private const int LockoutMinutes = 15;

    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));
    private readonly CountingHasher _hasher = new();
    private PostgreSqlContainer _db = null!;
    private ServiceProvider _provider = null!;
    private string _passwordHash = null!;

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
        services.AddSingleton<TimeProvider>(_clock);
        services.AddIdentityModule(config);
        services.Replace(ServiceDescriptor.Singleton<IPasswordHasher>(_hasher));

        _provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        _passwordHash = _hasher.Hash(Password);

        using var scope = _provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>().Database.MigrateAsync();
    }

    [OneTimeTearDown]
    public async Task StopDatabase()
    {
        await _provider.DisposeAsync();
        await _db.DisposeAsync();
    }

    [Test]
    public async Task Failures_below_the_limit_do_not_lock_the_account()
    {
        var (id, email) = await CreateAccountAsync();

        for (var i = 0; i < MaxAttempts - 1; i++)
            Assert.That(await VerifyAsync(email, WrongPassword), Is.EqualTo(CredentialResult.Invalid));

        var account = await FindAsync(id);
        Assert.That(account.FailedLoginCount, Is.EqualTo(MaxAttempts - 1));
        Assert.That(account.LockoutEnd, Is.Null);
    }

    [Test]
    public async Task Reaching_the_limit_locks_the_account_for_the_configured_time()
    {
        var (id, email) = await CreateAccountAsync();

        var last = await FailAsync(email, MaxAttempts);

        Assert.That(last.IsLockedOut, Is.True);
        Assert.That(last.IsValid, Is.False);
        Assert.That(last.AccountId, Is.EqualTo(id));

        var account = await FindAsync(id);
        Assert.That(account.LockoutEnd, Is.EqualTo(_clock.GetUtcNow().AddMinutes(LockoutMinutes)));
        Assert.That(account.FailedLoginCount, Is.EqualTo(0));
    }

    [Test]
    public async Task Correct_password_is_rejected_while_the_account_is_locked()
    {
        var (id, email) = await CreateAccountAsync();
        await FailAsync(email, MaxAttempts);

        var result = await VerifyAsync(email, Password);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.IsLockedOut, Is.True);
        Assert.That((await FindAsync(id)).LockoutEnd, Is.GreaterThan(_clock.GetUtcNow()));
    }

    [Test]
    public async Task Attempts_while_locked_are_not_counted_and_do_not_extend_the_lockout()
    {
        var (id, email) = await CreateAccountAsync();
        await FailAsync(email, MaxAttempts);
        var lockoutEnd = (await FindAsync(id)).LockoutEnd;

        _clock.Advance(TimeSpan.FromMinutes(1));
        await FailAsync(email, MaxAttempts + 1);

        var account = await FindAsync(id);
        Assert.That(account.LockoutEnd, Is.EqualTo(lockoutEnd));
        Assert.That(account.FailedLoginCount, Is.EqualTo(0));
    }

    [Test]
    public async Task Successful_login_resets_the_failure_counter()
    {
        var (id, email) = await CreateAccountAsync();
        await FailAsync(email, MaxAttempts - 1);

        var success = await VerifyAsync(email, Password);

        Assert.That(success.IsValid, Is.True);
        Assert.That(success.AccountId, Is.EqualTo(id));
        Assert.That(success.Status, Is.EqualTo(AccountStatus.Active));
        Assert.That((await FindAsync(id)).FailedLoginCount, Is.EqualTo(0));

        // After the reset a full run of failures is needed again.
        Assert.That((await FailAsync(email, MaxAttempts - 1)).IsLockedOut, Is.False);
    }

    [Test]
    public async Task Expired_lockout_allows_login_and_is_cleared()
    {
        var (id, email) = await CreateAccountAsync();
        await FailAsync(email, MaxAttempts);

        _clock.Advance(TimeSpan.FromMinutes(LockoutMinutes).Add(TimeSpan.FromSeconds(1)));
        var result = await VerifyAsync(email, Password);

        Assert.That(result.IsValid, Is.True);
        Assert.That((await FindAsync(id)).LockoutEnd, Is.Null);
    }

    [Test]
    public async Task Lockout_expiring_exactly_now_is_over()
    {
        var (_, email) = await CreateAccountAsync();
        await FailAsync(email, MaxAttempts);

        _clock.Advance(TimeSpan.FromMinutes(LockoutMinutes));

        Assert.That((await VerifyAsync(email, Password)).IsValid, Is.True);
    }

    [Test]
    public async Task After_an_expired_lockout_a_full_run_of_failures_is_needed_again()
    {
        var (_, email) = await CreateAccountAsync();
        await FailAsync(email, MaxAttempts);
        _clock.Advance(TimeSpan.FromMinutes(LockoutMinutes).Add(TimeSpan.FromSeconds(1)));

        Assert.That((await FailAsync(email, MaxAttempts - 1)).IsLockedOut, Is.False);
        Assert.That((await FailAsync(email, 1)).IsLockedOut, Is.True);
    }

    [Test]
    public async Task Parallel_failures_are_all_counted()
    {
        var (id, email) = await CreateAccountAsync();

        await Task.WhenAll(Enumerable.Range(0, MaxAttempts)
            .Select(_ => Task.Run(() => VerifyAsync(email, WrongPassword))));

        Assert.That((await FindAsync(id)).LockoutEnd, Is.GreaterThan(_clock.GetUtcNow()));
    }

    [Test]
    public async Task Email_case_and_whitespace_count_against_the_same_account()
    {
        var (id, email) = await CreateAccountAsync();

        await VerifyAsync(email.ToUpperInvariant(), WrongPassword);
        await VerifyAsync($"  {email} ", WrongPassword);

        Assert.That((await FindAsync(id)).FailedLoginCount, Is.EqualTo(2));
    }

    [Test]
    public async Task One_account_failing_does_not_touch_another()
    {
        var (_, email) = await CreateAccountAsync();
        var (otherId, otherEmail) = await CreateAccountAsync();

        await FailAsync(email, MaxAttempts);

        Assert.That((await FindAsync(otherId)).FailedLoginCount, Is.EqualTo(0));
        Assert.That((await VerifyAsync(otherEmail, Password)).IsValid, Is.True);
    }

    [Test]
    public async Task Unknown_email_is_invalid_verifies_a_hash_and_creates_nothing()
    {
        var email = NewEmail();
        _hasher.Reset();

        for (var i = 0; i < MaxAttempts + 1; i++)
            Assert.That(await VerifyAsync(email, WrongPassword), Is.EqualTo(CredentialResult.Invalid));

        Assert.That(_hasher.Verifications, Is.EqualTo(MaxAttempts + 1));

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();
        Assert.That(await db.Accounts.CountAsync(a => a.Email == email), Is.EqualTo(0));
    }

    [Test]
    public async Task Locked_account_still_verifies_a_hash()
    {
        var (_, email) = await CreateAccountAsync();
        await FailAsync(email, MaxAttempts);
        _hasher.Reset();

        await VerifyAsync(email, WrongPassword);

        Assert.That(_hasher.Verifications, Is.EqualTo(1));
    }

    [Test]
    public async Task Unlock_clears_lockout_and_counter_so_the_correct_password_works()
    {
        var (id, email) = await CreateAccountAsync();
        await FailAsync(email, MaxAttempts);

        var outcome = await UnlockAsync(id);

        Assert.That(outcome, Is.EqualTo(UnlockOutcome.Unlocked));
        var account = await FindAsync(id);
        Assert.That(account.LockoutEnd, Is.Null);
        Assert.That(account.FailedLoginCount, Is.EqualTo(0));
        Assert.That((await VerifyAsync(email, Password)).IsValid, Is.True);
    }

    [Test]
    public async Task Unlock_of_an_account_that_is_not_locked_resets_the_counter_and_says_so()
    {
        var (id, email) = await CreateAccountAsync();
        await VerifyAsync(email, WrongPassword);

        Assert.That(await UnlockAsync(id), Is.EqualTo(UnlockOutcome.NotLocked));
        Assert.That((await FindAsync(id)).FailedLoginCount, Is.EqualTo(0));
    }

    [Test]
    public async Task Unlock_of_an_expired_lockout_is_not_locked()
    {
        var (id, email) = await CreateAccountAsync();
        await FailAsync(email, MaxAttempts);
        _clock.Advance(TimeSpan.FromMinutes(LockoutMinutes).Add(TimeSpan.FromSeconds(1)));

        Assert.That(await UnlockAsync(id), Is.EqualTo(UnlockOutcome.NotLocked));
    }

    [Test]
    public async Task Unlock_of_an_unknown_account_is_not_found()
    {
        Assert.That(await UnlockAsync(Guid.NewGuid()), Is.EqualTo(UnlockOutcome.NotFound));
    }

    private static string NewEmail() => $"lockout-{Guid.NewGuid():N}@example.com";

    private async Task<(Guid Id, string Email)> CreateAccountAsync()
    {
        var account = new UserAccount(Guid.NewGuid(), NewEmail());
        account.SetPasswordHash(_passwordHash);

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();
        db.Accounts.Add(account);
        await db.SaveChangesAsync();

        return (account.Id, account.Email);
    }

    private async Task<CredentialResult> FailAsync(string email, int attempts)
    {
        var last = CredentialResult.Invalid;
        for (var i = 0; i < attempts; i++)
            last = await VerifyAsync(email, WrongPassword);

        return last;
    }

    private async Task<CredentialResult> VerifyAsync(string email, string password)
    {
        using var scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICredentialVerifier>().VerifyAsync(email, password);
    }

    private async Task<UnlockOutcome> UnlockAsync(Guid id)
    {
        using var scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IAccountLockoutAdministration>().UnlockAsync(id);
    }

    private async Task<UserAccount> FindAsync(Guid id)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();
        return await db.Accounts.AsNoTracking().SingleAsync(a => a.Id == id);
    }

    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    // Counts checks, so the hash work on every path is proven without measuring time.
    private sealed class CountingHasher : IPasswordHasher
    {
        private readonly BCryptPasswordHasher _inner = new();
        private int _verifications;

        public int Verifications => Volatile.Read(ref _verifications);

        public void Reset() => Interlocked.Exchange(ref _verifications, 0);

        public string Hash(string password) => _inner.Hash(password);

        public bool Verify(string password, string? passwordHash)
        {
            Interlocked.Increment(ref _verifications);
            return _inner.Verify(password, passwordHash);
        }
    }
}
