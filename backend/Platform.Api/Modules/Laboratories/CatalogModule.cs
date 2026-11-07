
using Platform.Api.Modules.Laboratories.Api;
using Platform.Api.Modules.Laboratories.Application;

namespace Platform.Api.Modules.Laboratories;

public static class CatalogModule
{
    public static IServiceCollection AddCatalog(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(
            configuration.GetConnectionString("Authoring")))
        {
            services.AddScoped<CatalogService>();
        }

        return services;
    }

    public static IEndpointRouteBuilder MapCatalog(
        this IEndpointRouteBuilder app,
        IConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(
            configuration.GetConnectionString("Authoring")))
        {
            app.MapCatalogEndpoints();
        }

        return app;
    }
}
