using Platform.LabLocalization.Application;

namespace Platform.Api.Modules.LabLocalization.Infrastructure;

// Nijedna revizija nije dostupna dok se ne registruje adapter nad objavljenim revizijama.
public sealed class UnavailableLabRevisionReader : ILabRevisionReader
{
    public Task<RevisionInfo?> FindCurrentForLabAsync(Guid labId, CancellationToken ct = default) =>
        Task.FromResult<RevisionInfo?>(null);

    public Task<RevisionInfo?> FindForLabAsync(Guid labId, Guid revisionId, CancellationToken ct = default) =>
        Task.FromResult<RevisionInfo?>(null);
}
