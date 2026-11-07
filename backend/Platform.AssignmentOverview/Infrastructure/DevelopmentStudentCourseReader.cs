using Platform.AssignmentOverview.Contracts;

namespace Platform.AssignmentOverview.Infrastructure;

// Privremena zamena za modul upisa (ACC-02). Samo za Development.
public sealed class DevelopmentStudentCourseReader : IStudentCourseReader
{
    public ValueTask<IReadOnlyCollection<string>> GetActiveCourseIdsAsync(
        string studentId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<string> courses =
            DevelopmentAssignmentData.Enrollments.TryGetValue(studentId, out var ids)
                ? ids
                : Array.Empty<string>();

        return ValueTask.FromResult(courses);
    }
}
