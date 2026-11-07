namespace Platform.Laboratories.Infrastructure.Persistence;

public sealed record ReviewDefinition(Guid RevisionId, Guid LaboratoryId, byte[] Content, DateTimeOffset CreatedAt);
