using NUnit.Framework;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Audit.Application;
using Platform.Audit.Contracts;
using Platform.Audit.Infrastructure.AccessForwarding;
using static Platform.Audit.Tests.AccessForwarding.AccessForwardingExamples;

namespace Platform.Audit.Tests.AccessForwarding;

/// <summary>S-13 mapping: AccessRequest + AccessDecision → access.denied draft and trusted context.</summary>
[TestFixture]
public sealed class AccessDecisionAuditMapperTests
{
    private static readonly string[] AllowedDetailKeys = ["action", "denialReason", "policyId", "policyVersion"];

    [Test]
    public void Map_WhenDenyForAnonymousCaller_ProducesAnonymousAccessDeniedEvent()
    {
        var mapping = AccessDecisionAuditMapper.Map(Request(), Deny(AccessDenialReason.Unauthenticated));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(mapping.Status, Is.EqualTo(AccessAuditMappingStatus.Mapped));
            Assert.That(mapping.Draft!.EventType, Is.EqualTo(AuditEventTypes.AccessDenied));
            Assert.That(mapping.Draft.Outcome, Is.EqualTo(AuditOutcome.Denied));
            Assert.That(mapping.Context!.Actor, Is.EqualTo(AuditActor.Anonymous));
            Assert.That(mapping.Context.CorrelationId, Is.EqualTo(CorrelationA));
            Assert.That(mapping.Draft.Details!["denialReason"], Is.EqualTo("unauthenticated"));
        }
    }

    [Test]
    public void Map_WhenDenyForUser_UsesUserActorFromSubject()
    {
        var mapping = AccessDecisionAuditMapper.Map(Request(UserA), Deny());

        Assert.That(mapping.Context!.Actor, Is.EqualTo(AuditActor.ForUser(UserA)));
    }

    [Test]
    public void Map_WhenDecisionIsAllow_IsNotAudited()
    {
        var mapping = AccessDecisionAuditMapper.Map(Request(UserA), Allow());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(mapping.Status, Is.EqualTo(AccessAuditMappingStatus.NotAudited));
            Assert.That(mapping.Draft, Is.Null);
            Assert.That(mapping.Context, Is.Null);
        }
    }

    [TestCase("demo-subject")]
    [TestCase("00000000-0000-0000-0000-000000000000")]
    [TestCase("")]
    public void Map_WhenSubjectIsNotUserGuid_DoesNotInventActor(string subjectId)
    {
        var mapping = AccessDecisionAuditMapper.Map(Request(subjectId: subjectId), Deny());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(mapping.Status, Is.EqualTo(AccessAuditMappingStatus.InvalidSubject));
            Assert.That(mapping.Draft, Is.Null);
        }
    }

    [TestCase("")]
    [TestCase("bad id with spaces")]
    [TestCase("x\ny")]
    public void Map_WhenCorrelationIdIsInvalid_DoesNotInventNewId(string correlationId)
    {
        var mapping = AccessDecisionAuditMapper.Map(Request(UserA, correlationId: correlationId), Deny(correlationId: correlationId));

        Assert.That(mapping.Status, Is.EqualTo(AccessAuditMappingStatus.InvalidCorrelationId));
    }

    [Test]
    public void Map_WhenCorrelationIdIsLongerThan64_IsInvalid()
    {
        var tooLong = new string('a', 65);

        var mapping = AccessDecisionAuditMapper.Map(Request(UserA, correlationId: tooLong), Deny(correlationId: tooLong));

        Assert.That(mapping.Status, Is.EqualTo(AccessAuditMappingStatus.InvalidCorrelationId));
    }

    [Test]
    public void Map_WhenResourceIsCourse_SetsCourseAndCourseResource()
    {
        var mapping = AccessDecisionAuditMapper.Map(Request(UserA, resource: Course(CourseK)), Deny());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(mapping.Draft!.CourseId, Is.EqualTo(CourseK));
            Assert.That(mapping.Draft.Resource, Is.EqualTo(new AuditResourceRef(AuditResourceTypes.Course, CourseK.ToString("D"))));
        }
    }

    [Test]
    public void Map_WhenResourceIsNotCourse_DoesNotGuessCourse()
    {
        var resource = new ResourceRef("attempt", Guid.NewGuid().ToString("D"));

        var mapping = AccessDecisionAuditMapper.Map(Request(UserA, "attempt.review", resource), Deny());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(mapping.Draft!.CourseId, Is.Null);
            Assert.That(mapping.Draft.Resource, Is.Null);
        }
    }

    [Test]
    public void Map_WhenCourseIdIsNotGuid_LeavesCourseOut()
    {
        var mapping = AccessDecisionAuditMapper.Map(Request(UserA, resource: new ResourceRef("course", "not-a-guid")), Deny());

        Assert.That(mapping.Draft!.CourseId, Is.Null);
    }

    [TestCase(AccessDenialReason.Unauthenticated, "unauthenticated")]
    [TestCase(AccessDenialReason.AccountNotActive, "account_not_active")]
    [TestCase(AccessDenialReason.UnknownAction, "unknown_action")]
    [TestCase(AccessDenialReason.NoPolicy, "no_policy")]
    [TestCase(AccessDenialReason.PolicyDenied, "policy_denied")]
    [TestCase(AccessDenialReason.PolicyError, "policy_error")]
    public void Map_DenialReason_UsesCatalogValue(AccessDenialReason reason, string expected)
    {
        var mapping = AccessDecisionAuditMapper.Map(Request(UserA), Deny(reason));

        Assert.That(mapping.Draft!.Details!["denialReason"], Is.EqualTo(expected));
    }

    [Test]
    public void Map_OnlyProducesCatalogDetailKeys()
    {
        var mapping = AccessDecisionAuditMapper.Map(Request(UserA, resource: Course(CourseK)), Deny());

        Assert.That(mapping.Draft!.Details!.Keys, Is.SubsetOf(AllowedDetailKeys));
    }

    [Test]
    public void Map_WhenPolicyValuesBreakFormat_LeavesThemOutInsteadOfCopying()
    {
        var decision = Deny(policyId: "Bad Policy <script>", policyVersion: "latest");

        var mapping = AccessDecisionAuditMapper.Map(Request(UserA), decision);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(mapping.Draft!.Details!.ContainsKey("policyId"), Is.False);
            Assert.That(mapping.Draft.Details.ContainsKey("policyVersion"), Is.False);
            Assert.That(mapping.Draft.Details.Values, Has.None.Contains("<script>"));
        }
    }

    private static IEnumerable<TestCaseData> MappedCases()
    {
        yield return new TestCaseData(Request(), Deny(AccessDenialReason.Unauthenticated, policyId: null)).SetName("Anonymous");
        yield return new TestCaseData(Request(UserA), Deny()).SetName("UserWithoutResource");
        yield return new TestCaseData(Request(UserA, resource: Course(CourseK)), Deny()).SetName("UserOnCourse");
        yield return new TestCaseData(Request(UserB, "unknown.action"), Deny(AccessDenialReason.UnknownAction, policyId: null))
            .SetName("UnknownAction");
    }

    [TestCaseSource(nameof(MappedCases))]
    public void Map_ResultIsAcceptedByRealAuditValidator(AccessRequest request, AccessDecision decision)
    {
        var mapping = AccessDecisionAuditMapper.Map(request, decision);

        var rejection = new AuditDraftValidator().Validate(mapping.Draft!, mapping.Context!);

        Assert.That(rejection, Is.EqualTo(AuditRejectionCode.None));
    }
}
