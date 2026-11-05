using Platform.Api.Modules.Laboratories.Application;

namespace Platform.Api.Modules.Laboratories.Infrastructure;

// Koristi se VAN Development rezima dok ne stigne IDN modul:
// niko nije prepoznat (fail closed).
public sealed class AnonymousIdentityContext : IIdentityContext
{
    public bool IsAuthenticated => false;

    public string? UserId => null;

    public PlatformRole? Role => null;
}