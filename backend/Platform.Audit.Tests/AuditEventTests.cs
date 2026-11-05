using NUnit.Framework;
using Platform.Audit.Contracts;
using Platform.Audit.Domain;
using static Platform.Audit.Tests.AuditDraftExamples;

namespace Platform.Audit.Tests;

// S-03 (AUD-01, ADM-07): invariants of a stored audit record.
[TestFixture]
public sealed class AuditEventTests
{
    private static readonly Guid EventId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void Create_WhenInputIsValid_CopiesEveryField()
    {
        var draft = StatusChange();

        var record = AuditEvent.Create(EventId, Now, draft, UserContext);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(record.Id, Is.EqualTo(EventId));
            Assert.That(record.OccurredAt, Is.EqualTo(Now));
            Assert.That(record.ActorKind, Is.EqualTo(AuditActorKind.User));
            Assert.That(record.ActorId, Is.EqualTo(UserId));
            Assert.That(record.EventType, Is.EqualTo(AuditEventTypes.AccountStatusChanged));
            Assert.That(record.Outcome, Is.EqualTo(AuditOutcome.Success));
            Assert.That(record.CourseId, Is.Null);
            Assert.That(record.ResourceType, Is.EqualTo(AuditResourceTypes.Account));
            Assert.That(record.ResourceId, Is.EqualTo(TargetAccountId.ToString()));
            Assert.That(record.CorrelationId, Is.EqualTo(CorrelationId));
            Assert.That(record.Details, Is.EquivalentTo(draft.Details!));
            Assert.That(record.SchemaVersion, Is.EqualTo(AuditEventCatalog.SchemaVersion));
        }
    }

    [Test]
    public void Create_WhenActorIsAnonymous_StoresNoActorId()
    {
        var draft = new AuditEventDraft(AuditEventTypes.AuthLoginThrottled, AuditOutcome.Denied);

        var record = AuditEvent.Create(EventId, Now, draft, AnonymousContext);

        Assert.That(record.ActorId, Is.Null);
    }

    [Test]
    public void Create_WhenDraftHasNoDetails_HasEmptyDetails()
    {
        var draft = new AuditEventDraft(AuditEventTypes.AuthLogout, AuditOutcome.Success);

        var record = AuditEvent.Create(EventId, Now, draft, UserContext);

        Assert.That(record.Details, Is.Empty);
    }

    [Test]
    public void Create_WhenIdIsEmpty_Throws()
    {
        Assert.Throws<ArgumentException>(() => AuditEvent.Create(Guid.Empty, Now, StatusChange(), UserContext));
    }

    [Test]
    public void Create_WhenTimeIsNotUtc_Throws()
    {
        var local = new DateTimeOffset(2026, 11, 3, 13, 0, 0, TimeSpan.FromHours(1));

        Assert.Throws<ArgumentException>(() => AuditEvent.Create(EventId, local, StatusChange(), UserContext));
    }

    [TestCase("")]
    [TestCase("   ")]
    public void Create_WhenEventTypeIsBlank_Throws(string eventType)
    {
        var draft = StatusChange() with { EventType = eventType };

        Assert.Throws<ArgumentException>(() => AuditEvent.Create(EventId, Now, draft, UserContext));
    }

    [Test]
    public void Create_WhenOutcomeIsUndefined_Throws()
    {
        var draft = StatusChange((AuditOutcome)99);

        Assert.Throws<ArgumentException>(() => AuditEvent.Create(EventId, Now, draft, UserContext));
    }

    [Test]
    public void Create_WhenCourseIsEmptyGuid_Throws()
    {
        var draft = StatusChange() with { CourseId = Guid.Empty };

        Assert.Throws<ArgumentException>(() => AuditEvent.Create(EventId, Now, draft, UserContext));
    }

    [TestCase(AuditActorKind.User, null)]
    [TestCase(AuditActorKind.Service, null)]
    [TestCase(AuditActorKind.User, "00000000-0000-0000-0000-000000000000")]
    [TestCase(AuditActorKind.Anonymous, "11111111-1111-1111-1111-111111111111")]
    [TestCase((AuditActorKind)99, "11111111-1111-1111-1111-111111111111")]
    public void Create_WhenActorKindAndIdDoNotMatch_Throws(AuditActorKind kind, string? id)
    {
        var actor = new AuditActor(kind, id is null ? null : Guid.Parse(id));
        var context = new AuditContext(actor, CorrelationId);

        Assert.Throws<ArgumentException>(() => AuditEvent.Create(EventId, Now, StatusChange(), context));
    }

    [TestCase("")]
    [TestCase(" ")]
    public void Create_WhenCorrelationIdIsBlank_Throws(string correlationId)
    {
        var context = new AuditContext(AuditActor.ForUser(UserId), correlationId);

        Assert.Throws<ArgumentException>(() => AuditEvent.Create(EventId, Now, StatusChange(), context));
    }

    [Test]
    public void Create_WhenSchemaVersionIsZero_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AuditEvent.Create(EventId, Now, StatusChange(), UserContext, schemaVersion: 0));
    }

    [Test]
    public void Create_WhenThrowing_DoesNotEchoRejectedValue()
    {
        var context = new AuditContext(AuditActor.ForUser(UserId), " ");
        var draft = StatusChange() with { EventType = "  secret-looking-type " };

        var exception = Assert.Throws<ArgumentException>(() => AuditEvent.Create(EventId, Now, draft, context));

        Assert.That(exception!.Message, Does.Not.Contain("secret-looking-type"));
    }

    [Test]
    public void Details_WhenSourceDictionaryChangesLater_StayUnchanged()
    {
        var source = new Dictionary<string, string> { ["oldStatus"] = "active", ["newStatus"] = "blocked" };
        var record = AuditEvent.Create(EventId, Now, StatusChange(details: source), UserContext);

        source["newStatus"] = "archived";
        source["extra"] = "added";

        Assert.That(record.Details, Is.EquivalentTo(new Dictionary<string, string>
        {
            ["oldStatus"] = "active",
            ["newStatus"] = "blocked"
        }));
    }

    [Test]
    public void Details_CannotBeModifiedThroughTheRecord()
    {
        var record = AuditEvent.Create(EventId, Now, StatusChange(), UserContext);

        Assert.Throws<NotSupportedException>(
            () => ((IDictionary<string, string>)record.Details)["newStatus"] = "archived");
    }

    [Test]
    public void HasSameContentAs_WhenOnlyTimeDiffers_ReturnsTrue()
    {
        var first = AuditEvent.Create(EventId, Now, StatusChange(), UserContext);
        var retry = AuditEvent.Create(EventId, Now.AddSeconds(5), StatusChange(), UserContext);

        Assert.That(first.HasSameContentAs(retry), Is.True);
    }

    [Test]
    public void HasSameContentAs_WhenOutcomeDiffers_ReturnsFalse()
    {
        var first = AuditEvent.Create(EventId, Now, StatusChange(), UserContext);
        var other = AuditEvent.Create(
            EventId,
            Now,
            StatusChange(AuditOutcome.Failure, Details(("reasonCode", "not_found"))),
            UserContext);

        Assert.That(first.HasSameContentAs(other), Is.False);
    }

    [Test]
    public void HasSameContentAs_WhenOneDetailValueDiffers_ReturnsFalse()
    {
        var first = AuditEvent.Create(EventId, Now, StatusChange(), UserContext);
        var other = AuditEvent.Create(
            EventId,
            Now,
            StatusChange(details: Details(("oldStatus", "active"), ("newStatus", "archived"))),
            UserContext);

        Assert.That(first.HasSameContentAs(other), Is.False);
    }

    [Test]
    public void HasSameContentAs_WhenActorDiffers_ReturnsFalse()
    {
        var first = AuditEvent.Create(EventId, Now, StatusChange(), UserContext);
        var other = AuditEvent.Create(
            EventId,
            Now,
            StatusChange(),
            new AuditContext(AuditActor.ForUser(TargetAccountId), CorrelationId));

        Assert.That(first.HasSameContentAs(other), Is.False);
    }
}
