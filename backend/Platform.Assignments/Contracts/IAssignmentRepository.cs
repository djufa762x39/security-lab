using Platform.Assignments.Domain.Models;

namespace Platform.Assignments.Contracts;

// KT1 TIM 15
public interface IAssignmentRepository
{
    ValueTask<Assignment?> GetByIdAsync(
        Guid assignmentId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<Assignment>> GetForCoursesAsync(
        IReadOnlyCollection<string> courseIds,
        CancellationToken cancellationToken = default);

    ValueTask AddAsync(
        Assignment assignment,
        CancellationToken cancellationToken = default);

    ValueTask UpdateAsync(
        Assignment assignment,
        CancellationToken cancellationToken = default);
}
