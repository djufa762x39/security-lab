using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Platform.Identity.Entities;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;

namespace Platform.Identity.Services;

/// <summary>
/// IDN-05: provera lozinke preko ASP.NET Core Identity sa lockoutOnFailure.
/// Brojač neuspeha i LockoutEnd čuva Identity u tabeli AspNetUsers; uspešna provera resetuje brojač.
/// </summary>
public sealed class IdentityPasswordSignInCheck(
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    IPasswordHasher<AppUser> passwordHasher) : IPasswordSignInCheck
{
    private const int MaxEmailLength = 254;
    private const int MaxPasswordLength = 128;

    private static readonly AppUser DummyUser = new() { Id = Guid.Empty };
    private static string? s_dummyHash;

    public async Task<PasswordCheckResult> CheckAsync(
        string? email,
        string? password,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalized = (email ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length is 0 or > MaxEmailLength
            || string.IsNullOrEmpty(password) || password.Length > MaxPasswordLength)
        {
            VerifyDummy(string.Empty);
            return PasswordCheckResult.Invalid();
        }

        var user = await userManager.FindByEmailAsync(normalized);
        if (user is null)
        {
            // Nepostojeći nalog prolazi isto heširanje kao postojeći, da vreme odgovora ne otkriva nalog.
            VerifyDummy(password);
            return PasswordCheckResult.Invalid();
        }

        // Identity ne hešira lozinku kad je nalog već zaključan, pa to nadoknađujemo lažnim hešom.
        var wasLockedOut = await userManager.IsLockedOutAsync(user);

        var result = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);

        if (result.Succeeded)
            return PasswordCheckResult.Valid(user.Id, user.Status);

        if (result.IsLockedOut)
        {
            if (wasLockedOut)
                VerifyDummy(password);

            return PasswordCheckResult.LockedOut(user.Id);
        }

        return PasswordCheckResult.Invalid();
    }

    private void VerifyDummy(string password) =>
        passwordHasher.VerifyHashedPassword(DummyUser, DummyHash(), password);

    // Isti heš algoritam i parametri kao za prave naloge. Trka pri prvom pozivu je bezopasna.
    private string DummyHash() =>
        s_dummyHash ??= passwordHasher.HashPassword(
            DummyUser, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
}
