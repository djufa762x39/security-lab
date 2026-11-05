using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Platform.Identity.Tests;

// IDN-05: podešavanja zaključavanja. Ne treba baza.
[TestFixture]
public sealed class IdentityLockoutOptionsTests
{
    [Test]
    public void Defaults_lock_after_five_failures_for_fifteen_minutes()
    {
        using var provider = Build(new Dictionary<string, string?>());

        var lockout = provider.GetRequiredService<IOptions<IdentityOptions>>().Value.Lockout;

        Assert.That(lockout.MaxFailedAccessAttempts, Is.EqualTo(5));
        Assert.That(lockout.DefaultLockoutTimeSpan, Is.EqualTo(TimeSpan.FromMinutes(15)));
        Assert.That(lockout.AllowedForNewUsers, Is.True);
    }

    [Test]
    public void Configured_values_are_applied_to_identity_options()
    {
        using var provider = Build(new Dictionary<string, string?>
        {
            ["Identity:Lockout:MaxFailedAccessAttempts"] = "3",
            ["Identity:Lockout:LockoutMinutes"] = "30"
        });

        var lockout = provider.GetRequiredService<IOptions<IdentityOptions>>().Value.Lockout;

        Assert.That(lockout.MaxFailedAccessAttempts, Is.EqualTo(3));
        Assert.That(lockout.DefaultLockoutTimeSpan, Is.EqualTo(TimeSpan.FromMinutes(30)));
    }

    [TestCase("0", "15")]
    [TestCase("21", "15")]
    [TestCase("5", "0")]
    [TestCase("5", "1441")]
    [TestCase("-1", "-1")]
    public void Out_of_range_values_are_rejected(string attempts, string minutes)
    {
        using var provider = Build(new Dictionary<string, string?>
        {
            ["Identity:Lockout:MaxFailedAccessAttempts"] = attempts,
            ["Identity:Lockout:LockoutMinutes"] = minutes
        });

        Assert.Throws<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<IdentityLockoutOptions>>().Value);
        Assert.Throws<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<IdentityOptions>>().Value);
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        settings["ConnectionStrings:Identity"] = "Host=127.0.0.1;Database=unused";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddIdentityModule();
        return services.BuildServiceProvider();
    }
}
