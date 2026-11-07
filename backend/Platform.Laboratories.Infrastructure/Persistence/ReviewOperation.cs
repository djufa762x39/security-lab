namespace Platform.Laboratories.Infrastructure.Persistence;

// Successful requests, review history and immutable publication snapshots share the revision transaction.
public sealed class ReviewOperation
{
    public Guid Id { get; init; }
    public Guid RevisionId { get; init; }
    public Guid ActorId { get; init; }
    public required string Key { get; init; }
    public required string Action { get; init; }
    public required string Hash { get; init; }
    public long Version { get; init; }
    public string? Comment { get; init; }
    public DateTimeOffset At { get; init; }
    public Guid? LaboratoryId { get; init; }
    public int? Number { get; init; }
    public byte[]? Snapshot { get; init; }
}
