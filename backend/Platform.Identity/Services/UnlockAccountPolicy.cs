using Microsoft.Extensions.Configuration;
using Platform.Access.Contracts;
using Platform.Access.Domain.Models;

namespace Platform.Identity.Services;

/// IDN-05: samo administrator sme da otključa nalog.
public sealed class UnlockAccountPolicy(IConfiguration configuration) : IAccessPolicy
{
    public const string ActionId = "identity.account.unlock";

    public string Id => "identity.account.unlock.admin-only";
    public string Version => "v1";

    public ValueTask<bool> EvaluateAsync(
        AccessRequest request,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(AdminAccounts.IsAdministrator(configuration, request.Subject));
}
