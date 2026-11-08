using Platform.Access.Domain.Enums;
//using Platform.Api.Services.Identity;
using Platform.Membership.Contracts;
//using ApiAccountStatus = Platform.Api.Domain.Identity.AccountStatus;
using Platform.Api.Interfaces.Identity;
using ApiAccountStatus = Platform.Identity.Enums.AccountStatus;
namespace Platform.Api.Modules.Membership;
/// <summary>
/// PRIVREMENI ADAPTER (ACC-02): status naloga čitamo iz IAccountRepository koji koristi IDN-03.
/// Kada Identity tim izloži zajednički ugovor za status naloga, menja se samo ova klasa.
/// </summary>
public sealed class AccountRepositoryStatusReader(IAccountRepository accounts) : IAccountStatusReader
{
    public ValueTask<AccountState?> GetStatusAsync(
    Guid accountId,
    CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AccountState? state = accounts.Find(accountId)?.Status switch
        {
            null => null,
            ApiAccountStatus.Active => AccountState.Active,
            ApiAccountStatus.Blocked => AccountState.Blocked,
            _ => AccountState.Archived
        };
        return ValueTask.FromResult(state);
    }
}