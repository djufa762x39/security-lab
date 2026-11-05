using Platform.LabVisibility.Domain;

namespace Platform.LabVisibility.Contracts;

// Sme da ga koristi samo EvaluatorKeyReader. Kontroleri ga nikad ne dobijaju.
public interface IEvaluatorKeyStore
{
    ValueTask<EvaluatorKey?> GetAsync(Guid labId, CancellationToken cancellationToken = default);

    ValueTask SaveAsync(EvaluatorKey key, CancellationToken cancellationToken = default);
}
