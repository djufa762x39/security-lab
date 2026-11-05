using Platform.Api.Modules.Laboratories.Application;
using Microsoft.Extensions.Primitives;

namespace Platform.Api.Modules.Laboratories.Infrastructure;

public sealed class FakeIdentityContext : IIdentityContext
{
    public const string HeaderName = "X-Test-User";

    private static readonly Dictionary<string, PlatformRole> KnownRoles = new()
    {
        ["Student"] = PlatformRole.Student,
        ["Teacher"] = PlatformRole.Teacher,
        ["Assistant"] = PlatformRole.Assistant,
        ["Administrator"] = PlatformRole.Administrator
    };

    public FakeIdentityContext(IHttpContextAccessor httpContextAccessor)
    {
        IHeaderDictionary? headers = httpContextAccessor.HttpContext?.Request.Headers;

        if (headers is null || !headers.TryGetValue(HeaderName, out StringValues values) || values.Count != 1)
        {
            return;
        }

        string? raw = values[0];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

         string[] parts = raw.Split(':');
        if (parts.Length != 2)
        {
            return;
        }

        string userId = parts[0].Trim();
        string roleName = parts[1].Trim();

        if (!IsValidUserId(userId) || !KnownRoles.TryGetValue(roleName, out PlatformRole role))
        {
            return;
        }

        IsAuthenticated = true;
        UserId = userId;
        Role = role;
    }

    public bool IsAuthenticated { get; }

    public string? UserId { get; }

    public PlatformRole? Role { get; }

    private static bool IsValidUserId(string userId) =>
        userId.Length is >= 1 and <= 64
        && userId.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
}