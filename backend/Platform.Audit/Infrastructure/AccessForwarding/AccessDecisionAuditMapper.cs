using System.Text.RegularExpressions;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Audit.Application;
using Platform.Audit.Contracts;

namespace Platform.Audit.Infrastructure.AccessForwarding;

internal enum AccessAuditMappingStatus
{
    /// <summary>A valid <c>access.denied</c> draft and trusted context were produced.</summary>
    Mapped,

    /// <summary>The decision is not stored in Audit (Allow has no event type in the catalog).</summary>
    NotAudited,

    /// <summary>The subject id is not a user GUID; no actor is invented.</summary>
    InvalidSubject,

    /// <summary>The correlation id does not match the Audit format; no new id is invented.</summary>
    InvalidCorrelationId
}

internal sealed record AccessAuditMapping(
    AccessAuditMappingStatus Status,
    AuditEventDraft? Draft,
    AuditContext? Context)
{
    public static AccessAuditMapping Skipped(AccessAuditMappingStatus status) => new(status, null, null);
}

/// <summary>
/// Maps a Platform.Access decision to an <c>access.denied</c> audit draft (AUD-01, S-13).
/// Pure and stateless. Only catalog-allowed detail keys are produced, and a value that does
/// not match its format is left out instead of being copied into the event.
/// </summary>
internal static partial class AccessDecisionAuditMapper
{
    /// <summary>Resource type used by Platform.Access for courses (see <c>AccessActions</c>).</summary>
    internal const string AccessCourseResourceType = "course";

    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex CorrelationIdPattern();

    public static AccessAuditMapping Map(AccessRequest request, AccessDecision decision)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(decision);

        if (decision.Outcome != AccessOutcome.Deny)
            return AccessAuditMapping.Skipped(AccessAuditMappingStatus.NotAudited);

        if (!TryGetActor(request.Subject, out var actor))
            return AccessAuditMapping.Skipped(AccessAuditMappingStatus.InvalidSubject);

        if (!IsValidCorrelationId(request.CorrelationId))
            return AccessAuditMapping.Skipped(AccessAuditMappingStatus.InvalidCorrelationId);

        // The course comes only from the server-resolved resource of the request, never from details.
        var courseId = CourseIdOf(request.Resource);
        var draft = new AuditEventDraft(
            AuditEventTypes.AccessDenied,
            AuditOutcome.Denied,
            courseId,
            courseId is Guid id ? new AuditResourceRef(AuditResourceTypes.Course, id.ToString("D")) : null,
            DetailsOf(request, decision));

        return new AccessAuditMapping(
            AccessAuditMappingStatus.Mapped,
            draft,
            new AuditContext(actor, request.CorrelationId));
    }

    internal static bool IsValidCorrelationId(string? correlationId) =>
        !string.IsNullOrEmpty(correlationId) && CorrelationIdPattern().IsMatch(correlationId);

    internal static string? DenialReasonOf(AccessDenialReason reason) => reason switch
    {
        AccessDenialReason.Unauthenticated => "unauthenticated",
        AccessDenialReason.AccountNotActive => "account_not_active",
        AccessDenialReason.UnknownAction => "unknown_action",
        AccessDenialReason.NoPolicy => "no_policy",
        AccessDenialReason.PolicyDenied => "policy_denied",
        AccessDenialReason.PolicyError => "policy_error",
        _ => null
    };

    // A missing subject is an anonymous caller. A subject must carry a user GUID: the internal
    // demo subject or any other identifier is not turned into an HTTP user.
    private static bool TryGetActor(SubjectRef? subject, out AuditActor actor)
    {
        if (subject is null)
        {
            actor = AuditActor.Anonymous;
            return true;
        }

        if (Guid.TryParse(subject.Id, out var userId) && userId != Guid.Empty)
        {
            actor = AuditActor.ForUser(userId);
            return true;
        }

        actor = AuditActor.Anonymous;
        return false;
    }

    private static Guid? CourseIdOf(ResourceRef? resource) =>
        resource is not null
        && string.Equals(resource.Type, AccessCourseResourceType, StringComparison.Ordinal)
        && Guid.TryParse(resource.Id, out var courseId)
        && courseId != Guid.Empty
            ? courseId
            : null;

    private static IReadOnlyDictionary<string, string>? DetailsOf(AccessRequest request, AccessDecision decision)
    {
        var details = new Dictionary<string, string>(StringComparer.Ordinal);

        AddIfValid(details, "action", request.Action, AuditDetailFormat.ActionId);
        AddIfValid(details, "denialReason", DenialReasonOf(decision.DenialReason), AuditDetailFormat.AccessDenialReason);
        AddIfValid(details, "policyId", decision.PolicyId, AuditDetailFormat.PolicyId);
        AddIfValid(details, "policyVersion", decision.PolicyVersion, AuditDetailFormat.PolicyVersion);

        return details.Count == 0 ? null : details;
    }

    private static void AddIfValid(
        Dictionary<string, string> details,
        string key,
        string? value,
        AuditDetailFormat format)
    {
        if (value is not null && AuditDetailFormats.IsValid(format, value))
            details[key] = value;
    }
}
