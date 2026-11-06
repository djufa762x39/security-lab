using Microsoft.Extensions.DependencyInjection;
using Platform.Audit.Contracts;
using Platform.Audit.Correlation;

namespace Platform.Audit.Composition;

public static class AuditServiceCollectionExtensions
{
    public static IServiceCollection AddAudit(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<CorrelationContext>();
        services.AddScoped<ICorrelationContext>(provider => provider.GetRequiredService<CorrelationContext>());

        return services;
    }
}
