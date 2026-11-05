namespace Platform.Audit.Contracts;

/// <summary>
/// What happened. It deliberately has no actor, time or correlation ID: the caller
/// cannot set them.
/// </summary>
/// <param name="EventType">A type from <see cref="AuditEventTypes"/>.</param>
/// <param name="Outcome">Must be allowed for the event type.</param>
/// <param name="CourseId">Required, forbidden or optional depending on the event type.</param>
/// <param name="Resource">The subject of the change; not the actor.</param>
/// <param name="Details">Only keys allowed for the event type.</param>
/// <param name="EventId">Idempotency key for producers that retry; generated when null.</param>
public sealed record AuditEventDraft(
    string EventType,
    AuditOutcome Outcome,
    Guid? CourseId = null,
    AuditResourceRef? Resource = null,
    IReadOnlyDictionary<string, string>? Details = null,
    Guid? EventId = null);
