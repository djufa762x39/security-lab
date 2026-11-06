using Platform.AssignmentOverview.Contracts;
using Platform.AssignmentOverview.Domain;
using Platform.Assignments.Contracts;
using Platform.Assignments.Domain.Models;

namespace Platform.AssignmentOverview.Application;

// ASN-02 TIM 16
// Kriterijum prihvatanja: "Student vidi samo aktivne dodele svojih kurseva."
// KT1 verzija: samo dodele sa kurseva na koje je student aktivno upisan.
// Sledece (Sprint 2): bez arhiviranih, isteklih, otkazanih i duplikata.
public sealed class MyAssignmentsService : IMyAssignmentsService
{
    private readonly IStudentCourseReader _courses;
    private readonly IAssignmentRepository _assignments;

    public MyAssignmentsService(
        IStudentCourseReader courses,
        IAssignmentRepository assignments)
    {
        _courses = courses;
        _assignments = assignments;
    }

    public async ValueTask<IReadOnlyList<StudentAssignmentView>> GetForStudentAsync(
        string studentId,
        CancellationToken cancellationToken = default)
    {
        // 1. Prazan ID studenta se odbija.
        if (string.IsNullOrWhiteSpace(studentId))
        {
            throw new ArgumentException("Student ID is required.", nameof(studentId));
        }

        // 2. Kursevi na kojima je student aktivno upisan.
        IReadOnlyCollection<string> courseIds =
            await _courses.GetActiveCourseIdsAsync(studentId, cancellationToken);

        // 3. Nije upisan nigde: prazna lista.
        if (courseIds.Count == 0)
        {
            return Array.Empty<StudentAssignmentView>();
        }

        var allowedCourses = new HashSet<string>(courseIds);

        // 4. Dodele za te kurseve (modul tima 15).
        IReadOnlyList<Assignment> assignments =
            await _assignments.GetForCoursesAsync(allowedCourses, cancellationToken);

        var result = new List<StudentAssignmentView>();

        foreach (Assignment assignment in assignments)
        {
            // 5. Odbrana u dubinu: dodela sa tudjeg kursa se odbacuje.
            if (!allowedCourses.Contains(assignment.CourseId))
            {
                continue;
            }

            // 6. Samo polja koja student sme da vidi.
            result.Add(new StudentAssignmentView(
                assignment.Id,
                assignment.CourseId,
                assignment.PublishedLabRevisionId,
                assignment.AvailableFromUtc,
                assignment.DueAtUtc,
                assignment.MaxAttempts));
        }

        return result;
    }
}