using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Platform.Access.AspNet;
using Platform.Api.Interfaces.Identity;
using Platform.Competencies.Contracts;
using Platform.Competencies.Domain;

namespace Platform.Api.Endpoints;

public sealed record CompetencyResponse(Guid Id, string Name, string? Description);

public sealed record CompetencyCatalogResponse(IReadOnlyList<CompetencyResponse> Competencies);

public sealed record LaboratoryCompetenciesResponse(
    Guid LaboratoryId,
    IReadOnlyList<CompetencyResponse> Competencies);

public sealed record CompetencyErrorResponse(string Error);

/// <summary>
/// PRG-04 HTTP boundary. This increment proves only that a caller has an active session identity.
/// It intentionally does not ask another module whether a laboratory exists or whether
/// the caller has a right over that laboratory; those checks are a later PRG-04 increment.
/// </summary>
public static class CompetencyEndpoints
{
    public const string CatalogPath = "/api/competencies";
    public const string LaboratoryCompetenciesPath = "/api/laboratories/{laboratoryId:guid}/competencies";

    public static IEndpointRouteBuilder MapCompetencyEndpoints(this IEndpointRouteBuilder app)
    {
        // SessionCheckMiddleware verifies the session before these handlers execute. The
        // handlers verify the resolved identity again. Delegated metadata documents that
        // this is the endpoint's reviewed authorization boundary for this increment.
        Configure(app.MapGet(CatalogPath, ListCatalogAsync));
        Configure(app.MapGet(LaboratoryCompetenciesPath, ListLaboratoryCompetenciesAsync));
        Configure(app.MapPut($"{LaboratoryCompetenciesPath}/{{competencyId:guid}}", AddLaboratoryCompetencyAsync));
        Configure(app.MapDelete($"{LaboratoryCompetenciesPath}/{{competencyId:guid}}", RemoveLaboratoryCompetencyAsync));
        return app;
    }

    public static async Task<IResult> ListCatalogAsync(
        HttpContext context,
        IIdentityContext identity,
        CancellationToken cancellationToken)
    {
        if (!HasAuthenticatedIdentity(identity))
            return Error(StatusCodes.Status401Unauthorized, "unauthenticated");

        if (context.RequestServices.GetService<ICompetencyCatalog>() is not { } catalog)
            return Error(StatusCodes.Status503ServiceUnavailable, "competency_storage_unavailable");

        var competencies = await catalog.ListAsync(cancellationToken);
        return Results.Ok(new CompetencyCatalogResponse(competencies.Select(ToResponse).ToList()));
    }

    public static async Task<IResult> ListLaboratoryCompetenciesAsync(
        Guid laboratoryId,
        HttpContext context,
        IIdentityContext identity,
        CancellationToken cancellationToken)
    {
        if (!HasAuthenticatedIdentity(identity))
            return Error(StatusCodes.Status401Unauthorized, "unauthenticated");
        if (laboratoryId == Guid.Empty)
            return Error(StatusCodes.Status400BadRequest, "invalid_laboratory_id");

        if (context.RequestServices.GetService<ILaboratoryCompetencyStore>() is not { } store)
            return Error(StatusCodes.Status503ServiceUnavailable, "competency_storage_unavailable");

        // Deliberately no laboratory lookup or resource-right check in this increment.
        var competencies = await store.GetForLaboratoryAsync(laboratoryId, cancellationToken);
        return Results.Ok(new LaboratoryCompetenciesResponse(
            laboratoryId,
            competencies.Select(ToResponse).ToList()));
    }

    public static async Task<IResult> AddLaboratoryCompetencyAsync(
        Guid laboratoryId,
        Guid competencyId,
        HttpContext context,
        IIdentityContext identity,
        CancellationToken cancellationToken)
    {
        if (!HasAuthenticatedIdentity(identity))
            return Error(StatusCodes.Status401Unauthorized, "unauthenticated");
        if (laboratoryId == Guid.Empty)
            return Error(StatusCodes.Status400BadRequest, "invalid_laboratory_id");
        if (competencyId == Guid.Empty)
            return Error(StatusCodes.Status400BadRequest, "invalid_competency_id");

        if (context.RequestServices.GetService<ILaboratoryCompetencyStore>() is not { } store)
            return Error(StatusCodes.Status503ServiceUnavailable, "competency_storage_unavailable");

        try
        {
            // Deliberately no laboratory lookup or resource-right check in this increment.
            bool created = await store.AddAsync(laboratoryId, competencyId, cancellationToken);
            return created
                ? Results.Created(LinkPath(laboratoryId, competencyId), value: null)
                : Results.NoContent();
        }
        catch (KeyNotFoundException)
        {
            return Error(StatusCodes.Status404NotFound, "competency_not_found");
        }
    }

    public static async Task<IResult> RemoveLaboratoryCompetencyAsync(
        Guid laboratoryId,
        Guid competencyId,
        HttpContext context,
        IIdentityContext identity,
        CancellationToken cancellationToken)
    {
        if (!HasAuthenticatedIdentity(identity))
            return Error(StatusCodes.Status401Unauthorized, "unauthenticated");
        if (laboratoryId == Guid.Empty)
            return Error(StatusCodes.Status400BadRequest, "invalid_laboratory_id");
        if (competencyId == Guid.Empty)
            return Error(StatusCodes.Status400BadRequest, "invalid_competency_id");

        if (context.RequestServices.GetService<ILaboratoryCompetencyStore>() is not { } store)
            return Error(StatusCodes.Status503ServiceUnavailable, "competency_storage_unavailable");

        // DELETE is idempotent: a missing relationship already has the requested state.
        _ = await store.RemoveAsync(laboratoryId, competencyId, cancellationToken);
        return Results.NoContent();
    }

    private static RouteHandlerBuilder Configure(RouteHandlerBuilder endpoint) => endpoint
        .WithTags("Competencies")
        .DelegatesToLegacyAuthorization();

    private static bool HasAuthenticatedIdentity(IIdentityContext identity) =>
        identity.Current is { IsAuthenticated: true, UserId: { } userId } && userId != Guid.Empty;

    private static IResult Error(int statusCode, string error) =>
        Results.Json(new CompetencyErrorResponse(error), statusCode: statusCode);

    private static string LinkPath(Guid laboratoryId, Guid competencyId) =>
        $"/api/laboratories/{laboratoryId:D}/competencies/{competencyId:D}";

    private static CompetencyResponse ToResponse(Competency competency) =>
        new(competency.Id, competency.Name, competency.Description);
}
