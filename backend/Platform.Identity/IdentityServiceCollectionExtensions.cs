using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Platform.Identity.Entities;
using Platform.Identity.Interfaces;
using Platform.Identity.Persistence;
using Platform.Identity.Services;

namespace Platform.Identity;

public static class IdentityServiceCollectionExtensions
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services)
    {
        services.AddDbContext<AccountDbContext>((sp, options) =>
            options.UseNpgsql(
                sp.GetRequiredService<IConfiguration>().GetConnectionString("Identity"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "identity")));

        // IDN-05: podešavanja zaključavanja, neispravna vrednost sprečava start servisa.
        services.AddOptions<IdentityLockoutOptions>()
            .BindConfiguration(IdentityLockoutOptions.Section)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<IdentityLockoutOptions>, IdentityLockoutOptionsValidator>();
        services.AddOptions<IdentityOptions>()
            .Configure<IOptions<IdentityLockoutOptions>>((options, lockout) =>
            {
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = lockout.Value.MaxFailedAccessAttempts;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(lockout.Value.LockoutMinutes);
            });

        services.AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 12;
            })
            .AddEntityFrameworkStores<AccountDbContext>()
            .AddSignInManager();

        // SignInManager traži IAuthenticationSchemeProvider; šeme ovde ne registrujemo (IDN-02).
        services.AddAuthentication();

        services.AddScoped<IAccountRegistration, AccountRegistrationService>();
        services.AddScoped<IPasswordSignInCheck, IdentityPasswordSignInCheck>();
        services.AddScoped<IAccountLockoutAdministration, AccountLockoutAdministrationService>();

        return services;
    }
}
