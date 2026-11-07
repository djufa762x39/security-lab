using System.Collections.Concurrent;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Domain;

namespace Platform.LabVisibility.Infrastructure.Persistence;

// Za testove i razvoj dok ne stigne EF implementacija (Antonić).
public sealed class InMemoryLabPublicContentReader : ILabPublicContentReader
{
    private readonly ConcurrentDictionary<Guid, LabPublicContent> labs = new();

    public void Save(LabPublicContent content) => labs[content.LabId] = content;

    public ValueTask<LabPublicContent?> GetAsync(Guid labId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(labs.TryGetValue(labId, out var content) ? content : null);
}
