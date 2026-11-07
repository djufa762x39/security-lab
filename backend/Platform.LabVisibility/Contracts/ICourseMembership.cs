namespace Platform.LabVisibility.Contracts;

public enum CourseRole
{
    None,
    Student,
    Teacher
}

// Privremeni ugovor dok ACC timovi ne objave članstvo na kursu.
public interface ICourseMembership
{
    ValueTask<CourseRole> GetRoleAsync(Guid courseId, Guid userId, CancellationToken cancellationToken = default);
}
