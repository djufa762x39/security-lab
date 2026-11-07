using Microsoft.AspNetCore.Mvc;
using Platform.Access.AspNet;
using Platform.Api.Interfaces.Identity;
using Platform.AssignmentOverview.Contracts;

namespace Platform.Api.Endpoints;

public sealed record MyAssignmentResponse(
    Guid AssignmentId,
    string CourseId,
    string PublishedLabRevisionId,
    DateTimeOffset? AvailableFromUtc,
    DateTimeOffset DueAtUtc,
    int MaxAttempts);

public sealed record MyAssignmentsResponse(
    IReadOnlyList<MyAssignmentResponse> Assignments,
    string CorrelationId);

public sealed record MyAssignmentsErrorResponse(string Error, string CorrelationId);

/// <summary>
/// ASN-02: dodele prijavljenog studenta. Student se uzima samo iz sesije, nikad iz zahteva.
/// </summary>
public static class MyAssignmentsEndpoints
{
    public const string Route = "/api/me/assignments";

    public static IEndpointRouteBuilder MapMyAssignmentsEndpoints(this IEndpointRouteBuilder app)
    {
        // Bez AllowAnonymous: SessionCheckMiddleware mora da upise sesiju, inace IIdentityContext ne vidi pozivaoca.
        app.MapGet(Route, GetAsync)
            .DelegatesToLegacyAuthorization();
        return app;
    }

    private static async Task<IResult> GetAsync(
        HttpContext context,
        IIdentityContext identity,
        [FromServices] IMyAssignmentsService service,
        CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        context.Response.Headers.CacheControl = "no-store";

        var caller = identity.Current;
        if (caller is null || !caller.IsAuthenticated || caller.UserId is null)
        {
            return Results.Json(
                new MyAssignmentsErrorResponse("unauthenticated", correlationId),
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var assignments = await service.GetForStudentAsync(
            caller.UserId.Value.ToString(),
            cancellationToken);

        return Results.Ok(new MyAssignmentsResponse(
            assignments
                .Select(a => new MyAssignmentResponse(
                    a.AssignmentId,
                    a.CourseId,
                    a.PublishedLabRevisionId,
                    a.AvailableFromUtc,
                    a.DueAtUtc,
                    a.MaxAttempts))
                .ToList(),
            correlationId));
    }
}
