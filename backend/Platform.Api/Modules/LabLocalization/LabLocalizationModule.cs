using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Access.Composition;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Modules.LabLocalization.Infrastructure;
using Platform.LabLocalization.Application;
using Platform.LabLocalization.Infrastructure;

namespace Platform.Api.Modules.LabLocalization;

public static class LabLocalizationModule
{
    public static IServiceCollection AddLabLocalizationModule(this IServiceCollection services)
    {
        services.AddAccessAction<LabLocalizationEntryPolicy>(
            new ActionDefinition(
                LabLocalizationActions.Entry,
                ActionScope.Authenticated,
                null,
                false));

        // Prevodi se gube pri restartu dok modul ne dobije PostgreSQL skladiste.
        services.TryAddSingleton<ILabLocalizationRepository, InMemoryLabLocalizationRepository>();
        services.TryAddSingleton<ILabRevisionReader, UnavailableLabRevisionReader>();
        services.TryAddSingleton<ILocalizationAccess, DenyAllLocalizationAccess>();

        services.AddScoped<GetLabContent>();
        services.AddScoped<AddLabLocalization>();

        return services;
    }

    public static IEndpointRouteBuilder MapLabLocalizationModule(this IEndpointRouteBuilder app)
    {
        app.MapLabLocalizationEndpoints();
        return app;
    }
}
