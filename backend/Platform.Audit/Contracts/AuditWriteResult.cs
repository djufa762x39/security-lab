namespace Platform.Audit.Contracts;

/// <summary>
/// Anything other than <see cref="AuditWriteStatus.Recorded"/> is a failure for the caller.
/// </summary>
public sealed record AuditWriteResult(
    AuditWriteStatus Status,
    AuditRejectionCode RejectionCode,
    Guid? EventId)
{
    public bool IsRecorded => Status == AuditWriteStatus.Recorded;

    public static AuditWriteResult Recorded(Guid eventId) =>
        new(AuditWriteStatus.Recorded, AuditRejectionCode.None, eventId);

    public static AuditWriteResult Rejected(AuditRejectionCode code) =>
        new(AuditWriteStatus.Rejected, code, null);

    public static AuditWriteResult Unavailable() =>
        new(AuditWriteStatus.Unavailable, AuditRejectionCode.None, null);
}

public enum AuditWriteStatus
{
    /// <summary>The event is stored.</summary>
    Recorded,

    /// <summary>The draft breaks the contract; nothing is stored.</summary>
    Rejected,

    /// <summary>Storage failed; nothing is guaranteed to be stored.</summary>
    Unavailable
}

public enum AuditRejectionCode
{
    None,
    UnknownEventType,
    OutcomeNotAllowed,
    CourseRequired,
    CourseNotAllowed,
    InvalidResource,
    InvalidContext,
    UnknownDetailKey,
    ForbiddenDetailKey,
    InvalidDetailValue,
    DetailsTooLarge,
    EventIdConflict
}
