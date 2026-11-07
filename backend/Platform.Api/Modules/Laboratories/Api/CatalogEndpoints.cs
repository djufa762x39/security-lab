
using AccountState = Platform.Access.Domain.Enums.AccountState;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Modules.Laboratories.Application;
using Platform.LabVisibility.Contracts;

namespace Platform.Api.Modules.Laboratories.Api;

// LAB-02: API kataloga.
// Zasebne rute, bez izmene postojecih LAB-01 endpointa.
public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/catalog")
            .WithTags("Laboratory catalog");

        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", DetailAsync);

        return app;
    }

    private static async Task<IResult> ListAsync(
        string? search,
        string? difficulty,
        string? type,
        string? tag,
        HttpContext http,
        IRequestSubjectResolver subjects,
        CatalogService catalog,
        ILabPublicContentReader contentReader,
        ICourseMembership membership,
        CancellationToken ct)
    {
        var subject = subjects.Resolve(http);

        if (subject is not { State: AccountState.Active } ||
            !Guid.TryParse(subject.Id, out var userId) ||
            userId == Guid.Empty)
        {
            return Results.Unauthorized();
        }

        var filter = new CatalogLaboratoryFilter(
            search, difficulty, type, tag);

        var laboratories = await catalog.GetCatalogAsync(filter, ct);
        var visible = new List<CatalogLaboratoryResponse>();

        foreach (var lab in laboratories)
        {
            var content = await contentReader.GetAsync(lab.Id, ct);

            if (content is null)
                continue;

            var role = await membership.GetRoleAsync(
                content.CourseId, userId, ct);

            if (CatalogVisibilityPolicy.CanView(content, role))
                visible.Add(lab);
        }

        return Results.Ok(visible);
    }

    private static async Task<IResult> DetailAsync(
        Guid id,
        HttpContext http,
        IRequestSubjectResolver subjects,
        CatalogService catalog,
        ILabPublicContentReader contentReader,
        ICourseMembership membership,
        CancellationToken ct)
    {
        var subject = subjects.Resolve(http);

        if (subject is not { State: AccountState.Active } ||
            !Guid.TryParse(subject.Id, out var userId) ||
            userId == Guid.Empty)
        {
            return Results.Unauthorized();
        }

        var content = await contentReader.GetAsync(id, ct);

        if (content is null)
            return Results.NotFound();

        var role = await membership.GetRoleAsync(
            content.CourseId, userId, ct);

        if (!CatalogVisibilityPolicy.CanView(content, role))
            return Results.NotFound();

        var laboratories = await catalog.GetCatalogAsync(
            new CatalogLaboratoryFilter(null, null, null, null),
            ct);

        var lab = laboratories.FirstOrDefault(x => x.Id == id);

        return lab is null
            ? Results.NotFound()
            : Results.Ok(lab);
    }
}
