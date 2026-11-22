using Platform.LabLocalization.Application;

namespace Platform.Api.Modules.LabLocalization.Infrastructure;

// Odbija svaku radnju dok se ne registruje provera clanstva na kursu.
public sealed class DenyAllLocalizationAccess : ILocalizationAccess
{
    public Task<bool> IsAllowedAsync(Guid actorId, LocalizationAction action, Guid resourceId, CancellationToken ct = default) =>
        Task.FromResult(false);
}
