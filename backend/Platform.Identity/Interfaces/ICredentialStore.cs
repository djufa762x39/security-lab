using Platform.Identity.Models;

namespace Platform.Identity.Interfaces;

/// <summary>Finds the data needed for a login check. The e-mail passed in is already normalized.</summary>
public interface ICredentialStore
{
    Task<CredentialRecord?> FindByEmailAsync(string normalizedEmail, CancellationToken ct = default);
}
