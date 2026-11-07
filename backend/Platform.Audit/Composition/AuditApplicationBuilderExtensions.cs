using Microsoft.AspNetCore.Builder;
using Platform.Audit.Correlation;

namespace Platform.Audit.Composition;

public static class AuditApplicationBuilderExtensions
{
    public static IApplicationBuilder UseCorrelationContext(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<CorrelationMiddleware>();
    }
}
