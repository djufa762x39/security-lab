using Microsoft.Extensions.DependencyInjection;
using Platform.Access.Infrastructure.Catalog;
using Platform.Access.Infrastructure.Policies;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;

namespace Platform.Api.Security.RevisionAuthorization;

public static class RevisionAuthorizationRegistration
{
    public static IServiceCollection AddRevisionAuthorization(
        this IServiceCollection services)
    {
        services.AddSingleton<IRevisionAuthorReader, RevisionAuthorReader>();

        services.AddSingleton<DenyRevisionPermissions>();

        services.AddSingleton<IRevisionApprovalPermission>(
            sp => sp.GetRequiredService<DenyRevisionPermissions>());

        services.AddSingleton<IRevisionActionPermission>(
            sp => sp.GetRequiredService<DenyRevisionPermissions>());

        services.AddSingleton<RevisionApprovalPolicy>();
        services.AddSingleton<RevisionActionPolicy>();

        return services;
    }

    public static void RegisterRevisionPolicies(this IServiceProvider services)
{
    var catalog = services.GetRequiredService<InMemoryActionCatalog>();
    var registry = services.GetRequiredService<InMemoryAccessPolicyRegistry>();

    catalog.Register(new ActionDefinition(
        RevisionActions.Submit,
        ActionScope.Course,
        "revision",
        false));

    registry.Register(
        RevisionActions.Submit,
        services.GetRequiredService<RevisionActionPolicy>());

    catalog.Register(new ActionDefinition(
        RevisionActions.Approve,
        ActionScope.Course,
        "revision",
        true));

    registry.Register(
        RevisionActions.Approve,
        services.GetRequiredService<RevisionApprovalPolicy>());

    catalog.Register(new ActionDefinition(
        RevisionActions.Return,
        ActionScope.Course,
        "revision",
        false));

    registry.Register(
        RevisionActions.Return,
        services.GetRequiredService<RevisionActionPolicy>());

    catalog.Register(new ActionDefinition(
        RevisionActions.UnlockAccount,
        ActionScope.System,
        null,
        true));

    registry.Register(
        RevisionActions.UnlockAccount,
        services.GetRequiredService<RevisionActionPolicy>());
}
}
