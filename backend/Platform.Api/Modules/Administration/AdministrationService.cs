using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Modules.Access;

namespace Platform.Api.Modules.Administration;

public sealed class AdministrationService(IAccountReader accounts, ICourseAdministrationStore store,
    IAccessDecision decisions, ISecurityAudit audit, TimeProvider clock)
{
    public async Task<OperationResult> Execute(Guid? actor, Guid course, string action, string correlation,
        RenameCourseRequest? rename = null, DelegateRequest? delegation = null,
        Guid? grantId = null, RevokeRequest? revoke = null, CancellationToken ct = default)
    {
        OperationResult Finish(int status, string reason, object? value = null, int? before = null, int? after = null)
        {
            audit.Write(new(clock.GetUtcNow(), correlation, actor, action, value is GrantView changedGrant ? changedGrant.Id : grantId ?? course, course,
                status == 200 ? "ALLOW" : status == 409 ? "CONFLICT" : "DENY", reason, "adm08:v1", before, after));
            return new(status, value);
        }
        if (actor is null || actor == Guid.Empty) return Finish(401, "UNAUTHENTICATED");
        if (course == Guid.Empty || AdministrationPolicy.Required(action) == AdminCapability.None)
            return Finish(400, "INVALID_ACTION_OR_COURSE");
        if (action == AdministrationPolicy.Rename && (rename is null || string.IsNullOrWhiteSpace(rename.DisplayName) ||
            rename.DisplayName.Length > 100 || rename.ExpectedVersion < 1 || rename.DisplayName.Any(char.IsControl)))
            return Finish(400, "INVALID_COURSE_INPUT");
        AdminCapability delegatedCaps = AdminCapability.None;
        if (action == AdministrationPolicy.Delegate)
        {
            if (delegation is null || delegation.GranteeId == Guid.Empty || delegation.GranteeId == actor ||
                !TryCapabilities(delegation.Capabilities, out delegatedCaps)) return Finish(400, "INVALID_DELEGATION");
        }
        if (action == AdministrationPolicy.Revoke && (grantId is null || grantId == Guid.Empty || revoke is null || revoke.ExpectedVersion < 1))
            return Finish(400, "INVALID_REVOCATION");
        try
        {
            var account = accounts.FindAccount(actor.Value);
            if (account.Status != PortStatus.Ok || account.Value is not { IsActive: true } || account.Value.Id != actor)
                return Finish(404, account.Status == PortStatus.Unavailable ? "IDENTITY_UNAVAILABLE" : "ACCOUNT_INACTIVE");
            var authority = store.FindAuthority(actor.Value, course);
            if (authority.Status != PortStatus.Ok || authority.Value is null)
                return Finish(404, authority.Status == PortStatus.Unavailable ? "ADMIN_STORE_UNAVAILABLE" : "NO_COURSE_AUTHORITY");
            var decision = await decisions.DecideAsync(new(new(actor.ToString()!, AccountState.Active), action,
                new("course", course.ToString()), correlation), ct);
            if (decision.Outcome != AccessOutcome.Allow) return Finish(404, "ACCESS_DENIED");
            // Adapter binds the final read/write to this exact active authority under its lock.
            if (action == AdministrationPolicy.Read)
            {
                var view = store.ReadAdministration(authority.Value);
                return view.Status == PortStatus.Ok && view.Value is not null ? Finish(200, "ADMIN_VIEW", view.Value) : Finish(404, "AUTHORITY_CHANGED");
            }
            if (delegation is not null)
            {
                var target = accounts.FindAccount(delegation.GranteeId);
                if (target.Status != PortStatus.Ok || target.Value is not { IsActive: true } || target.Value.Id != delegation.GranteeId)
                    return Finish(404, "DELEGATION_TARGET_UNAVAILABLE");
            }
            var result = action switch
            {
                AdministrationPolicy.Rename => store.Rename(authority.Value, rename!),
                AdministrationPolicy.Delegate => store.Delegate(authority.Value, delegation!.GranteeId, delegatedCaps),
                AdministrationPolicy.Revoke => store.Revoke(authority.Value, grantId!.Value, revoke!.ExpectedVersion),
                _ => new AdminWriteResult(WriteStatus.Denied)
            };
            return result.Status switch
            {
                WriteStatus.Updated => Finish(200, "ADMIN_CHANGE", result.Value, result.Before, result.After),
                WriteStatus.Conflict => Finish(409, "VERSION_OR_ACTIVE_GRANT_CONFLICT"),
                _ => Finish(404, "AUTHORITY_OR_TARGET_UNAVAILABLE")
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is TimeoutException or IOException)
        { return Finish(404, "DEPENDENCY_UNAVAILABLE"); }
    }
    public static bool TryCapabilities(string[]? input, out AdminCapability value)
    {
        value = AdminCapability.None;
        if (input is null || input.Length is < 1 or > 2 || input.Distinct(StringComparer.Ordinal).Count() != input.Length) return false;
        foreach (var name in input)
        {
            if (name == "ViewAdministration") value |= AdminCapability.ViewAdministration;
            else if (name == "ManageSettings") value |= AdminCapability.ManageSettings;
            else return false; // In particular, a delegate can never receive ManageDelegations or teaching rights.
        }
        return value.HasFlag(AdminCapability.ViewAdministration);
    }
}
