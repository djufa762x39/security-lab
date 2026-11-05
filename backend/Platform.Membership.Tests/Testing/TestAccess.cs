using Platform.Access.Application.Services;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Infrastructure.Auditing;
using Platform.Access.Infrastructure.Catalog;
using Platform.Access.Infrastructure.Policies;
using Platform.Membership.Application;
namespace Platform.Membership.Tests.Testing;
/// <summary>
/// Pravi odlučivač Access modula (Tim 3, podrazumevano odbijanje), sa ili bez naše politike.
/// Tako testovi prolaze kroz isti put kao aplikacija.
/// </summary>
internal static class TestAccess
{
    public static IAccessDecision WithEnrollPolicy(IAccessPolicy? enrollPolicy)
    {
        var catalog = new InMemoryActionCatalog();
        var registry = new InMemoryAccessPolicyRegistry();
        if (enrollPolicy is not null)
        {
            catalog.Register(MembershipActions.EnrollMember);
            registry.Register(MembershipActions.EnrollMember, enrollPolicy);
        }
        return new DefaultDenyAccessDecision(catalog, registry, new NullAccessAuditWriter());
    }
    public static SubjectRef Subject(Guid accountId, AccountState state = AccountState.Active) =>
    new(accountId.ToString("D"), state);
}
/// <summary>Test politika koja sve dozvoljava; koristi se samo za testove poslovnih pravila upisa.</summary>
internal sealed class AllowAllPolicy : IAccessPolicy
{
    public string Id => "test.allow-all";
    public string Version => "test";
    public ValueTask<bool> EvaluateAsync(
    AccessRequest request,
    CancellationToken cancellationToken = default) =>
    ValueTask.FromResult(true);
}