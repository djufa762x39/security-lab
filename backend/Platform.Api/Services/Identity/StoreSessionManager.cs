using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;

namespace Platform.Api.Services.Identity;

/// Stub dok IDN-02 ne napravi pravi opoziv sesija.
/// Proverava sesiju u bazi (US-06). Opoziv jos ne postoji.
public sealed class StoreSessionManager : ISessionManager
{
    public Task<int> RevokeAllForUser(Guid userId, string reason, CancellationToken ct = default) => Task.FromResult(0);
    public Task<bool> RevokeSession(Guid ownerUserId, Guid sessionHandle, CancellationToken ct = default) => Task.FromResult(false);
    public Task<IReadOnlyList<SessionInfo>> ListForUser(Guid userId, CancellationToken ct = default) => Task.FromResult((IReadOnlyList<SessionInfo>)Array.Empty<SessionInfo>());
    private readonly ISessionStore _sessionStore = null!;
    public StoreSessionManager(ISessionStore sessionStore)
    {
        _sessionStore = sessionStore;
    }

    private static AuthenticationLevel MapLevel(AuthLevel level) => level switch
    {
        AuthLevel.Active => AuthenticationLevel.Active,
        AuthLevel.MfaPending => AuthenticationLevel.MfaPending,
        _ => AuthenticationLevel.None
    };

    public async Task<ValidateSession?> ValidateAsync(string token, CancellationToken ct = default)
    {
        var session = await _sessionStore.FindByTokenAsync(token, ct);

        if (session == null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;

        if (session.RevokedAt != null || session.ExpiresAt <= now)
        {
            return null;
        }

        await _sessionStore.TouchAsync(session, now, ct);

        ValidateSession validate = new ValidateSession(session.SessionHandle, session.UserId, MapLevel(session.AuthLevel));

        return validate;
    }
}
