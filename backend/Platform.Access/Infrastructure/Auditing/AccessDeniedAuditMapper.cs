using System.Text.RegularExpressions;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Domain.ValueObjects;
using Platform.Audit.Contracts;

namespace Platform.Access.Infrastructure.Auditing;

internal interface IAccessDeniedAuditMapper
{
    AuditEventDraft CreateDraft(AccessRequest request, AccessDecision decision);

    AuditContext CreateContext(AccessRequest request);
}

internal sealed partial class AccessDeniedAuditMapper : IAccessDeniedAuditMapper
{
    private static readonly string[] ResourceTypes =
    [
        AuditResourceTypes.Account,
        AuditResourceTypes.Course,
        AuditResourceTypes.Delegation,
        AuditResourceTypes.Revision,
    ];

    [GeneratedRegex(@"^[a-z][a-z0-9_.-]{0,63}\z", RegexOptions.CultureInvariant)]
    private static partial Regex PolicyIdPattern();

    [GeneratedRegex(@"^v[0-9]{1,4}(\.[0-9]{1,4}){0,2}\z", RegexOptions.CultureInvariant)]
    private static partial Regex PolicyVersionPattern();

    public AuditEventDraft CreateDraft(AccessRequest request, AccessDecision decision)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(decision);

        var details = new Dictionary<string, string>
        {
            ["denialReason"] = DenialReasonOf(decision.DenialReason),
        };

        if (ActionId.IsValid(request.Action))
        {
            details["action"] = request.Action;
        }

        if (decision.PolicyId is { } policyId && PolicyIdPattern().IsMatch(policyId))
        {
            details["policyId"] = policyId;
        }

        if (decision.PolicyVersion is { } version && PolicyVersionPattern().IsMatch(version))
        {
            details["policyVersion"] = version;
        }

        return new AuditEventDraft(
            AuditEventTypes.AccessDenied,
            AuditOutcome.Denied,
            Resource: ResourceOf(request.Resource),
            Details: details);
    }

    public AuditContext CreateContext(AccessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = Guid.TryParse(request.Subject?.Id, out var userId) && userId != Guid.Empty
            ? AuditActor.ForUser(userId)
            : AuditActor.Anonymous;

        return new AuditContext(actor, request.CorrelationId);
    }

    internal static string DenialReasonOf(AccessDenialReason reason) => reason switch
    {
        AccessDenialReason.Unauthenticated => "unauthenticated",
        AccessDenialReason.AccountNotActive => "account_not_active",
        AccessDenialReason.UnknownAction => "unknown_action",
        AccessDenialReason.NoPolicy => "no_policy",
        AccessDenialReason.PolicyDenied => "policy_denied",
        _ => "policy_error",
    };

    private static AuditResourceRef? ResourceOf(ResourceRef? resource)
    {
        var type = ResourceTypes.FirstOrDefault(known =>
            string.Equals(known, resource?.Type, StringComparison.OrdinalIgnoreCase));

        return type is null ? null : new AuditResourceRef(type, resource!.Id);
    }
}
