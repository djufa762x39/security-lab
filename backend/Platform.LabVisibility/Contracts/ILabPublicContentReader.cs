using Platform.LabVisibility.Domain;

namespace Platform.LabVisibility.Contracts;

public interface ILabPublicContentReader
{
    ValueTask<LabPublicContent?> GetAsync(Guid labId, CancellationToken cancellationToken = default);
}
