using Platform.Api.Models.Identity;

namespace Platform.Api.Interfaces.Identity;

public interface ISessionManager
{
    Task<int> RevokeAllForUser(Guid userId, string reason, CancellationToken ct = default);

    Task<bool> RevokeSession(Guid ownerUserId, Guid sessionHandle, CancellationToken ct = default);

    Task<ValidateSession?> ValidateAsync(string token, CancellationToken ct = default);

    Task<IReadOnlyList<SessionInfo>> ListForUser(Guid userId, CancellationToken ct = default);
}
