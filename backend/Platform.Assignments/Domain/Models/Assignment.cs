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
        bool allowStartAfterDeadline = false,
        AssignmentStatus status = AssignmentStatus.Active)
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

        Id = id;
        CourseId = courseId;
        PublishedLabRevisionId = publishedLabRevisionId;
        AvailableFromUtc = availableFromUtc;
        DueAtUtc = dueAtUtc;
        MaxAttempts = maxAttempts;
        AllowStartAfterDeadline = allowStartAfterDeadline;
        Status = status;
    }

    public Guid Id { get; }

    public string CourseId { get; }

    public string PublishedLabRevisionId { get; }

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
