using System.Collections.Concurrent;
using Platform.LabVisibility.Contracts;

namespace Platform.LabVisibility.Infrastructure.Membership;

// DEV STUB: zameniti ugovorom ACC tima kad ga objave.
public sealed class InMemoryCourseMembership : ICourseMembership
{
    private readonly ConcurrentDictionary<(Guid CourseId, Guid UserId), CourseRole> roles = new();

    public void Set(Guid courseId, Guid userId, CourseRole role) => roles[(courseId, userId)] = role;

    public ValueTask<CourseRole> GetRoleAsync(Guid courseId, Guid userId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(roles.TryGetValue((courseId, userId), out var role) ? role : CourseRole.None);
}
