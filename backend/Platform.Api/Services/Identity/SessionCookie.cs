namespace Platform.Api.Services.Identity;

/// <summary>Session cookie settings, shared by login (sets it), logout (US-05) and session check (US-06).</summary>
public static class SessionCookie
{
    public const string Name = "SecurityLab.Session";

    public static CookieOptions Options(DateTimeOffset expiresAt) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Expires = expiresAt,
        IsEssential = true
    };
}
