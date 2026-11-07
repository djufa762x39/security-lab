using Platform.AssignmentOverview.Application;
using Platform.AssignmentOverview.Contracts;
using Platform.AssignmentOverview.Infrastructure;
using Platform.Assignments.Contracts;

namespace Platform.Api.Modules.MyAssignments;

public static class MyAssignmentsModule
{
    /// <summary>
    /// ASN-02: servis za listu dodela i njegove zavisnosti. Dok moduli za upise i dodele nemaju
    /// implementaciju, Development koristi privremene podatke, a ostala okruzenja fail-closed varijante.
    /// </summary>
    public static IServiceCollection AddMyAssignments(
        this IServiceCollection services,
        IHostEnvironment environment)
    {
        services.AddScoped<IMyAssignmentsService, MyAssignmentsService>();

        if (environment.IsDevelopment())
        {
            services.AddSingleton<IStudentCourseReader, DevelopmentStudentCourseReader>();
            services.AddSingleton<IAssignmentRepository, DevelopmentAssignmentRepository>();
        }
        else
        {
            services.AddSingleton<IStudentCourseReader, UnavailableStudentCourseReader>();
            services.AddSingleton<IAssignmentRepository, UnavailableAssignmentRepository>();
        }

        return services;
    }
}
