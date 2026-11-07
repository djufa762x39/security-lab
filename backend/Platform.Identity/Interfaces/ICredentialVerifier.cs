using Platform.Identity.Models;

namespace Platform.Identity.Interfaces;

public interface ICredentialVerifier
{
    /// <summary>Checks e-mail and password. It does NOT look at the account status; the caller decides.</summary>
    Task<CredentialResult> VerifyAsync(string email, string password, CancellationToken ct = default);
}
