using Platform.AssignmentOverview.Contracts;
using Platform.Assignments.Contracts;
using Platform.Assignments.Domain.Models;

namespace Platform.AssignmentOverview.Tests.Support;

// ASN-02 TIM 16
// Lazni pomocnici za testove, umesto pravih modula drugih timova.

// Glumi modul za clanstva (tim 10): vraca kurseve koje mu zadamo.
internal sealed class FakeStudentCourseReader : IStudentCourseReader
{
    private readonly Dictionary<string, string[]> _enrollments = new();

    public void Enroll(string studentId, params string[] courseIds)
    {
        _enrollments[studentId] = courseIds;
    }

    public ValueTask<IReadOnlyCollection<string>> GetActiveCourseIdsAsync(
        string studentId,
        CancellationToken cancellationToken = default)
    {
        if (_enrollments.TryGetValue(studentId, out string[]? courses))
        {
            return ValueTask.FromResult<IReadOnlyCollection<string>>(courses);
        }

        return ValueTask.FromResult<IReadOnlyCollection<string>>(Array.Empty<string>());
    }
}
// Glumi repozitorijum dodela (tim 15): vraca dodele koje mu zadamo.
internal sealed class FakeAssignmentRepository : IAssignmentRepository
{
    private readonly List<Assignment> _items = new();

    // Kad je true, vraca SVE dodele, i sa kurseva koji nisu trazeni.
    // Tako glumimo gresku u tudjem modulu i proveravamo odbranu u dubinu.
    public bool ReturnAllCourses { get; set; }

    public void Add(params Assignment[] assignments)
    {
        _items.AddRange(assignments);
    }

    public ValueTask<IReadOnlyList<Assignment>> GetForCoursesAsync(
        IReadOnlyCollection<string> courseIds,
        CancellationToken cancellationToken = default)
    {
        List<Assignment> result = ReturnAllCourses
            ? _items.ToList()
            : _items.Where(a => courseIds.Contains(a.CourseId)).ToList();

        return ValueTask.FromResult<IReadOnlyList<Assignment>>(result);
    }

    // Ostale metode interfejsa servis ne koristi, ali moraju postojati.
    public ValueTask<Assignment?> GetByIdAsync(
        Guid assignmentId, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(_items.FirstOrDefault(a => a.Id == assignmentId));

    public ValueTask AddAsync(
        Assignment assignment, CancellationToken cancellationToken = default)
    {
        _items.Add(assignment);
        return ValueTask.CompletedTask;
    }

    public ValueTask UpdateAsync(
        Assignment assignment, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;
}