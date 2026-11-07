using Platform.Assignments.Contracts;
using Platform.Assignments.Domain.Models;

namespace Platform.AssignmentOverview.Infrastructure;

// Privremena zamena za repozitorijum dodela (ASN-04). Samo za Development.
public sealed class DevelopmentAssignmentRepository : IAssignmentRepository
{
    private readonly object _lock = new();
    private readonly List<Assignment> _items = [.. DevelopmentAssignmentData.CreateAssignments()];

    public ValueTask<Assignment?> GetByIdAsync(
        Guid assignmentId,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return ValueTask.FromResult(_items.FirstOrDefault(a => a.Id == assignmentId));
        }
    }

    public ValueTask<IReadOnlyList<Assignment>> GetForCoursesAsync(
        IReadOnlyCollection<string> courseIds,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            IReadOnlyList<Assignment> result =
                _items.Where(a => courseIds.Contains(a.CourseId)).ToList();
            return ValueTask.FromResult(result);
        }
    }

    public ValueTask AddAsync(Assignment assignment, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _items.Add(assignment);
        }

        return ValueTask.CompletedTask;
    }

    // Dodele su reference, pa je izmena vec vidljiva u listi.
    public ValueTask UpdateAsync(Assignment assignment, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;
}
