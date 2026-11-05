using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Platform.Audit.Correlation;

internal sealed class CorrelationMiddleware(RequestDelegate next, ILogger<CorrelationMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-ID";
    public const string LogScopeKey = "CorrelationId";

    public async Task InvokeAsync(HttpContext http, CorrelationContext context)
    {
        var correlationId = CorrelationContext.NewId();

        context.Assign(correlationId);
        http.TraceIdentifier = correlationId;
        http.Response.OnStarting(() =>
        {
            http.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object> { [LogScopeKey] = correlationId }))
        {
            await next(http);
        }
    }
}
