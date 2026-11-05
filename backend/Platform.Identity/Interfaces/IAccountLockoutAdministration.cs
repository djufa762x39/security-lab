using Platform.Identity.Enums;

namespace Platform.Identity.Interfaces;

/// <summary>
/// IDN-05: administratorsko otključavanje. Pravo pristupa proverava pozivalac
/// (IAccessDecision, akcija identity.account.unlock) pre poziva.
/// </summary>
public interface IAccountLockoutAdministration
{
    Task<UnlockOutcome> UnlockAsync(Guid accountId, CancellationToken cancellationToken = default);
}
