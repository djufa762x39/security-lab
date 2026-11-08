using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Platform.Api.Development;
using Platform.Api.Models.Identity;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Persistence;
using Platform.Identity.Services;

namespace Platform.Identity.Tests.Development;

[TestFixture]
[Category("OPS-01")]
public class DevelopmentAccountSeederTests
{
    [Test]
    public void All_ContainsStudentTeacherAdministratorAndBlockedAccount()
    {
        Assert.That(DevelopmentAccountSeeder.All, Has.Count.EqualTo(4));
        Assert.That(DevelopmentAccountSeeder.All.Count(account => account.Status == AccountStatus.Blocked), Is.EqualTo(1));
        Assert.That(
            DevelopmentAccountSeeder.All
                .Where(account => account.Status == AccountStatus.Active)
                .Select(account => account.Role),
            Is.EquivalentTo(new[] { AccountRole.Student, AccountRole.Teacher, AccountRole.Administrator }));
    }

    [Test]
    public void All_HaveUniqueIdsAndEmails()
    {
        Assert.That(
            DevelopmentAccountSeeder.All.Select(account => account.Id).Distinct().Count(),
            Is.EqualTo(DevelopmentAccountSeeder.All.Count));
        Assert.That(
            DevelopmentAccountSeeder.All.Select(account => account.Email).Distinct(StringComparer.Ordinal).Count(),
            Is.EqualTo(DevelopmentAccountSeeder.All.Count));
    }

    [Test]
    public void All_UseTheReservedExampleDomain()
    {
        Assert.That(
            DevelopmentAccountSeeder.All.Select(account => account.Email),
            Has.All.Matches<string>(email => email is not null && email.EndsWith("@example.com")));
    }

    [Test]
    public void All_EmailsAreInTheNormalizedFormUsedByLogin()
    {
        Assert.That(
            DevelopmentAccountSeeder.All.Select(account => account.Email),
            Has.All.Matches<string>(email => email is not null && email == CredentialVerifier.NormalizeEmail(email)));
    }

    [Test]
    public void Administrator_UsesTheIdFromStubAdminIds()
    {
        var administrator = DevelopmentAccountSeeder.All.Single(account => account.Role == AccountRole.Administrator);
        Assert.That(administrator.Id, Is.EqualTo(DevelopmentAccountSeeder.AdminId));

        var settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.Development.json");
        if (!File.Exists(settingsPath))
            Assert.Ignore("appsettings.Development.json is not available in the test output directory.");

        using var settings = JsonDocument.Parse(File.ReadAllText(settingsPath));
        var adminIds = settings.RootElement.GetProperty("Stub").GetProperty("AdminIds").GetString();
        Assert.That(adminIds, Does.Contain(DevelopmentAccountSeeder.AdminId.ToString()));
    }

    [TestCase("Production")]
    [TestCase("Staging")]
    public void RequireDevelopment_Throws_OutsideDevelopment(string environmentName)
    {
        Assert.Throws<InvalidOperationException>(
            () => DevelopmentAccountSeeder.RequireDevelopment(EnvironmentNamed(environmentName)));
    }

    [Test]
    public void RequireDevelopment_Allows_Development()
    {
        Assert.DoesNotThrow(
            () => DevelopmentAccountSeeder.RequireDevelopment(EnvironmentNamed(Environments.Development)));
    }

    [TestCase("Production")]
    [TestCase("Staging")]
    public async Task SeedAsync_Throws_OutsideDevelopment_BeforeTouchingTheDatabase(string environmentName)
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => DevelopmentAccountSeeder.SeedAsync(
                context, new ThrowingHasher(), "any-dev-password", EnvironmentNamed(environmentName)));
    }

    [Test]
    public async Task SeedAsync_RejectsBlankPassword_BeforeTouchingTheDatabase()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();

        await Assert.ThrowsAsync<ArgumentException>(
            () => DevelopmentAccountSeeder.SeedAsync(
                context, new ThrowingHasher(), "   ", EnvironmentNamed(Environments.Development)));
    }

    [Test]
    public async Task SeedAsync_RejectsPasswordThatLoginWouldRefuse()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();
        var password = new string('x', CredentialVerifier.MaxPasswordLength + 1);

        await Assert.ThrowsAsync<ArgumentException>(
            () => DevelopmentAccountSeeder.SeedAsync(
                context, new ThrowingHasher(), password, EnvironmentNamed(Environments.Development)));
    }

    [Test]
    public async Task SeedAsync_CreatesAllFourAccounts_WithExpectedRoleAndStatus()
    {
        await using var context = InMemoryContext();

        var result = await SeedAsync(context);

        Assert.That(result.Added, Is.EqualTo(4));
        Assert.That(result.Updated, Is.Zero);
        var accounts = await context.Accounts.AsNoTracking().ToListAsync();
        Assert.That(accounts, Has.Count.EqualTo(4));
        foreach (var expected in DevelopmentAccountSeeder.All)
        {
            var actual = accounts.Single(account => account.Email == expected.Email);
            Assert.That(actual.Id, Is.EqualTo(expected.Id));
            Assert.That(actual.Role, Is.EqualTo(expected.Role));
            Assert.That(actual.Status, Is.EqualTo(expected.Status));
        }
    }

    [Test]
    public async Task SeedAsync_IsIdempotent_SecondRunAddsNothingAndKeepsHashes()
    {
        await using var context = InMemoryContext();

        var first = await SeedAsync(context);
        var hashes = await context.Accounts.AsNoTracking()
            .ToDictionaryAsync(account => account.Email, account => account.PasswordHash);
        var second = await SeedAsync(context);

        Assert.That(first.Added, Is.EqualTo(4));
        Assert.That(second.Added, Is.Zero);
        Assert.That(second.Updated, Is.Zero);
        Assert.That(second.PasswordsSet, Is.Zero);
        Assert.That(
            await context.Accounts.AsNoTracking().ToDictionaryAsync(account => account.Email, account => account.PasswordHash),
            Is.EqualTo(hashes));
    }

    [Test]
    public async Task SeedAsync_ChangedPassword_ReplacesTheHashesOfSeedAccounts()
    {
        await using var context = InMemoryContext();
        await SeedAsync(context);

        var result = await DevelopmentAccountSeeder.SeedWithoutMigrationCheckAsync(
            context, new TestHasher(), "changed-seed-password", EnvironmentNamed(Environments.Development));

        Assert.That(result.Added, Is.Zero);
        Assert.That(result.PasswordsSet, Is.EqualTo(4));
        Assert.That(
            await context.Accounts.AsNoTracking().Select(account => account.PasswordHash).ToListAsync(),
            Has.All.EqualTo("test-hash:changed-seed-password"));
    }


    [Test]
    public async Task SeedAsync_StoresOnlyAHash_NotThePassword()
    {
        await using var context = InMemoryContext();
        const string password = "seed-password-for-test";

        await DevelopmentAccountSeeder.SeedWithoutMigrationCheckAsync(
            context, new BCryptPasswordHasher(), password, EnvironmentNamed(Environments.Development));

        var accounts = await context.Accounts.AsNoTracking().ToListAsync();
        Assert.That(accounts, Has.Count.EqualTo(4));
        Assert.That(accounts, Has.All.Matches<UserAccount>(account =>
            account?.PasswordHash is not null &&
            account.PasswordHash != password &&
            new BCryptPasswordHasher().Verify(password, account.PasswordHash)));
    }

    [Test]
    public async Task SeedAsync_RestoresStatusAndRole_OfAnExistingSeedAccount()
    {
        await using var context = InMemoryContext();
        await SeedAsync(context);
        var blocked = await context.Accounts.SingleAsync(account => account.Email == "blocked@example.com");
        var teacher = await context.Accounts.SingleAsync(account => account.Email == "teacher@example.com");
        var hashes = new[] { blocked.PasswordHash, teacher.PasswordHash };
        blocked.Status = AccountStatus.Active;
        teacher.Role = AccountRole.Student;
        await context.SaveChangesAsync();

        var result = await SeedAsync(context);

        Assert.That(result.Added, Is.Zero);
        Assert.That(result.Updated, Is.EqualTo(2));
        Assert.That(blocked.Status, Is.EqualTo(AccountStatus.Blocked));
        Assert.That(teacher.Role, Is.EqualTo(AccountRole.Teacher));
        Assert.That(new[] { blocked.PasswordHash, teacher.PasswordHash }, Is.EqualTo(hashes));
    }

    [Test]
    public async Task SeedAsync_DoesNotTouchNonSeedAccounts()
    {
        await using var context = InMemoryContext();
        var other = new UserAccount(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "other@example.com",
            AccountStatus.Archived,
            AccountRole.Teacher);
        other.SetPasswordHash("existing-hash");
        context.Accounts.Add(other);
        await context.SaveChangesAsync();

        var result = await SeedAsync(context);

        Assert.That(result.Added, Is.EqualTo(4));
        Assert.That(result.Updated, Is.Zero);
        var saved = await context.Accounts.AsNoTracking().SingleAsync(account => account.Id == other.Id);
        Assert.That(saved.Status, Is.EqualTo(AccountStatus.Archived));
        Assert.That(saved.Role, Is.EqualTo(AccountRole.Teacher));
        Assert.That(saved.PasswordHash, Is.EqualTo("existing-hash"));
    }

    private static Task<SeedResult> SeedAsync(PlatformIdentityDbContext context) =>
        DevelopmentAccountSeeder.SeedWithoutMigrationCheckAsync(
            context, new TestHasher(), "seed-password-for-test", EnvironmentNamed(Environments.Development));

    private static PlatformIdentityDbContext InMemoryContext()
    {
        var options = new DbContextOptionsBuilder<PlatformIdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PlatformIdentityDbContext(options);
    }

    private static IHostEnvironment EnvironmentNamed(string name) =>
        WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = name }).Environment;

    private static ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{IdentityModule.ConnectionStringName}"] = "Host=localhost;Database=model_only"
            })
            .Build();

        return new ServiceCollection().AddIdentityModule(configuration).BuildServiceProvider();
    }

    private sealed class ThrowingHasher : IPasswordHasher
    {
        public string Hash(string password) => throw new InvalidOperationException("Hasher must not be called.");

        public bool Verify(string password, string? passwordHash) =>
            throw new InvalidOperationException("Hasher must not be called.");
    }

    private sealed class TestHasher : IPasswordHasher
    {
        public string Hash(string password) => $"test-hash:{password}";

        public bool Verify(string password, string? passwordHash) => passwordHash == Hash(password);
    }
}
