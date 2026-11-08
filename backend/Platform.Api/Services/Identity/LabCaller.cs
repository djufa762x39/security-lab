using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Domain.Identity;

namespace Platform.Api.Services.Identity;

public sealed class LabCaller(IIdentityContext identityContext, IAccountRepository accounts)
{
    public SubjectRef? GetSubject()
    {
        var identity = identityContext.Current;

        if (identity is null || !identity.IsAuthenticated || identity.UserId is not Guid userId || userId == Guid.Empty)
        {
            return null;
        }

        var account = accounts.Find(userId);

        var state = account?.Status switch
        {
            AccountStatus.Active => AccountState.Active,
            AccountStatus.Archived => AccountState.Archived,
            _ => AccountState.Blocked
        };

        return new SubjectRef(userId.ToString("D"), state);
    }
}