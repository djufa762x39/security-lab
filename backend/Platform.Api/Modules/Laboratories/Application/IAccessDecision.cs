namespace Platform.Api.Modules.Laboratories.Application;

// Ugovor iz specifikacije. Podrazumevano odbijanje.
// KT1: FakeAccessDecision, kasnije ACC modul (tim 3).

public interface IAccessDecision
{
    AccessResult Decide(IIdentityContext subject, string action, string? resourceId = null);
}

public enum DenyReason
{
    Unauthenticated,
    UnknownAction,
    NotPermitted
}

public sealed record AccessResult(bool Allowed, DenyReason? Reason)
{
    public static AccessResult Allow() => new(true, null);

    public static AccessResult Deny(DenyReason reason) => new(false, reason);
}