using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Platform.Access.AspNet;
using Team21.Acc.Api.Extensions;
using Team21.Acc.Core.Courses;
namespace Team21.Acc.Api.Courses;

/// <summary>
/// ACC-01 endpoints. Creating a course without the right answers 403, archiving one answers 404.
/// </summary>
public static class CourseEndpoints
{
    public static IEndpointRouteBuilder MapCourseEndpoints(
        this IEndpointRouteBuilder app)
    {
        // Access is decided in the service, ACC-05 only has to let the call reach it
        var group = app.MapGroup("/api/courses")
            .DelegatesToLegacyAuthorization();

         group.MapGet("", GetCourses)
            .WithName("GetCourses");

        group.MapGet("/{id:guid}", GetCourse)
            .WithName("GetCourse");

        group.MapPost("", CreateCourse)
            .WithName("CreateCourse");

        group.MapPost("/{id:guid}/archive", ArchiveCourse)
            .WithName("ArchiveCourse");

        return app;
    }

    private static async Task<IResult> CreateCourse(
    CreateCourseRequest request,
    ICourseService courseService,
    CancellationToken cancellationToken)
    {
        var result = await courseService.CreateAsync(
            request,
            cancellationToken);

        return result.ToCreatedHttpResult(
            course => $"/api/courses/{course.Id}");
    }

    private static async Task<IResult> ArchiveCourse(
        Guid id,
        ICourseService courseService,
        CancellationToken cancellationToken)
    {
        var result = await courseService.ArchiveAsync(
            id,
            cancellationToken);

        return result.ToHttpResult();
    }


private static async Task<IResult> GetCourses(
    ICourseService courseService,
    CancellationToken cancellationToken)
{
    var result = await courseService.GetCoursesAsync(
        cancellationToken);

    return result.ToHttpResult();
}

private static async Task<IResult> GetCourse(
    Guid id,
    ICourseService courseService,
    CancellationToken cancellationToken)
{
    var result = await courseService.GetCourseAsync(
        id,
        cancellationToken);

    return result.ToHttpResult();
}

}