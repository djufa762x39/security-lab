using Platform.Api.Modules.Laboratories.Application;

namespace Platform.Api.Modules.Laboratories.Infrastructure;

// Koristi se VAN Development rezima dok ne stigne ACC modul:
// sve je odbijeno (fail closed).
public sealed class DenyAllAccessDecision : IAccessDecision
{
    public AccessResult Decide(IIdentityContext subject, string action, string? resourceId = null)
    {
        if (!subject.IsAuthenticated)
        {
            return AccessResult.Deny(DenyReason.Unauthenticated);
        }

        return AccessResult.Deny(DenyReason.NotPermitted);
    }
}