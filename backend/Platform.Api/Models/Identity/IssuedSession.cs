namespace Platform.Api.Models.Identity;

/// <param name="Token">Opaque secret for the cookie. The store must keep only a hash of it.</param>
/// <param name="Handle">Public handle of the session (same one that appears in SessionInfo).</param>
public sealed record IssuedSession(string Token, Guid Handle, DateTimeOffset ExpiresAt);
