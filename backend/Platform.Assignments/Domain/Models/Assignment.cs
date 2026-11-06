using Platform.Assignments.Domain.Enums;

namespace Platform.Assignments.Domain.Models;

// KT1 TIM 15
public sealed class Assignment
{
    public Assignment(
        Guid id,
        string courseId,
        string publishedLabRevisionId,
        DateTimeOffset? availableFromUtc,
        DateTimeOffset dueAtUtc,
        int maxAttempts,
        AssignmentScope scope,
        bool allowStartAfterDeadline = false,
        AssignmentStatus status = AssignmentStatus.Active,
        string? groupId = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Assignment ID is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(courseId))
        {
            throw new ArgumentException("Course ID is required.", nameof(courseId));
        }

        if (string.IsNullOrWhiteSpace(publishedLabRevisionId))
        {
            throw new ArgumentException("Published laboratory revision ID is required.", nameof(publishedLabRevisionId));
        }

        if (availableFromUtc.HasValue && availableFromUtc.Value > dueAtUtc)
        {
            throw new ArgumentException(
                "The availability start must not be after the due date.",
                nameof(availableFromUtc));
        }

        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxAttempts),
                "At least one attempt must be allowed.");
        }

        if (scope != AssignmentScope.Course &&
            scope != AssignmentScope.Group)
        {
            throw new ArgumentException(
                "Assignment scope must be Course or Group.",
                nameof(scope));
        }

        if (scope == AssignmentScope.Group &&
            string.IsNullOrWhiteSpace(groupId))
        {
            throw new ArgumentException(
                "Group ID is required for group assignments.",
                nameof(groupId));
        }

        if (scope == AssignmentScope.Course && groupId is not null)
        {
            throw new ArgumentException(
                "Group ID must be null for course assignments.",
                nameof(groupId));
        }

        Id = id;
        CourseId = courseId;
        GroupId = groupId;
        Scope = scope;
        PublishedLabRevisionId = publishedLabRevisionId;
        AvailableFromUtc = availableFromUtc;
        DueAtUtc = dueAtUtc;
        MaxAttempts = maxAttempts;
        AllowStartAfterDeadline = allowStartAfterDeadline;
        Status = status;

    }

    public Guid Id { get; }

    public string CourseId { get; }

    public string? GroupId { get; }

    public string PublishedLabRevisionId { get; }
    public AssignmentScope Scope { get; }

    public DateTimeOffset? AvailableFromUtc { get; }

    public DateTimeOffset DueAtUtc { get; }

    public int MaxAttempts { get; }

    public bool AllowStartAfterDeadline { get; }

    public AssignmentStatus Status { get; private set; }

    public void Archive() => Status = AssignmentStatus.Archived;

    public AssignmentAvailability GetAvailability(DateTimeOffset now)
    {
        if (Status == AssignmentStatus.Archived)
        {
            return AssignmentAvailability.Archived;
        }

        if (AvailableFromUtc.HasValue && now < AvailableFromUtc.Value)
        {
            return AssignmentAvailability.NotStarted;
        }

        if (!AllowStartAfterDeadline && now > DueAtUtc)
        {
            return AssignmentAvailability.Expired;
        }

        return AssignmentAvailability.Available;
    }
}
