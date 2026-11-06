using Microsoft.Extensions.DependencyInjection;
using Platform.Access.Infrastructure.Catalog;
using Platform.Access.Infrastructure.Policies;
using Platform.Membership.Application;

namespace Platform.Membership.Composition;

public static class MembershipAccessRegistration
{
    public static IServiceProvider RegisterMembershipAccess(
        this IServiceProvider services)
    {
        var catalog = services
            .GetRequiredService<InMemoryActionCatalog>();

        var registry = services
            .GetRequiredService<InMemoryAccessPolicyRegistry>();

        catalog.Register(MembershipActions.EnrollMember);

        registry.Register(
            MembershipActions.EnrollMember,
            ActivatorUtilities.CreateInstance<EnrollCourseMemberPolicy>(
                services));

        return services;
    }
}
