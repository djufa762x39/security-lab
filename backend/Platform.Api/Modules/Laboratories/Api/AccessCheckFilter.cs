using Platform.Api.Modules.Laboratories.Application;

namespace Platform.Api.Modules.Laboratories.Api;

// zahtev -> ko je? (IIdentityContext) -> sme li? (IAccessDecision) -> dozvoli / odbij
public static class AccessCheckFilter
{
    public static RouteHandlerBuilder RequireLabAccess(this RouteHandlerBuilder builder, string action)
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            HttpContext http = context.HttpContext;
            IIdentityContext identity = http.RequestServices.GetRequiredService<IIdentityContext>();
            IAccessDecision accessDecision = http.RequestServices.GetRequiredService<IAccessDecision>();

            AccessResult result = accessDecision.Decide(identity, action);

            // Dozvoljeno
            if (result.Allowed)
            {
                return await next(context);
            }

            // Odbijeno -> zapisi u log i vrati gresku
            ILogger logger = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Laboratories.Access");
            logger.LogWarning("Pristup odbijen: radnja={Action} korisnik={UserId} razlog={Reason}",
                action, identity.UserId ?? "(nepoznat)", result.Reason);

            if (result.Reason == DenyReason.Unauthenticated)
            {
                return Results.Problem(statusCode: 401, title: "Unauthenticated");
            }

            return Results.Problem(statusCode: 403, title: "Access denied");
        });
    }
}