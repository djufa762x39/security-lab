using Platform.Identity.Models;

namespace Platform.Identity.Interfaces;

/// <summary>
/// IDN-05: provera lozinke sa zaključavanjem naloga. Ne pravi sesiju; to radi IDN-02.
/// Svaki ishod koji nije Valid pozivalac mora prikazati istom generičkom porukom.
/// </summary>
public interface IPasswordSignInCheck
{
    Task<PasswordCheckResult> CheckAsync(
        string? email,
        string? password,
        CancellationToken cancellationToken = default);
}
