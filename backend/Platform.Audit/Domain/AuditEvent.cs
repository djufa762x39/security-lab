using System.Collections.Frozen;
using Platform.Audit.Contracts;

namespace Platform.Audit.Domain;

/// <summary>
/// A stored audit record (AUD-01). Immutable once created: the module only appends new
/// records and never changes or deletes one (ADM-07). <see cref="Application.AuditDraftValidator"/>
/// checks the catalog rules first; this type guards the facts every record must satisfy,
/// whoever creates it. Exception messages never contain the rejected values.
/// </summary>
internal sealed class AuditEvent
{
    private AuditEvent(
        Guid id,
        DateTimeOffset occurredAt,
        AuditActor actor,
        AuditEventDraft draft,
        string correlationId,
        int schemaVersion)
    {
        Id = id;
        OccurredAt = occurredAt;
        ActorKind = actor.Kind;
        ActorId = actor.Id;
        EventType = draft.EventType;
        Outcome = draft.Outcome;
        CourseId = draft.CourseId;
        ResourceType = draft.Resource?.Type;
        ResourceId = draft.Resource?.Id;
        CorrelationId = correlationId;
        SchemaVersion = schemaVersion;

        // Own copy: a later change to the caller's dictionary must not change the record.
        Details = draft.Details?.ToFrozenDictionary(StringComparer.Ordinal)
            ?? FrozenDictionary<string, string>.Empty;
    }

    public Guid Id { get; }

    /// <summary>Server time in UTC (OPS-09).</summary>
    public DateTimeOffset OccurredAt { get; }

    public AuditActorKind ActorKind { get; }

    public Guid? ActorId { get; }

    public string EventType { get; }

    public AuditOutcome Outcome { get; }

    public Guid? CourseId { get; }

    public string? ResourceType { get; }

    public string? ResourceId { get; }

    public string CorrelationId { get; }

    public IReadOnlyDictionary<string, string> Details { get; }

    /// <summary>Catalog schema version the event was validated against.</summary>
    public int SchemaVersion { get; }

    public static AuditEvent Create(
        Guid id,
        DateTimeOffset occurredAt,
        AuditEventDraft draft,
        AuditContext context,
        int schemaVersion = AuditEventCatalog.SchemaVersion)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Actor);

        if (id == Guid.Empty)
            throw new ArgumentException("An audit event needs a non-empty id.", nameof(id));

        if (occurredAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("An audit event time must be in UTC.", nameof(occurredAt));

        if (string.IsNullOrWhiteSpace(draft.EventType))
            throw new ArgumentException("An audit event needs an event type.", nameof(draft));

        if (!Enum.IsDefined(draft.Outcome))
            throw new ArgumentException("An audit event needs a defined outcome.", nameof(draft));

        if (draft.CourseId == Guid.Empty)
            throw new ArgumentException("An empty GUID is not a valid course.", nameof(draft));

        if (!HasValidActor(context.Actor))
            throw new ArgumentException("The actor kind and id do not match.", nameof(context));

        if (string.IsNullOrWhiteSpace(context.CorrelationId))
            throw new ArgumentException("An audit event needs a correlation id.", nameof(context));

        if (schemaVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), "Schema versions start at 1.");

        return new AuditEvent(id, occurredAt, context.Actor, draft, context.CorrelationId, schemaVersion);
    }

    /// <summary>
    /// True when <paramref name="other"/> describes the same event. The time is ignored:
    /// the server assigns it when the event is written, so a retry of the same event gets a
    /// new time. Used to tell a harmless retry from a conflicting reuse of an id.
    /// </summary>
    public bool HasSameContentAs(AuditEvent other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return Id == other.Id
            && ActorKind == other.ActorKind
            && ActorId == other.ActorId
            && string.Equals(EventType, other.EventType, StringComparison.Ordinal)
            && Outcome == other.Outcome
            && CourseId == other.CourseId
            && string.Equals(ResourceType, other.ResourceType, StringComparison.Ordinal)
            && string.Equals(ResourceId, other.ResourceId, StringComparison.Ordinal)
            && string.Equals(CorrelationId, other.CorrelationId, StringComparison.Ordinal)
            && SchemaVersion == other.SchemaVersion
            && HaveSameDetails(Details, other.Details);
    }

    private static bool HasValidActor(AuditActor actor) => actor.Kind switch
    {
        AuditActorKind.Anonymous => actor.Id is null,
        AuditActorKind.User or AuditActorKind.Service => actor.Id is Guid id && id != Guid.Empty,
        _ => false
    };

    private static bool HaveSameDetails(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right) =>
        left.Count == right.Count
        && left.All(pair => right.TryGetValue(pair.Key, out var value)
            && string.Equals(pair.Value, value, StringComparison.Ordinal));
}
