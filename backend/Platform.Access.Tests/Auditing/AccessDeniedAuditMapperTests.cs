using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Security.AccessAudit;
using Platform.Audit.Contracts;

namespace Platform.Access.Tests.Auditing;

[TestFixture]
[Property("RequirementId", "ACC-05")]
public sealed class AccessDeniedAuditMapperTests
{
    private const string UserId = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";

    private readonly AccessDeniedAuditMapper mapper = new();

    private static AccessRequest Request(
        string? subjectId = UserId,
        string action = "course.read",
        ResourceRef? resource = null) =>
        new(subjectId is null ? null : new SubjectRef(subjectId, AccountState.Active), action, resource, "corr-1");

    private static AccessDecision Denied(
        AccessDenialReason reason = AccessDenialReason.PolicyDenied,
        string? policyId = "course.role",
        string policyVersion = "v1") =>
        new(AccessOutcome.Deny, reason, policyId, policyVersion, "corr-1");

    [Test]
    public void Deny_BecomesAccessDeniedWithCatalogDetails()
    {
        var draft = mapper.CreateDraft(Request(), Denied());

        Assert.Multiple(() =>
        {
            Assert.That(draft.EventType, Is.EqualTo(AuditEventTypes.AccessDenied));
            Assert.That(draft.Outcome, Is.EqualTo(AuditOutcome.Denied));
            Assert.That(draft.Details, Is.EquivalentTo(new Dictionary<string, string>
            {
                ["denialReason"] = "policy_denied",
                ["action"] = "course.read",
                ["policyId"] = "course.role",
                ["policyVersion"] = "v1",
            }));
        });
    }

    [Test]
    public void InvalidOptionalDetails_AreOmittedSoTheDenialIsStillRecorded()
    {
        var draft = mapper.CreateDraft(
            Request(action: "Course.Read\r\nforged"),
            Denied(AccessDenialReason.UnknownAction, policyId: "Bad Policy", policyVersion: "1.0"));

        Assert.That(draft.Details, Is.EquivalentTo(new Dictionary<string, string>
        {
            ["denialReason"] = "unknown_action",
        }));
    }

    [TestCase(AccessDenialReason.Unauthenticated, "unauthenticated")]
    [TestCase(AccessDenialReason.AccountNotActive, "account_not_active")]
    [TestCase(AccessDenialReason.UnknownAction, "unknown_action")]
    [TestCase(AccessDenialReason.NoPolicy, "no_policy")]
    [TestCase(AccessDenialReason.PolicyDenied, "policy_denied")]
    [TestCase(AccessDenialReason.PolicyError, "policy_error")]
    public void DenialReason_UsesAuditCatalogCodes(AccessDenialReason reason, string expected)
    {
        Assert.That(mapper.CreateDraft(Request(), Denied(reason)).Details!["denialReason"], Is.EqualTo(expected));
    }

    [Test]
    public void Deny_IsNotAttachedToTheResourceAuditTrail()
    {
        var draft = mapper.CreateDraft(Request(resource: new ResourceRef("revision", "r-1")), Denied());

        Assert.That(draft.Resource, Is.Null);
    }

    [Test]
    public void GuidSubject_IsUserActorWithRequestCorrelation()
    {
        var context = mapper.CreateContext(Request());

        Assert.Multiple(() =>
        {
            Assert.That(context.Actor, Is.EqualTo(AuditActor.ForUser(Guid.Parse(UserId))));
            Assert.That(context.CorrelationId, Is.EqualTo("corr-1"));
        });
    }

    [TestCase(null)]
    [TestCase("user-1")]
    [TestCase("00000000-0000-0000-0000-000000000000")]
    public void SubjectWithoutUserGuid_IsAnonymous(string? subjectId)
    {
        Assert.That(mapper.CreateContext(Request(subjectId)).Actor, Is.EqualTo(AuditActor.Anonymous));
    }
}
