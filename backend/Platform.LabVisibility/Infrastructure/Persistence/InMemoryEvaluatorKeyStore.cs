using System.Collections.Concurrent;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Domain;

namespace Platform.LabVisibility.Infrastructure.Persistence;

public sealed class InMemoryEvaluatorKeyStore : IEvaluatorKeyStore
{
    private readonly ConcurrentDictionary<Guid, EvaluatorKey> keys = new();

    public ValueTask<EvaluatorKey?> GetAsync(Guid labId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(keys.TryGetValue(labId, out var key) ? key : null);

    public ValueTask SaveAsync(EvaluatorKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        keys[key.LabId] = key;
        return ValueTask.CompletedTask;
    }
}
