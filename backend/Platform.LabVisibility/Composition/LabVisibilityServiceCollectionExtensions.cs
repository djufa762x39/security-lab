using Microsoft.Extensions.DependencyInjection;
using Platform.Access.Infrastructure.Catalog;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Infrastructure.Auditing;
using Platform.LabVisibility.Infrastructure.Membership;
using Platform.LabVisibility.Infrastructure.Persistence;

namespace Platform.LabVisibility.Composition;

public static class LabVisibilityServiceCollectionExtensions
{
    // Poziva se posle AddAccessControl(). Svako dodaje svoju liniju ovde.
    public static IServiceCollection AddLabVisibility(this IServiceCollection services)
    {
        services.AddSingleton<InMemoryLabPublicContentReader>();
        services.AddSingleton<ILabPublicContentReader>(sp => sp.GetRequiredService<InMemoryLabPublicContentReader>());

        services.AddSingleton<InMemoryEvaluatorKeyStore>();
        services.AddSingleton<IEvaluatorKeyStore>(sp => sp.GetRequiredService<InMemoryEvaluatorKeyStore>());

        services.AddSingleton<InMemoryCourseMembership>();
        services.AddSingleton<ICourseMembership>(sp => sp.GetRequiredService<InMemoryCourseMembership>());

        services.AddSingleton<ILabVisibilityAudit, NullLabVisibilityAudit>();

        return services;
    }

    // Poziva se jednom, posle builder.Build(). Radnje bez politike tim 3 odbija (NoPolicy).
    public static IServiceProvider RegisterLabVisibilityAccess(this IServiceProvider services)
    {
        var catalog = services.GetRequiredService<InMemoryActionCatalog>();
        foreach (var action in LabVisibilityActions.All)
        {
            catalog.Register(action);
        }

        // Politike se dodaju ovde, svako svoju:
        // var policies = services.GetRequiredService<InMemoryAccessPolicyRegistry>();
        // policies.Register(LabVisibilityActions.StudentView, new StudentViewPolicy(...));

        return services;
    }
}
