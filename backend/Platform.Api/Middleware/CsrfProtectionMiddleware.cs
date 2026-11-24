using Microsoft.AspNetCore.Antiforgery;
using Platform.Access.AspNet;
using Platform.Api.Security.IdentityAccess;
using Platform.Api.Services.Identity;

namespace Platform.Api.Middleware;

/// <summary>
/// US-15 / SEC-03: a state-changing request (anything but GET, HEAD, OPTIONS, TRACE) must carry a valid CSRF token
/// when the browser sends it with our session cookie, and always on login (login CSRF).
/// Requests without the session cookie (for example the development <c>X-Test-User</c> header) are not checked:
/// a foreign site cannot add a header, and without the cookie there is no ambient credential to abuse.
/// Logout is the one documented exception: a stale token must never keep a session alive.
/// The token itself is made and validated by ASP.NET Core antiforgery (no custom cryptography).
/// </summary>
public sealed class CsrfProtectionMiddleware(RequestDelegate next, ILogger<CsrfProtectionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, IAntiforgery antiforgery)
    {
        if (!RequiresToken(context))
        {
            await next(context);
            return;
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            // AUD-03: never log the token or the cookie, only that the check failed.
            logger.LogInformation(
                "CSRF token missing or invalid for {Method} {Endpoint}. CorrelationId: {CorrelationId}",
                context.Request.Method,
                context.GetEndpoint()?.DisplayName ?? "unknown endpoint",
                context.TraceIdentifier);

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsJsonAsync(new { code = CsrfToken.InvalidCode });
            return;
        }

        await next(context);
    }

    public static bool RequiresToken(HttpContext context)
    {
        var method = context.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method)
            || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method))
            return false;

        var action = context.GetEndpoint()?.Metadata.GetMetadata<IAccessRequirementMetadata>()?.Action;

        if (action == IdentityAccessActions.Logout)
            return false;

        if (action == IdentityAccessActions.Login)
            return true;

        return context.Request.Cookies.ContainsKey(SessionCookie.Name);
    }
}
