using Platform.Api.Modules.Laboratories.Application;

namespace Platform.Api.Modules.Laboratories.Api;

public static class LaboratoriesEndpoints
{
    public static IEndpointRouteBuilder MapLaboratoriesEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/laboratories");

        group.MapGet("/", () => Results.Ok(Array.Empty<object>()))
            .RequireLabAccess(LabActions.List);

        group.MapPost("/", () => Results.StatusCode(501))
            .RequireLabAccess(LabActions.Create);

        return app;
    }
}